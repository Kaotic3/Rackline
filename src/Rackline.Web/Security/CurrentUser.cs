using System.Security.Claims;
using Rackline.Application.Common;

namespace Rackline.Web.Security;

/// <summary>
/// The signed-in user for this scope: the request's user, or, for work done from an interactive page
/// (where there is no request), the user that page passes in.
/// </summary>
internal sealed class CurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    /// <summary>Set by interactive pages, which have no HTTP request to read the user from.</summary>
    public ClaimsPrincipal? Principal { get; set; }

    private ClaimsPrincipal? User => Principal ?? http.HttpContext?.User;

    public Guid? UserId => User.UserId();

    public bool IsAdmin => User?.IsInRole(Roles.Admin) == true;
}

internal static class ClaimsPrincipalExtensions
{
    public static Guid? UserId(this ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
