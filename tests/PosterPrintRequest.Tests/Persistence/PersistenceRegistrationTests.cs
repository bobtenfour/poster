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
}
