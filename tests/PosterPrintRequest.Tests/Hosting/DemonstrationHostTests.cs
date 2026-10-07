using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
        using var local = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await local.GetAsync("/health")).StatusCode);

        using var publicClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://poster.blueignix.com")
        });

        var plain = await publicClient.GetAsync("/request");
        Assert.Equal(HttpStatusCode.Found, plain.StatusCode);
        Assert.Equal(
            "http://poster.blueignix.com/Account/Login?ReturnUrl=%2Frequest",
            plain.Headers.Location?.ToString());

        var secureRequest = new HttpRequestMessage(HttpMethod.Get, "/request");
        secureRequest.Headers.Add("X-Forwarded-Proto", "https");
        var secure = await publicClient.SendAsync(secureRequest);
        Assert.Equal(HttpStatusCode.Found, secure.StatusCode);
        Assert.Equal(
            "https://poster.blueignix.com/Account/Login?ReturnUrl=%2Frequest",
            secure.Headers.Location?.ToString());
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
