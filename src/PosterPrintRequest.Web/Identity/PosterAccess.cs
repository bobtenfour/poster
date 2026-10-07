using Microsoft.AspNetCore.Identity;
using PosterPrintRequest.Infrastructure.Identity;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web;

public static class PosterRoles
{
    public const string User = "User";

    public const string Operator = "Operator";
}

public static class PosterAccess
{
    public const string UserPolicy = "UserFrontend";

    public const string OperatorPolicy = "TechnicianOperations";

    public static IServiceCollection AddPosterAuthentication(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = false;
                if (!environment.IsProduction())
                {
                    options.Password.RequiredLength = 4;
                    options.Password.RequireDigit = false;
                    options.Password.RequireLowercase = false;
                    options.Password.RequireUppercase = false;
                    options.Password.RequireNonAlphanumeric = false;
                }
            })
            .AddEntityFrameworkStores<PosterPrintRequestDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<SessionSecurityOptions>(configuration.GetSection(SessionSecurityOptions.SectionName));
        services.Configure<DemoEvaluationUsersOptions>(configuration.GetSection(DemoEvaluationUsersOptions.SectionName));
        services.ConfigureOptions<ApplicationCookieConfigurator>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(UserPolicy, policy => policy.RequireRole(PosterRoles.User));
            options.AddPolicy(OperatorPolicy, policy => policy.RequireRole(PosterRoles.Operator));
        });
        return services;
    }
}
