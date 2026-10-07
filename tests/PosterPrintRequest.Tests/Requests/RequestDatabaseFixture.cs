using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Tests.Requests;

public sealed class RequestDatabaseFixture : IDisposable
{
    public const string TestDatabaseName = "PosterPrintRequest_RequestTests";

    public string ConnectionString { get; }

    public RequestDatabaseFixture()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryPaths.WebProjectDirectory(), "appsettings.json"), optional: false)
            .Build();

        var configured = configuration.GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Connection string '{PersistenceServiceCollectionExtensions.ConnectionStringName}' is not configured.");
        }

        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = TestDatabaseName
        };
        ConnectionString = builder.ConnectionString;

        using var context = CreateContext();
        context.Database.EnsureDeleted();
        context.Database.Migrate();
    }

    public PosterPrintRequestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PosterPrintRequestDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new PosterPrintRequestDbContext(options);
    }

    public void Dispose()
    {
        using var context = CreateContext();
        context.Database.EnsureDeleted();
    }
}
