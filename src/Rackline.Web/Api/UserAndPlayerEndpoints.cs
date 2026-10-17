using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rackline.Application.Common;
using Rackline.Application.Players;
using Rackline.Infrastructure.Identity;

namespace Rackline.Web.Api;

internal static class UserAndPlayerEndpoints
{
    public static RouteGroupBuilder MapPlayerEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/players", (string? search, IPlayerQueries players, CancellationToken ct) => players.ListAsync(search, ct))
            .WithTags("Players");
        return api;
    }

    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users").WithTags("Users");

        users.MapGet("/me", async (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager) =>
            await userManager.GetUserAsync(principal) is { } user
                ? Results.Ok(new UserDto(user.Id, user.Email, [.. await userManager.GetRolesAsync(user)]))
                : Results.Unauthorized())
            .RequireAuthorization();

        users.MapGet("/", async (string? email, UserManager<ApplicationUser> userManager, CancellationToken ct) =>
        {
            var query = userManager.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(email))
            {
                var normalised = userManager.NormalizeEmail(email);
                query = query.Where(u => u.NormalizedEmail == normalised);
            }

            return await query.OrderBy(u => u.Email).Select(u => new { u.Id, u.Email }).ToListAsync(ct);
        })
        .RequireAuthorization(Policies.ManageUsers);

        users.MapPut("/{id:guid}/roles", async (Guid id, SetRolesRequest request, UserManager<ApplicationUser> userManager) =>
        {
            var known = Roles.All.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = request.Roles.Where(r => !known.Contains(r)).ToList();
            if (unknown.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(SetRolesRequest.Roles)] = [$"Unknown role(s): {string.Join(", ", unknown)}."],
                });
            }

            var user = await userManager.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User", id);
            var wanted = request.Roles.Select(r => known.First(k => k.Equals(r, StringComparison.OrdinalIgnoreCase))).ToHashSet();
            var current = await userManager.GetRolesAsync(user);

            await userManager.RemoveFromRolesAsync(user, current.Except(wanted));
            await userManager.AddToRolesAsync(user, wanted.Except(current));
            return Results.Ok(new UserDto(user.Id, user.Email, [.. await userManager.GetRolesAsync(user)]));
        })
        .RequireAuthorization(Policies.ManageUsers);

        return api;
    }
}
