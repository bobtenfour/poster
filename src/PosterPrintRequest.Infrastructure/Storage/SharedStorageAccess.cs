using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using System.Security.Principal;

namespace PosterPrintRequest.Infrastructure.Storage;

/// <summary>
/// The single implementation of <see cref="ISharedStorageAccess"/>. The access mode is chosen
/// once at construction from configuration: process identity when no credential file is
/// configured, otherwise the dedicated Qumulo account on Windows. There is no fallback identity.
/// </summary>
public sealed class SharedStorageAccess : ISharedStorageAccess, IDisposable
{
    private readonly ILogger _logger;
    private readonly string _root;
    private readonly string _rootPrefix;
    private readonly SafeAccessTokenHandle? _token;
    private readonly string _mode;

    public SharedStorageAccess(SharedStorageOptions options, ILogger<SharedStorageAccess> logger, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new StorageConfigurationException("The shared-storage root path is not configured.");
        }

        var credentialConfigured = !string.IsNullOrWhiteSpace(options.CredentialFile);
        if (credentialConfigured && !OperatingSystem.IsWindows())
        {
            throw new StorageConfigurationException(
                "A shared-storage credential file is configured on a host that is not Windows.");
        }

        if (!credentialConfigured && isProduction && IsUncPath(options.RootPath))
        {
            throw new StorageConfigurationException(
                "The production shared-storage root is a network path and no credential file is configured.");
        }

        _root = Path.GetFullPath(options.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _rootPrefix = _root + Path.DirectorySeparatorChar;

        if (credentialConfigured && OperatingSystem.IsWindows())
        {
            _mode = "QumuloAccount";
            _token = QumuloCredential.CreateToken(options.CredentialFile!);
        }
        else
        {
            _mode = "ProcessIdentity";
        }

        var reachable = DirectoryExists(string.Empty);
        _logger.LogInformation(
            "Storage startup check: root {Result}. Access mode {Mode}.",
            reachable ? "reachable" : "not reachable",
            _mode);
    }

    public void Dispose() => _token?.Dispose();

    public bool DirectoryExists(string relativePath) =>
        Probe(relativePath, allowRoot: true, "DirectoryExists", Directory.Exists);

    public bool FileExists(string relativePath) =>
        Probe(relativePath, allowRoot: false, "FileExists", File.Exists);

    public void CreateDirectory(string relativePath) =>
        Run(relativePath, allowRoot: false, "CreateDirectory", full => { Directory.CreateDirectory(full); return 0; });

    public IReadOnlyList<StorageEntry> ListEntries(string relativePath) =>
        Run(relativePath, allowRoot: true, "ListEntries", full =>
        {
            var entries = new List<StorageEntry>();
            foreach (var child in Directory.EnumerateFileSystemEntries(full))
            {
                entries.Add(new StorageEntry(Path.GetFileName(child), Directory.Exists(child)));
            }

            return (IReadOnlyList<StorageEntry>)entries;
        });

    public Stream OpenRead(string relativePath) =>
        Run(relativePath, allowRoot: false, "OpenRead", full => (Stream)new FileStream(
            full,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan));

    public Stream OpenWrite(string relativePath) =>
        Run(relativePath, allowRoot: false, "OpenWrite", full => (Stream)new FileStream(
            full,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true));

    public void CopyFile(string sourceRelative, string destinationRelative)
    {
        var destination = Resolve(destinationRelative, allowRoot: false);
        Run(sourceRelative, allowRoot: false, "CopyFile", source =>
        {
            File.Copy(source, destination ?? throw new StorageFailureException("CopyFile"), overwrite: false);
            return 0;
        });
    }

    public void MoveDirectory(string sourceRelative, string destinationRelative)
    {
        var destination = Resolve(destinationRelative, allowRoot: false);
        Run(sourceRelative, allowRoot: false, "MoveDirectory", source =>
        {
            if (destination is null
                || string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(source)
                || IsReparse(source)
                || Directory.Exists(destination)
                || File.Exists(destination)
                || (Exists(destination) && IsReparse(destination)))
            {
                throw new StorageFailureException("MoveDirectory");
            }

            Directory.Move(source, destination);
            return 0;
        });
    }

    public void DeleteFile(string relativePath) =>
        Run(relativePath, allowRoot: false, "DeleteFile", full => { File.Delete(full); return 0; });

    public void DeleteDirectory(string relativePath) =>
        Run(relativePath, allowRoot: false, "DeleteDirectory", full => { Directory.Delete(full, recursive: true); return 0; });

    public long FileLength(string relativePath) =>
        Run(relativePath, allowRoot: false, "FileLength", full => new FileInfo(full).Length);

    public bool IsReparsePoint(string relativePath)
    {
        var full = Resolve(relativePath, allowRoot: true);
        if (full is null)
        {
            return true;
        }

        try
        {
            return Execute("IsReparsePoint", () => IsReparse(full));
        }
        catch (StorageFailureException)
        {
            return true;
        }
    }

    private static bool IsReparse(string full) =>
        (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0;

    private static bool Exists(string full) => File.Exists(full) || Directory.Exists(full);

    private static bool IsUncPath(string path)
    {
        var trimmed = path.TrimStart();
        return trimmed.StartsWith(@"\\", StringComparison.Ordinal)
            || (OperatingSystem.IsWindows() && trimmed.StartsWith("//", StringComparison.Ordinal));
    }

    /// <summary>
    /// Resolves a relative path against the root. Returns null when the path is absolute, has a
    /// colon prefix, contains a "." or ".." segment or ".." inside a segment, or leaves the root.
    /// </summary>
    private string? Resolve(string? relativePath, bool allowRoot)
    {
        if (relativePath is null)
        {
            return null;
        }

        var normalized = relativePath.Replace('\\', '/').Trim();
        if (normalized.Length == 0)
        {
            return allowRoot ? _root : null;
        }

        if (normalized.StartsWith('/') || normalized.Contains(':'))
        {
            return null;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment is "." or ".." || segment.Contains("..", StringComparison.Ordinal)))
        {
            return null;
        }

        var combined = _root;
        foreach (var segment in segments)
        {
            combined = Path.Combine(combined, segment);
        }

        string full;
        try
        {
            full = Path.GetFullPath(combined);
        }
        catch (Exception)
        {
            return null;
        }

        return full.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private bool Probe(string relativePath, bool allowRoot, string operation, Func<string, bool> check)
    {
        var full = Resolve(relativePath, allowRoot);
        if (full is null)
        {
            return false;
        }

        try
        {
            return Execute(operation, () => check(full));
        }
        catch (StorageFailureException)
        {
            return false;
        }
    }

    private T Run<T>(string relativePath, bool allowRoot, string operation, Func<string, T> action)
    {
        var full = Resolve(relativePath, allowRoot);
        if (full is null)
        {
            throw new StorageFailureException(operation);
        }

        return Execute(operation, () => action(full));
    }

    private T Execute<T>(string operation, Func<T> action)
    {
        try
        {
            if (_token is not null && OperatingSystem.IsWindows())
            {
                return RunImpersonated(_token, action);
            }

            return action();
        }
        catch (StorageFailureException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException
                or IOException
                or System.ComponentModel.Win32Exception
                or System.Security.SecurityException
                or ArgumentException
                or NotSupportedException)
        {
            _logger.LogWarning(
                "Storage operation {Operation} failed. Code 0x{Code:X8}. Access mode {Mode}.",
                operation,
                exception.HResult,
                _mode);
            throw new StorageFailureException(operation);
        }
    }

    [SupportedOSPlatform("windows")]
    private static T RunImpersonated<T>(SafeAccessTokenHandle token, Func<T> action) =>
        WindowsIdentity.RunImpersonated(token, action);
}
