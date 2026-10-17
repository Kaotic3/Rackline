using Microsoft.AspNetCore.Authorization;
using Rackline.Application.Common;
using Rackline.Domain.Organisations;

namespace Rackline.Web.Security;

/// <summary>
/// The user is an admin, or holds an appointment with at least this role somewhere. This only
/// decides who may reach staff pages and endpoints; each command then checks the organisation it
/// touches.
/// </summary>
internal sealed class StaffRequirement(StaffRole minimum) : IAuthorizationRequirement
{
    public StaffRole Minimum { get; } = minimum;
}

/// <summary>
/// Reads appointments from the database, so a new appointment counts at once, without signing in again.
/// It uses its own scope (and so its own DbContext): on a server-rendered page, authorisation checks
/// in the layout can run alongside the page's own queries, and a DbContext allows only one at a time.
/// </summary>
internal sealed class StaffRequirementHandler(IServiceScopeFactory scopes) : AuthorizationHandler<StaffRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, StaffRequirement requirement)
    {
        if (context.User.IsInRole(Roles.Admin))
        {
            context.Succeed(requirement);
            return;
        }

        if (context.User.UserId() is not { } userId)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IOrganisationDirectory>();
        if ((await directory.AppointmentsForAsync(userId, CancellationToken.None)).Any(a => a.Role >= requirement.Minimum))
        {
            context.Succeed(requirement);
        }
    }
}
