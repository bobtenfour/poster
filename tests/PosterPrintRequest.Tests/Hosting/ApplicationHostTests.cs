using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class ApplicationHostTests
{
    [Fact]
    public async Task Host_resolves_persistence_storage_and_the_entry_page()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("type=\"password\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Personal activity", html, StringComparison.Ordinal);

        using var signedIn = await PosterSignIn.SignInAsync(factory, "usera");
        var home = await signedIn.GetStringAsync("/");
        Assert.Contains("Internal system for students and staff to request poster printing.", home, StringComparison.Ordinal);

        var error = await client.GetAsync("/Error");
        error.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PosterPrintRequestDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);

        var storage = scope.ServiceProvider.GetRequiredService<IOptions<SharedStorageOptions>>().Value;
        Assert.Equal(@"C:\posters", storage.RootPath);
    }

    [Fact]
    public void Host_uses_a_replaced_storage_root()
    {
        const string replacedRoot = @"\\fileserver\posters";
        using var factory = new PosterWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{SharedStorageOptions.SectionName}:RootPath"] = replacedRoot
                });
            });
        });

        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IOptions<SharedStorageOptions>>().Value;
        Assert.Equal(replacedRoot, storage.RootPath);
    }
}
