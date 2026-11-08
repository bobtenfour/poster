using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Tests.Workflow;
using PosterPrintRequest.Web;
using static PosterPrintRequest.Tests.Hosting.EntraConfigurationTests;

namespace PosterPrintRequest.Tests.Hosting;

/// <summary>
/// Authentication Contract section 12: the Production-shaped build, the client secret in logs and responses,
/// Data Protection, and what must not be committed.
/// </summary>
[Collection("Workflow database")]
public sealed class EntraProductionTests
{
    private readonly WorkflowDatabaseFixture _database;

    public EntraProductionTests(WorkflowDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task The_production_build_with_scope_and_build_validation_resolves_every_registered_service()
    {
        using var secret = new ProtectedSecretFile();
        using var root = new ProductionFactory(_database.ConnectionString, secret.Path);
        IServiceCollection? registered = null;
        using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            });
            builder.ConfigureTestServices(services => registered = services);
        });

        var provider = factory.Services;

        Assert.NotNull(registered);
        using var scope = provider.CreateScope();
        var failures = new List<string>();
        var resolved = 0;
        foreach (var descriptor in registered!.Where(item => !item.ServiceType.IsGenericTypeDefinition && item.ServiceKey is null))
        {
            try
            {
                _ = scope.ServiceProvider.GetService(descriptor.ServiceType);
                resolved++;
            }
            catch (Exception exception)
            {
                failures.Add(descriptor.ServiceType.FullName + ": " + exception.GetType().Name);
            }
        }

        Assert.Empty(failures);
        Assert.True(resolved > 100);
        var schemes = (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync())
            .Select(scheme => scheme.Name)
            .Order()
            .ToArray();
        Assert.Equal(["Cookies", "OpenIdConnect"], schemes);
        Assert.DoesNotContain(NegotiateDefaults.AuthenticationScheme, schemes);
        Assert.Null(provider.GetService<IInstitutionalGroupVerifier>());
        Assert.DoesNotContain(
            provider.GetServices<IAuthorizationHandler>(),
            handler => handler is InstitutionalGroupHandler or InstitutionalParticipantHandler);
        Assert.IsType<ClaimsActorSource>(provider.GetRequiredService<IInstitutionalActorSource>());
        Assert.IsType<SessionAuthenticationStateProvider>(scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>());
    }

    [Fact]
    public async Task The_client_secret_and_its_file_appear_in_no_log_entry_exception_or_response()
    {
        using var secret = new ProtectedSecretFile();
        using var root = new ProductionFactory(_database.ConnectionString, secret.Path);
        using var factory = root.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(TestOpenIdConnect.UseStaticConfiguration));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var responses = new List<HttpResponseMessage>
        {
            await client.GetAsync("/health"),
            await client.GetAsync("/"),
            await client.GetAsync("/request"),
            await client.GetAsync("/signin-oidc?error=access_denied&error_description=refused"),
            await client.PostAsync("/Account/SignOut", new FormUrlEncodedContent([])),
            await client.GetAsync("/Error")
        };

        Assert.Equal(HttpStatusCode.Redirect, responses[1].StatusCode);
        foreach (var response in responses)
        {
            var text = response + await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(ProtectedSecretFile.TestSecret, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Path.GetFileName(secret.Path), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("client_secret", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.NotEmpty(root.Logs.Entries);
        foreach (var entry in root.Logs.Entries)
        {
            Assert.DoesNotContain(ProtectedSecretFile.TestSecret, entry, StringComparison.Ordinal);
            Assert.DoesNotContain(secret.Path, entry, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Data_protection_uses_only_the_application_name_and_no_key_repository_or_protection()
    {
        using var secret = new ProtectedSecretFile();
        using var factory = new ProductionFactory(_database.ConnectionString, secret.Path);

        var discriminator = factory.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;
        var keys = factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        Assert.Equal("PosterPrintRequest", discriminator);
        Assert.Null(keys.XmlRepository);
        Assert.Null(keys.XmlEncryptor);
    }

    [Fact]
    public void The_source_configures_data_protection_with_the_application_name_and_nothing_else()
    {
        var source = Path.Combine(RepositoryPaths.Root(), "src");
        var code = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Equal(1, code.Sum(text => Regex.Matches(text, @"AddDataProtection\(\)\.SetApplicationName\(""PosterPrintRequest""\);").Count));
        Assert.Equal(1, code.Sum(text => Regex.Matches(text, @"AddDataProtection\(").Count));
        foreach (var forbidden in new[]
        {
            "PersistKeysTo", "ProtectKeysWith", "SetDefaultKeyLifetime", "UseCryptographicAlgorithms",
            "AddKeyManagementOptions", "DisableAutomaticKeyGeneration", "DataProtection-Keys", "LOCALAPPDATA"
        })
        {
            Assert.DoesNotContain(code, text => text.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Tenant_client_and_secret_settings_are_in_no_committed_configuration()
    {
        var web = RepositoryPaths.WebProjectDirectory();
        var files = Directory.EnumerateFiles(web, "appsettings*.json")
            .Append(Path.Combine(web, "Properties", "launchSettings.json"))
            .Append(Path.Combine(web, "web.config"))
            .ToArray();

        Assert.True(files.Length >= 5);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("\"Authentication\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Authentication__", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TenantId", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ClientId", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ClientSecret", text, StringComparison.OrdinalIgnoreCase);
        }

        var project = File.ReadAllText(Path.Combine(web, "PosterPrintRequest.Web.csproj"));
        Assert.DoesNotContain("UserSecretsId", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_web_project_references_the_openid_connect_package_aligned_with_the_existing_packages()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryPaths.WebProjectDirectory(), "PosterPrintRequest.Web.csproj"));

        Assert.Matches(@"Microsoft\.AspNetCore\.Authentication\.OpenIdConnect"" Version=""10\.0\.\d+""", project);
        Assert.Matches(@"Microsoft\.EntityFrameworkCore\.Design"" Version=""10\.0\.13""", project);
    }

    [Fact]
    public void The_provisioning_document_exists_and_holds_no_real_values()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryPaths.Root(), "docs", "Entra_Credential_Provisioning.md"));

        Assert.Contains("PosterPrintRequest.Authentication.v1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PosterPrintRequest.SharedStorage.v1", text, StringComparison.Ordinal);
        Assert.Contains("LocalMachine", text, StringComparison.Ordinal);
        Assert.Contains("Authentication__ClientSecretFile", text, StringComparison.Ordinal);
        Assert.Contains("Authentication__TenantId", text, StringComparison.Ordinal);
        Assert.Contains("Authentication__ClientId", text, StringComparison.Ordinal);
        Assert.Contains(@"IIS AppPool\<application-pool-name>", text, StringComparison.Ordinal);
        Assert.Contains("12 months", text, StringComparison.Ordinal);
        Assert.Contains("Secret Value", text, StringComparison.Ordinal);
        Assert.Contains("AsSecureString", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("Set-EntraAcl $target", StringComparison.Ordinal) < text.IndexOf("WriteAllBytes", StringComparison.Ordinal),
            "The access control comes before any secret is written.");
        Assert.DoesNotMatch(@"[A-Za-z]:\\", text);
        Assert.DoesNotContain(@"\\", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", text);
        Assert.DoesNotContain("posters.den.uic.edu", text, StringComparison.Ordinal);
    }
}
