using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Web;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class DemonstrationHostTests
{
    [Fact]
    public async Task Demonstration_host_is_poster_blueignix_com()
    {
        var root = RepositoryRoot();
        var fragment = File.ReadAllText(Path.Combine(root, "docker", "caddy", "poster.blueignix.caddy"));
        Assert.Contains("poster.blueignix.com {", fragment, StringComparison.Ordinal);
        Assert.Contains("reverse_proxy 127.0.0.1:8083", fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("dinventory.blueignix.com {", fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("kinventory.blueignix.com {", fragment, StringComparison.Ordinal);
        Assert.Contains(
            "\"AllowedHosts\": \"poster.blueignix.com\"",
            File.ReadAllText(Path.Combine(root, "src", "PosterPrintRequest.Web", "appsettings.Demo.json")),
            StringComparison.Ordinal);

        using var factory = new DemonstrationFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var schemes = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
            Assert.Equal(NegotiateDefaults.AuthenticationScheme, schemes.DefaultScheme);
            Assert.IsType<WindowsGroupVerifier>(scope.ServiceProvider.GetRequiredService<IInstitutionalGroupVerifier>());
            Assert.IsType<WindowsActorSource>(scope.ServiceProvider.GetRequiredService<IInstitutionalActorSource>());
        }

        using var local = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await local.GetAsync("/health")).StatusCode);

        using var publicClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://poster.blueignix.com")
        });

        var thrown = await Assert.ThrowsAsync<NotSupportedException>(() => publicClient.GetAsync("/request"));
        Assert.Contains("Negotiate authentication requires a server", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/Account/Login", thrown.ToString(), StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PosterPrintRequest.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private sealed class DemonstrationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment("Demo");
    }
}
