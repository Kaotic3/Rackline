using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rackline.Application.Common;
using Rackline.Application.Tournaments;
using Rackline.Infrastructure.Identity;
using Rackline.Infrastructure.Persistence;

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

        // API callers using the cookie get 401/403, not a redirect to a login page.
        services.ConfigureApplicationCookie(o =>
        {
            o.Events.OnRedirectToLogin = ctx => StatusOrRedirect(ctx, StatusCodes.Status401Unauthorized);
            o.Events.OnRedirectToAccessDenied = ctx => StatusOrRedirect(ctx, StatusCodes.Status403Forbidden);
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.ManageTournaments, p => p.RequireRole(Roles.Admin, Roles.Organiser))
            .AddPolicy(Policies.ScoreMatches, p => p.RequireRole(Roles.Admin, Roles.Organiser, Roles.Referee))
            .AddPolicy(Policies.ManageUsers, p => p.RequireRole(Roles.Admin));

        return services;
    }

    public static WebApplication MapRacklineApi(this WebApplication app)
    {
        app.MapGroup("/api/auth").WithTags("Auth").MapIdentityApi<ApplicationUser>();

        app.MapGroup("/api")
            .MapTournamentEndpoints()
            .MapPlayerEndpoints()
            .MapUserEndpoints();

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

    private static Task StatusOrRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> context, int status)
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
