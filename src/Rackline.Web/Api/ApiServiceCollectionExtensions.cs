using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rackline.Application.Challenges;
using Rackline.Application.Common;
using Rackline.Application.Leagues;
using Rackline.Application.Live;
using Rackline.Application.Organisations;
using Rackline.Application.Rankings;
using Rackline.Application.Tournaments;
using Rackline.Infrastructure.Identity;
using Rackline.Infrastructure.Live;
using Rackline.Infrastructure.Persistence;
using Rackline.Domain.Organisations;
using Rackline.Web.Live;
using Rackline.Web.Security;

namespace Rackline.Web.Api;

internal static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddRacklineApi(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<TournamentLifecycle>();
        services.AddScoped<TournamentEntries>();
        services.AddScoped<TournamentDraws>();
        services.AddScoped<MatchResults>();
        services.AddScoped<LiveScoring>();
        services.AddScoped<LeagueSetup>();
        services.AddScoped<LeagueResults>();
        services.AddScoped<RankingSetup>();
        services.AddScoped<RatingRebuild>();
        services.AddScoped<OrganisationSetup>();
        services.AddScoped<StaffAppointments>();
        services.AddScoped<PlayerRegistry>();
        services.AddScoped<ChallengeRecording>();

        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.AddScoped<OrganisationAccess>();
        services.AddSingleton<IAuthorizationHandler, StaffRequirementHandler>();

        services.AddSingleton<LiveFeed>();
        services.AddScoped<ILiveNotifier, SignalRLiveNotifier<LiveHub>>();
        services.AddSignalR()
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddValidation();
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddIdentityApiEndpoints<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<RacklineDbContext>();

        // Challenges go through the cookie handler: pages redirect to the sign-in page, and its events
        // below turn that into a plain 401/403 for /api, so bearer-token callers are unaffected.
        services.Configure<AuthenticationOptions>(o =>
        {
            o.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            o.DefaultForbidScheme = IdentityConstants.ApplicationScheme;
        });

        // API callers get 401/403, not a redirect to a login page.
        services.ConfigureApplicationCookie(o =>
        {
            o.LoginPath = "/account/login";
            o.AccessDeniedPath = "/account/access-denied";
            o.Events.OnRedirectToLogin = ctx => StatusOrRedirect(ctx, StatusCodes.Status401Unauthorized);
            o.Events.OnRedirectToAccessDenied = ctx => StatusOrRedirect(ctx, StatusCodes.Status403Forbidden);
        });

        services.AddCascadingAuthenticationState();
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.ManageTournaments, Staff(StaffRole.Organiser))
            .AddPolicy(Policies.ScoreMatches, Staff(StaffRole.Referee))
            .AddPolicy(Policies.ManageLeagues, Staff(StaffRole.Organiser))
            .AddPolicy(Policies.ManageRankings, Staff(StaffRole.Organiser))
            .AddPolicy(Policies.ManageOrganisations, Staff(StaffRole.Organiser))
            .AddPolicy(Policies.RebuildRatings, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.ManageUsers, p => p.RequireRole(Roles.Admin));

        return services;
    }

    public static WebApplication MapRacklineApi(this WebApplication app)
    {
        app.MapGroup("/api/auth").WithTags("Auth").MapIdentityApi<ApplicationUser>();

        app.MapGroup("/api")
            .MapTournamentEndpoints()
            .MapPlayerEndpoints()
            .MapUserEndpoints()
            .MapLeagueEndpoints()
            .MapRankingEndpoints()
            .MapOrganisationEndpoints()
            .MapChallengeEndpoints();

        app.MapHub<LiveHub>("/hubs/live");

        // Sign-out for the pages: a form post, protected by antiforgery.
        app.MapPost("/account/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.LocalRedirect("/");
        });
        return app;
    }

    /// <summary>
    /// For /api requests, swaps the HTML "not found" page for a problem response whenever an
    /// error status has no body (e.g. 401 or 403 from authorisation).
    /// </summary>
    public static IApplicationBuilder UseApiProblemResponses(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await next(context);
                return;
            }

            if (context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
            {
                statusCodePages.Enabled = false;
            }

            await next(context);

            if (context.Response.StatusCode >= 400 && !context.Response.HasStarted
                && context.Response.ContentLength is null && string.IsNullOrEmpty(context.Response.ContentType))
            {
                var problems = context.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context });
            }
        });

    /// <summary>Signed in, and an admin or holding at least this role somewhere; the command checks where.</summary>
    private static Action<AuthorizationPolicyBuilder> Staff(StaffRole minimum) =>
        p => p.RequireAuthenticatedUser().AddRequirements(new StaffRequirement(minimum));

    private static Task StatusOrRedirect(RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> context, int status)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = status;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }
}
