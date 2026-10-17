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
        var isApi = httpContext.Request.Path.StartsWithSegments("/api");

        (int Status, string Title, string? Detail)? problem = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.Message),
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
            logger.LogError(exception, "Unhandled exception for {Path}", httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = p.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = p.Status, Title = p.Title, Detail = p.Detail },
        });
    }
}
