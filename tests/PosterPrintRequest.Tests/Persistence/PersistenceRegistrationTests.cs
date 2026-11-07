using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Tests.Persistence;

public sealed class PersistenceRegistrationTests
{
    [Fact]
    public void Missing_connection_string_is_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}"] = " "
            })
            .Build();

        var services = new ServiceCollection();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddPosterPrintRequestPersistence(configuration));

        Assert.Contains(
            PersistenceServiceCollectionExtensions.ConnectionStringName,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_rejects_sql_authentication_and_a_database_other_than_poster()
    {
        var sqlPassword = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}"] =
                    "Server=sql.example;Database=POSTER;User Id=poster;Password=secret"
            })
            .Build();
        var wrongDatabase = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}"] =
                    "Server=sql.example;Database=PosterPrintRequest;Integrated Security=True"
            })
            .Build();
        var integrated = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}"] =
                    "Server=sql.example;Database=POSTER;Integrated Security=True;Encrypt=True"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => PosterSqlConnection.Require(sqlPassword, requireIntegratedAuthentication: true));
        Assert.Throws<InvalidOperationException>(() => PosterSqlConnection.Require(wrongDatabase, requireIntegratedAuthentication: true));
        Assert.Contains("Database=POSTER", PosterSqlConnection.Require(integrated, requireIntegratedAuthentication: true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=secret", PosterSqlConnection.Require(sqlPassword, requireIntegratedAuthentication: false), StringComparison.Ordinal);
    }

    [Fact]
    public void Local_sql_stays_in_development_configuration()
    {
        var web = RepositoryPaths.WebProjectDirectory();
        var production = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var development = File.ReadAllText(Path.Combine(web, "appsettings.Development.json"));

        Assert.DoesNotContain("ConnectionStrings", production, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=localhost", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", production, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("\"PosterPrintRequest\"", development, StringComparison.Ordinal);
        Assert.Contains("Server=localhost", development, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Database=PosterPrintRequest", development, StringComparison.Ordinal);
        Assert.Contains("Integrated Security=True", development, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", development, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User Id", development, StringComparison.OrdinalIgnoreCase);
    }
}
