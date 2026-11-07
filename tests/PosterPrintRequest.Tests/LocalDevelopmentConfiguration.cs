using Microsoft.Extensions.Configuration;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Tests;

internal static class LocalDevelopmentConfiguration
{
    public static string ConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryPaths.WebProjectDirectory(), "appsettings.Development.json"), optional: false)
            .Build();
        var configured = configuration.GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Connection string '{PersistenceServiceCollectionExtensions.ConnectionStringName}' is not configured.");
        }

        return configured;
    }
}
