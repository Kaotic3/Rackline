using Microsoft.AspNetCore.Identity;
using Rackline.Application.Common;

namespace Rackline.Infrastructure.Identity;

public static class AdminSeeder
{
    /// <summary>
    /// Makes sure an administrator exists. The email and password come from configuration
    /// (user-secrets or environment variables), never from the repository.
    /// </summary>
    public static async Task EnsureAdminAsync(UserManager<ApplicationUser> users, string email, string password)
    {
        var admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            ThrowIfFailed(await users.CreateAsync(admin, password));
        }

        if (!await users.IsInRoleAsync(admin, Roles.Admin))
        {
            ThrowIfFailed(await users.AddToRoleAsync(admin, Roles.Admin));
        }
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create the administrator: " + string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
