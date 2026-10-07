using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Identity;

namespace PosterPrintRequest.Web;

public static class DemoEvaluationUsersSeeder
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly IReadOnlyDictionary<string, string> RoleByUser =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["usera"] = PosterRoles.User,
            ["usero"] = PosterRoles.Operator
        };

    public static async Task SeedAsync(IServiceProvider services, IHostEnvironment environment, ILogger logger)
    {
        if (environment.IsProduction())
        {
            logger.LogWarning("Temporary evaluation accounts are not created in Production.");
            return;
        }

        var options = services.GetRequiredService<IOptions<DemoEvaluationUsersOptions>>().Value;
        if (!options.Enabled || string.IsNullOrEmpty(options.Password))
        {
            return;
        }

        await Gate.WaitAsync();
        try
        {
            await using var scope = services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var roleName in new[] { PosterRoles.User, PosterRoles.Operator })
            {
                if (await roles.RoleExistsAsync(roleName))
                {
                    continue;
                }

                var createdRole = await roles.CreateAsync(new IdentityRole(roleName));
                if (!createdRole.Succeeded)
                {
                    throw new InvalidOperationException(Describe("The role " + roleName + " could not be created.", createdRole));
                }
            }

            foreach (var userName in options.UserNames)
            {
                if (string.IsNullOrWhiteSpace(userName) || !RoleByUser.TryGetValue(userName.Trim(), out var roleName))
                {
                    continue;
                }

                var normalized = userName.Trim();
                var user = await users.FindByNameAsync(normalized);
                if (user is null)
                {
                    user = new ApplicationUser
                    {
                        UserName = normalized,
                        EmailConfirmed = true
                    };
                    var created = await users.CreateAsync(user, options.Password);
                    if (!created.Succeeded)
                    {
                        user = await users.FindByNameAsync(normalized);
                        if (user is null)
                        {
                            throw new InvalidOperationException(Describe("The account " + normalized + " could not be created.", created));
                        }
                    }
                }
                else if (!await users.CheckPasswordAsync(user, options.Password))
                {
                    var token = await users.GeneratePasswordResetTokenAsync(user);
                    var reset = await users.ResetPasswordAsync(user, token, options.Password);
                    if (!reset.Succeeded)
                    {
                        throw new InvalidOperationException(Describe("The account " + normalized + " could not be updated.", reset));
                    }
                }

                if (!await users.IsInRoleAsync(user, roleName))
                {
                    var assigned = await users.AddToRoleAsync(user, roleName);
                    if (!assigned.Succeeded)
                    {
                        throw new InvalidOperationException(Describe("The account " + normalized + " could not receive its role.", assigned));
                    }
                }
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string Describe(string message, IdentityResult result) =>
        message + " " + string.Join(" ", result.Errors.Select(error => error.Description));
}
