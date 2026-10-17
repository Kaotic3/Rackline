using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Rackline.Application.Common;
using Rackline.Domain.Common;

namespace Rackline.Web.Api;

/// <summary>Turns exceptions from the application and domain into RFC 9457 problem responses.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Outside Development the request path has already been rewritten to the error page by the
        // time handlers run, so use the path the request originally had.
        var path = httpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? httpContext.Request.Path;
        var isApi = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.Equals("/api", StringComparison.OrdinalIgnoreCase);

        (int Status, string Title, string? Detail)? problem = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, "Forbidden", e.Message),
            DomainException e => (StatusCodes.Status409Conflict, "Not allowed", e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, "Conflicting change", e.Message),
            BadHttpRequestException e when isApi => (e.StatusCode, "Bad request", e.Message),
            _ when isApi => (StatusCodes.Status500InternalServerError, "Something went wrong", null),
            _ => null,
        };

        if (problem is not { } p)
        {
            return false;
        }

        if (p.Status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Path}", path);
        }

        httpContext.Response.StatusCode = p.Status;
        var details = new ProblemDetails { Status = p.Status, Title = p.Title, Detail = p.Detail };
        if (await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, Exception = exception, ProblemDetails = details }))
        {
            return true;
        }

        // Belt and braces: API callers must get JSON even if the shared writer declines.
        await httpContext.Response.WriteAsJsonAsync(details, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
        return true;
    }
}
