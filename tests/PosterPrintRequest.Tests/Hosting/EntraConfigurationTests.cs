using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Tests.Workflow;
using PosterPrintRequest.Web;

namespace PosterPrintRequest.Tests.Hosting;

/// <summary>Authentication Contract sections 5, 6.7, and 8: OIDC configuration and the startup refusals.</summary>
[Collection("Workflow database")]
public sealed class EntraConfigurationTests
{
    private readonly WorkflowDatabaseFixture _database;

    public EntraConfigurationTests(WorkflowDatabaseFixture database)
    {
        _database = database;
    }

    private static ServiceProvider Build(string environment, string? secretFile, string? tenant = TestEntra.TenantId, string? client = TestEntra.ClientId)
    {
        var values = new Dictionary<string, string?>
        {
            [EntraSettings.TenantIdKey] = tenant,
            [EntraSettings.ClientIdKey] = client,
            [EntraSettings.ClientSecretFileKey] = secretFile
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPosterAuthentication(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new TestEnvironment(environment));
        return services.BuildServiceProvider();
    }

    private static OpenIdConnectOptions Options(ServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(EntraSettings.ChallengeScheme);

    [Fact]
    public void Production_is_a_confidential_client_on_the_authorization_code_flow_with_pkce()
    {
        using var secret = new ProtectedSecretFile();
        using var provider = Build("Production", secret.Path);

        var options = Options(provider);

        Assert.Equal(OpenIdConnectResponseType.Code, options.ResponseType);
        Assert.DoesNotContain("id_token", options.ResponseType, StringComparison.Ordinal);
        Assert.True(options.UsePkce);
        Assert.Equal("login", options.Prompt);
        Assert.Equal(new PathString("/signin-oidc"), options.CallbackPath);
        Assert.Equal(TestEntra.ClientId, options.ClientId);
        Assert.Equal(ProtectedSecretFile.TestSecret, options.ClientSecret);
        Assert.Equal("https://login.microsoftonline.com/" + TestEntra.TenantId + "/v2.0", options.Authority);
        Assert.False(options.UseTokenLifetime);
        Assert.False(options.MapInboundClaims);
        Assert.False(options.SaveTokens);
        Assert.False(options.GetClaimsFromUserInfoEndpoint);
        Assert.Equal(["openid", "profile"], options.Scope.Order().ToArray());
        Assert.Equal("roles", options.TokenValidationParameters.RoleClaimType);
        Assert.Equal("Cookies", options.SignInScheme);
        Assert.False(options.SignedOutCallbackPath.HasValue);
        Assert.False(options.RemoteSignOutPath.HasValue);
    }

    [Fact]
    public void Development_is_a_public_client_with_no_secret_and_ignores_a_secret_file()
    {
        using var secret = new ProtectedSecretFile();
        using var provider = Build("Development", secret.Path);

        var options = Options(provider);

        Assert.Null(options.ClientSecret);
        Assert.Equal(OpenIdConnectResponseType.Code, options.ResponseType);
        Assert.True(options.UsePkce);
        Assert.Equal("login", options.Prompt);
        Assert.False(options.UseTokenLifetime);
        Assert.False(options.MapInboundClaims);
        Assert.Equal("roles", options.TokenValidationParameters.RoleClaimType);
    }

    [Fact]
    public void Development_needs_no_secret_file()
    {
        using var provider = Build("Development", secretFile: null);

        Assert.Null(Options(provider).ClientSecret);
    }

    [Theory]
    [InlineData(null, TestEntra.ClientId, "The Entra tenant identifier is not configured.")]
    [InlineData("   ", TestEntra.ClientId, "The Entra tenant identifier is not configured.")]
    [InlineData("tenant/../evil", TestEntra.ClientId, "The Entra tenant identifier is not valid.")]
    [InlineData(TestEntra.TenantId, null, "The Entra client identifier is not configured.")]
    [InlineData(TestEntra.TenantId, "not-a-guid", "The Entra client identifier is not valid.")]
    public void Both_environments_refuse_a_missing_or_malformed_tenant_or_client_identifier(string? tenant, string? client, string message)
    {
        using var secret = new ProtectedSecretFile();
        foreach (var environment in new[] { "Production", "Development" })
        {
            using var provider = Build(environment, secret.Path, tenant, client);

            var refusal = Assert.Throws<EntraConfigurationException>(() => Options(provider));

            Assert.Equal(message, refusal.Message);
        }
    }

    [Fact]
    public void Production_refuses_when_the_secret_file_is_not_configured()
    {
        foreach (var value in new string?[] { null, "", "   " })
        {
            using var provider = Build("Production", value);

            var refusal = Assert.Throws<EntraConfigurationException>(() => Options(provider));

            Assert.Equal("The Entra client secret file is not configured.", refusal.Message);
        }
    }

    [Fact]
    public void Production_refuses_a_missing_file_without_naming_the_path()
    {
        var missing = Path.Combine(Path.GetTempPath(), "poster-entra-missing-" + Guid.NewGuid().ToString("N"), "secret.bin");
        using var provider = Build("Production", missing);

        var refusal = Assert.Throws<EntraConfigurationException>(() => Options(provider));

        Assert.Equal("The Entra client secret file is missing.", refusal.Message);
        Assert.DoesNotContain(missing, refusal.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetFileName(missing), refusal.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_refuses_an_unreadable_file()
    {
        using var secret = new ProtectedSecretFile();
        using var locked = new FileStream(secret.Path, FileMode.Open, FileAccess.Read, FileShare.None);
        using var provider = Build("Production", secret.Path);

        var refusal = Assert.Throws<EntraConfigurationException>(() => Options(provider));

        Assert.Equal("The Entra client secret file cannot be read.", refusal.Message);
        Assert.DoesNotContain(secret.Path, refusal.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_refuses_a_file_that_cannot_be_decrypted()
    {
        using var secret = new ProtectedSecretFile();
        File.WriteAllBytes(secret.Path, "not protected bytes"u8.ToArray());
        using var provider = Build("Production", secret.Path);

        var refusal = Assert.Throws<EntraConfigurationException>(() => Options(provider));

        Assert.Equal("The Entra client secret file cannot be decrypted.", refusal.Message);
    }

    [Fact]
    public void Production_refuses_a_file_protected_with_other_entropy()
    {
        using var secret = new ProtectedSecretFile();
        File.WriteAllBytes(
            secret.Path,
            System.Security.Cryptography.ProtectedData.Protect(
                System.Text.Encoding.UTF8.GetBytes(ProtectedSecretFile.TestSecret),
                System.Text.Encoding.UTF8.GetBytes("PosterPrintRequest.SharedStorage.v1"),
                System.Security.Cryptography.DataProtectionScope.LocalMachine));
        using var provider = Build("Production", secret.Path);

        var refusal = Assert.Throws<EntraConfigurationException>(() => Options(provider));

        Assert.Equal("The Entra client secret file cannot be decrypted.", refusal.Message);
        Assert.DoesNotContain(ProtectedSecretFile.TestSecret, refusal.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Production_refuses_an_empty_file_and_an_empty_secret()
    {
        using var secret = new ProtectedSecretFile();
        File.WriteAllBytes(secret.Path, []);
        using (var provider = Build("Production", secret.Path))
        {
            Assert.Equal("The Entra client secret file is empty.", Assert.Throws<EntraConfigurationException>(() => Options(provider)).Message);
        }

        File.WriteAllBytes(secret.Path, ProtectedSecretFile.Protect("   "));
        using (var provider = Build("Production", secret.Path))
        {
            Assert.Equal("The Entra client secret file is empty.", Assert.Throws<EntraConfigurationException>(() => Options(provider)).Message);
        }
    }

    [Fact]
    public void Production_refuses_the_public_client_configuration()
    {
        var validation = new EntraOpenIdConnectValidation(new TestEnvironment("Production"));
        var publicClient = new OpenIdConnectOptions
        {
            ResponseType = OpenIdConnectResponseType.Code,
            UsePkce = true,
            Prompt = "login",
            CallbackPath = "/signin-oidc"
        };

        var result = validation.Validate(EntraSettings.ChallengeScheme, publicClient);

        Assert.True(result.Failed);
        Assert.Contains("public-client", result.FailureMessage, StringComparison.Ordinal);

        publicClient.ClientSecret = ProtectedSecretFile.TestSecret;
        Assert.True(validation.Validate(EntraSettings.ChallengeScheme, publicClient).Succeeded);
    }

    [Fact]
    public void Development_refuses_a_client_secret_and_every_environment_refuses_other_flows()
    {
        var development = new EntraOpenIdConnectValidation(new TestEnvironment("Development"));
        var production = new EntraOpenIdConnectValidation(new TestEnvironment("Production"));
        OpenIdConnectOptions Valid() => new()
        {
            ResponseType = OpenIdConnectResponseType.Code,
            UsePkce = true,
            Prompt = "login",
            CallbackPath = "/signin-oidc",
            ClientSecret = null
        };

        Assert.True(development.Validate(EntraSettings.ChallengeScheme, Valid()).Succeeded);

        var withSecret = Valid();
        withSecret.ClientSecret = ProtectedSecretFile.TestSecret;
        Assert.True(development.Validate(EntraSettings.ChallengeScheme, withSecret).Failed);

        foreach (var validation in new[] { development, production })
        {
            foreach (var change in new Action<OpenIdConnectOptions>[]
            {
                options => options.ResponseType = OpenIdConnectResponseType.IdToken,
                options => options.ResponseType = OpenIdConnectResponseType.CodeIdToken,
                options => options.UsePkce = false,
                options => options.Prompt = null,
                options => options.Prompt = "none",
                options => options.CallbackPath = "/other"
            })
            {
                var options = Valid();
                options.ClientSecret = ProtectedSecretFile.TestSecret;
                change(options);
                Assert.True(validation.Validate(EntraSettings.ChallengeScheme, options).Failed);
            }
        }
    }

    [Fact]
    public void Production_does_not_start_without_the_client_secret()
    {
        using var factory = new ProductionFactory(_database.ConnectionString, secretFile: null);

        var refusal = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("The Entra client secret file is not configured.", Flatten(refusal), StringComparison.Ordinal);
    }

    [Fact]
    public void Development_does_not_start_without_the_tenant_and_client_identifiers()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:" + PersistenceServiceCollectionExtensions.ConnectionStringName, _database.ConnectionString);
        });

        var refusal = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("The Entra tenant identifier is not configured.", Flatten(refusal), StringComparison.Ordinal);
    }

    [Fact]
    public void Production_configures_no_forwarded_headers_and_other_environments_are_unchanged()
    {
        using var secret = new ProtectedSecretFile();
        using var production = new ProductionFactory(_database.ConnectionString, secret.Path);
        var productionOptions = production.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(ForwardedHeaders.None, productionOptions.ForwardedHeaders);
        Assert.NotEmpty(productionOptions.KnownProxies);

        using var development = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            TestEntra.Configure(builder);
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:" + PersistenceServiceCollectionExtensions.ConnectionStringName, _database.ConnectionString);
        });
        Assert.Equal(ForwardedHeaders.None, development.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value.ForwardedHeaders);

        using var demonstration = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Demo");
            builder.UseSetting("ConnectionStrings:" + PersistenceServiceCollectionExtensions.ConnectionStringName, _database.ConnectionString);
        });
        var demoOptions = demonstration.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, demoOptions.ForwardedHeaders);
        Assert.Empty(demoOptions.KnownProxies);
        Assert.Empty(demoOptions.KnownIPNetworks);
    }

    [Fact]
    public void Callback_path_is_relative_to_the_path_base_and_yields_the_production_redirect_uri()
    {
        using var secret = new ProtectedSecretFile();
        using var provider = Build("Production", secret.Path);

        var callback = Options(provider).CallbackPath;

        Assert.Equal("/signin-oidc", callback.Value);
        Assert.DoesNotContain("PosterPrintRequest", callback.Value, StringComparison.Ordinal);
        Assert.Equal(
            "https://posters.den.uic.edu/PosterPrintRequest/signin-oidc",
            "https://posters.den.uic.edu" + new PathString("/PosterPrintRequest").Add(callback));
    }

    internal static string Flatten(Exception exception)
    {
        var text = new System.Text.StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            text.AppendLine(current.Message);
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    text.AppendLine(Flatten(inner));
                }
            }
        }

        return text.ToString();
    }

    /// <summary>The application in the Production environment, with only the physical database redirected.</summary>
    internal sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly string? _secretFile;

        public ProductionFactory(string connectionString, string? secretFile)
        {
            _connectionString = connectionString;
            _secretFile = secretFile;
        }

        private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "poster-prod-" + Guid.NewGuid().ToString("N"));

        public LogCapture Logs { get; } = new();

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(EntraSettings.TenantIdKey, TestEntra.TenantId);
            builder.UseSetting(EntraSettings.ClientIdKey, TestEntra.ClientId);
            if (_secretFile is not null)
            {
                builder.UseSetting(EntraSettings.ClientSecretFileKey, _secretFile);
            }

            builder.UseSetting(
                "ConnectionStrings:" + PersistenceServiceCollectionExtensions.ConnectionStringName,
                "Server=placeholder;Database=POSTER;Integrated Security=True;Encrypt=True");
            Directory.CreateDirectory(_storageRoot);
            builder.UseSetting("SharedStorage:RootPath", _storageRoot);
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
            builder.ConfigureTestServices(services =>
            {
                // Only the physical database is redirected; the production registrations stay.
                services.RemoveAll<IDbContextOptionsConfiguration<PosterPrintRequestDbContext>>();
                services.RemoveAll<DbContextOptions<PosterPrintRequestDbContext>>();
                services.RemoveAll<PosterPrintRequestDbContext>();
                services.AddDbContext<PosterPrintRequestDbContext>(options => options.UseSqlServer(_connectionString));
            });
        }
    }
}
