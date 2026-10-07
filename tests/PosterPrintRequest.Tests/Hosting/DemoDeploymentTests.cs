using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Identity;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class DemoDeploymentTests
{
    [Fact]
    public void Demo_configuration_targets_poster_database_storage_and_host()
    {
        var root = RepositoryRoot();
        var demo = File.ReadAllText(Path.Combine(root, "src", "PosterPrintRequest.Web", "appsettings.Demo.json"));
        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.demo.yml"));
        var caddy = File.ReadAllText(Path.Combine(root, "docker", "caddy", "poster.blueignix.caddy"));
        var web = File.ReadAllText(Path.Combine(root, "docker", "web-entrypoint.sh"));
        var migrate = File.ReadAllText(Path.Combine(root, "docker", "migrate-entrypoint.sh"));

        Assert.Contains("\"AllowedHosts\": \"poster.blueignix.com\"", demo, StringComparison.Ordinal);
        Assert.Contains("\"RootPath\": \"/var/poster-print-request\"", demo, StringComparison.Ordinal);
        Assert.Contains("\"usera\"", demo, StringComparison.Ordinal);
        Assert.Contains("\"usero\"", demo, StringComparison.Ordinal);
        Assert.Contains("\"prueba1\"", demo, StringComparison.Ordinal);

        Assert.Contains("127.0.0.1:$${PORT:-8080}/health/ready", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("$${PORT:-8080}/health\\\"", compose, StringComparison.Ordinal);
        Assert.Contains("Database=POSTER", compose, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:${DEMO_HOST_PORT:-8083}:${PORT:-8080}", compose, StringComparison.Ordinal);
        Assert.Contains("posterprintrequest-demo-storage:/var/poster-print-request", compose, StringComparison.Ordinal);
        Assert.Contains("name: dentalinventory-demo_default", compose, StringComparison.Ordinal);
        Assert.Contains("external: true", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("mcr.microsoft.com/mssql/server", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("DentalInventoryDemo", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyInventoryDemo", compose, StringComparison.Ordinal);

        Assert.Contains("poster.blueignix.com {", caddy, StringComparison.Ordinal);
        Assert.Contains("reverse_proxy 127.0.0.1:8083", caddy, StringComparison.Ordinal);
        Assert.Contains("database must be POSTER", web, StringComparison.Ordinal);
        Assert.Contains("/var/poster-print-request", web, StringComparison.Ordinal);
        Assert.Contains("database must be POSTER", migrate, StringComparison.Ordinal);

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src", "PosterPrintRequest.Web", "appsettings.Demo.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PosterPrintRequest"] =
                    "Server=sqlserver,1433;Database=POSTER;User Id=sa;Password=secret;TrustServerCertificate=True;Encrypt=False"
            })
            .Build();
        Assert.Contains("Database=POSTER", configuration.GetConnectionString("PosterPrintRequest"), StringComparison.Ordinal);
        Assert.Equal("/var/poster-print-request", configuration["SharedStorage:RootPath"]);
        Assert.Equal("poster.blueignix.com", configuration["AllowedHosts"]);
        Assert.True(configuration.GetValue<bool>("DemoEvaluationUsers:Enabled"));
        Assert.Equal("usera", configuration["DemoEvaluationUsers:UserNames:0"]);
        Assert.Equal("usero", configuration["DemoEvaluationUsers:UserNames:1"]);
    }

    [Fact]
    public async Task Demo_host_is_healthy_and_keeps_requester_and_technician_apart()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), "poster-demo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            using var factory = new DemoFactory(storageRoot);
            using (var scope = factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                Assert.NotNull(services.GetRequiredService<PosterPrintRequestDbContext>());
                Assert.NotNull(services.GetRequiredService<AcceptedStorage>());
                Assert.NotNull(services.GetRequiredService<ITechnicianLibrary>());
                Assert.NotNull(services.GetRequiredService<UserManager<ApplicationUser>>());
                Assert.Equal(storageRoot, services.GetRequiredService<IOptions<SharedStorageOptions>>().Value.RootPath);
                var accounts = services.GetRequiredService<IOptions<DemoEvaluationUsersOptions>>().Value;
                Assert.True(accounts.Enabled);
                Assert.Equal(["usera", "usero"], accounts.UserNames);
            }

            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = true,
                HandleCookies = true,
                BaseAddress = new Uri("http://poster.blueignix.com")
            });
            foreach (var path in new[] { "/health", "/health/ready" })
            {
                var health = await client.GetAsync(path);
                health.EnsureSuccessStatusCode();
                var healthBody = await health.Content.ReadAsStringAsync();
                Assert.Contains("Healthy", healthBody, StringComparison.Ordinal);
                Assert.DoesNotContain(storageRoot, healthBody, StringComparison.Ordinal);
                Assert.DoesNotContain("/var/poster-print-request", healthBody, StringComparison.Ordinal);
            }

            var requester = await PosterSignIn.PostCredentialsAsync(client, "usera", PosterSignIn.Password);
            requester.EnsureSuccessStatusCode();
            var requesterHome = await requester.Content.ReadAsStringAsync();
            Assert.Contains("Personal activity", requesterHome, StringComparison.Ordinal);
            Assert.DoesNotContain("Posters pending printing", requesterHome, StringComparison.Ordinal);
            var requesterDenied = await client.GetAsync("/technician");
            Assert.Contains("/Account/AccessDenied", requesterDenied.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);

            using var technicianClient = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = true,
                HandleCookies = true,
                BaseAddress = new Uri("http://poster.blueignix.com")
            });
            var technician = await PosterSignIn.PostCredentialsAsync(technicianClient, "usero", PosterSignIn.Password);
            technician.EnsureSuccessStatusCode();
            var technicianHome = await technician.Content.ReadAsStringAsync();
            Assert.Contains("Posters pending printing", technicianHome, StringComparison.Ordinal);
            Assert.DoesNotContain("Personal activity", technicianHome, StringComparison.Ordinal);
            var technicianDenied = await technicianClient.GetAsync("/");
            Assert.Contains("/Account/AccessDenied", technicianDenied.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
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

    private sealed class DemoFactory : WebApplicationFactory<Program>
    {
        private readonly string _storageRoot;

        public DemoFactory(string storageRoot) => _storageRoot = storageRoot;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Demo");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{SharedStorageOptions.SectionName}:RootPath"] = _storageRoot
                });
            });
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<SharedStorageOptions>(options => options.RootPath = _storageRoot);
            });
        }
    }
}
