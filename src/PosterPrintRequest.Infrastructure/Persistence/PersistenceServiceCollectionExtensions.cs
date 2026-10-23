using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PosterPrintRequest.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "PosterPrintRequest";

    public static IServiceCollection AddPosterPrintRequestPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireIntegratedAuthentication = false)
    {
        var connectionString = PosterSqlConnection.Require(configuration, requireIntegratedAuthentication);

        services.AddDbContext<PosterPrintRequestDbContext>(options =>
            options.UseSqlServer(connectionString));

        return services;
    }
}
