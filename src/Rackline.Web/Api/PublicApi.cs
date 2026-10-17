using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Rackline.Web.Api;

/// <summary>
/// The public, read-only face of the API: anyone may read it from any site, it is described by an
/// OpenAPI document, and anonymous callers are rate limited so one heavy user cannot slow the site.
/// </summary>
internal static class PublicApi
{
    public const string CorsPolicy = "PublicReads";
    public const string RateLimitPolicy = "Anonymous";
    public const string DocumentName = "v1";
    public const int DefaultPermitsPerMinute = 120;

    public static IServiceCollection AddPublicApi(this IServiceCollection services, IConfiguration configuration)
    {
        // Reads only, and never with cookies, so another site cannot act as a signed-in user.
        services.AddCors(o => o.AddPolicy(CorsPolicy, p => p.AllowAnyOrigin().WithMethods("GET").AllowAnyHeader()));

        var permits = configuration.GetValue("PublicApi:PermitsPerMinute", DefaultPermitsPerMinute);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                http.Response.Headers.RetryAfter = "60";
                var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = $"Anonymous callers may make {permits} API requests a minute. Wait a minute and try again.",
                    },
                });
            };

            // Signed-in staff are not limited; everyone else is limited per address. This covers the
            // sign-in endpoints too, which slows down password guessing.
            o.AddPolicy(RateLimitPolicy, http => http.User.Identity?.IsAuthenticated == true
                ? RateLimitPartition.GetNoLimiter("signed-in")
                : RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        services.AddOpenApi(DocumentName, o =>
        {
            // Only what anyone may read: GETs under /api that need no sign-in, leaving out the account endpoints.
            o.ShouldInclude = d => d.HttpMethod == HttpMethods.Get
                && d.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true
                && !d.RelativePath.StartsWith("api/auth/", StringComparison.Ordinal)
                && !d.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any();

            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Rackline public API";
                document.Info.Version = "1";
                document.Info.Description =
                    "Read-only access to Rackline's tournaments, leagues, rankings, ratings, players, organisations and challenges. " +
                    "No key is needed. Responses are JSON, enums are strings and times are UTC. " +
                    $"Anonymous callers may make {permits} requests a minute.";
                return Task.CompletedTask;
            });
        });

        return services;
    }
}
