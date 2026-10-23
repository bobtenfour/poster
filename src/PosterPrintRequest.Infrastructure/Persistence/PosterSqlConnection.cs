using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace PosterPrintRequest.Infrastructure.Persistence;

public static class PosterSqlConnection
{
    public static string Require(IConfiguration configuration, bool requireIntegratedAuthentication)
    {
        var connectionString = configuration.GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{PersistenceServiceCollectionExtensions.ConnectionStringName}' is not configured.");
        }

        if (!requireIntegratedAuthentication)
        {
            return connectionString;
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!builder.IntegratedSecurity
            || !string.IsNullOrEmpty(builder.UserID)
            || !string.IsNullOrEmpty(builder.Password)
            || !string.Equals(builder.InitialCatalog, "POSTER", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Production SQL connections use Windows Integrated Authentication to database POSTER. "
                + "Set ConnectionStrings__PosterPrintRequest in deployment configuration. "
                + "The application does not store the institutional server name or a SQL password.");
        }

        return connectionString;
    }
}
