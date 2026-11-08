using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PosterPrintRequest.Web;

// The client-secret tests use DPAPI LocalMachine, as Production on IIS does.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace PosterPrintRequest.Tests.Hosting;

/// <summary>
/// A client-secret file protected exactly as the Authentication Contract section 6.2 describes: DPAPI,
/// LocalMachine scope, the contract entropy, UTF-8 secret bytes only. The value is a throwaway test value.
/// </summary>
internal sealed class ProtectedSecretFile : IDisposable
{
    internal const string TestSecret = "test-only-secret-value~Zq7!x";

    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "poster-entra-" + Guid.NewGuid().ToString("N"));

    public ProtectedSecretFile(string secret = TestSecret)
    {
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, "client-secret.bin");
        File.WriteAllBytes(Path, Protect(secret));
    }

    public string Path { get; }

    public static byte[] Protect(string secret) =>
        ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(EntraClientSecret.Entropy),
            DataProtectionScope.LocalMachine);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

internal sealed class TestEnvironment : IHostEnvironment
{
    public TestEnvironment(string name)
    {
        EnvironmentName = name;
    }

    public string EnvironmentName { get; set; }

    public string ApplicationName { get; set; } = "PosterPrintRequest.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan span) => _now += span;
}

internal sealed class LogCapture : ILoggerProvider
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CaptureLogger : ILogger
    {
        private readonly LogCapture _owner;
        private readonly string _category;

        public CaptureLogger(LogCapture owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_owner._entries)
            {
                _owner._entries.Add(_category + ": " + formatter(state, exception) + (exception is null ? "" : " " + exception));
            }
        }
    }
}

internal static class TestOpenIdConnect
{
    internal const string AuthorizationEndpoint = "https://identity.test.invalid/authorize";

    /// <summary>
    /// Supplies the provider configuration that metadata retrieval would give, so that building the
    /// authorization request needs no network. Nothing here answers for Entra.
    /// </summary>
    internal static void UseStaticConfiguration(IServiceCollection services)
    {
        services.PostConfigure<OpenIdConnectOptions>(EntraSettings.ChallengeScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = "https://identity.test.invalid/issuer",
                AuthorizationEndpoint = AuthorizationEndpoint,
                TokenEndpoint = "https://identity.test.invalid/token"
            };
            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });
    }
}
