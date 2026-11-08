using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Storage;

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        if (exception is not null)
        {
            Messages.Add(exception.ToString());
        }
    }
}

/// <summary>A fact that the runner reports as skipped on Windows (needs a non-Windows host).</summary>
internal sealed class FactSkippedOnWindowsAttribute : FactAttribute
{
    public FactSkippedOnWindowsAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Requires a non-Windows host.";
        }
    }
}

public sealed class StorageAccessContractTests : IDisposable
{
    private const string UncRoot = @"\\placeholder-host\placeholder-share";

    private readonly string _parent = Path.Combine(Path.GetTempPath(), "poster-access-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public StorageAccessContractTests()
    {
        _root = Path.Combine(_parent, "root");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_parent))
        {
            Directory.Delete(_parent, recursive: true);
        }
    }

    private static readonly string EntraSecretReader =
        Path.Combine("PosterPrintRequest.Web", "Identity", "EntraClientSecret.cs");

    [Fact]
    public void Only_the_storage_boundary_touches_the_file_system_under_the_root()
    {
        // The Storage Access Contract (section 3) limits the boundary to paths under the shared-storage root.
        // The one other file the application reads is the DPAPI-protected Entra client secret, which lives outside
        // that root (Authentication Contract section 6); its reader is the only exemption, named by exact path.
        var pattern = new Regex(
            @"(?<![\w.])(System\.IO\.)?(File|Directory|FileStream|FileInfo|DirectoryInfo)\.|(?<![\w.])System\.IO\.(File|Directory|FileStream|FileInfo|DirectoryInfo)\b|new\s+(System\.IO\.)?(FileStream|FileInfo|DirectoryInfo)\b|Path\.GetFullPath",
            RegexOptions.Compiled);
        var source = Path.Combine(RepositoryPaths.Root(), "src");
        var boundary = Path.Combine("PosterPrintRequest.Infrastructure", "Storage") + Path.DirectorySeparatorChar;
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".razor", StringComparison.Ordinal)))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Contains(boundary, StringComparison.Ordinal)
                || relative == EntraSecretReader
                || relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (pattern.IsMatch(line))
                {
                    violations.Add(relative + ":" + lineNumber);
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void The_provisioning_document_exists_and_holds_no_real_values()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryPaths.Root(), "docs", "Storage_Credential_Provisioning.md"));

        Assert.Contains("PosterPrintRequest.SharedStorage.v1", text, StringComparison.Ordinal);
        Assert.Contains("LocalMachine", text, StringComparison.Ordinal);
        Assert.Contains("SharedStorage__CredentialFile", text, StringComparison.Ordinal);
        Assert.Contains(@"IIS AppPool\<application-pool-name>", text, StringComparison.Ordinal);
        Assert.Contains("12 months", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[A-Za-z]:\\", text);
        Assert.DoesNotContain(@"\\", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Containment_rejects_paths_outside_the_root_and_never_reads_or_deletes_them()
    {
        var secret = Path.Combine(_parent, "secret.pdf");
        File.WriteAllText(secret, "secret");
        var access = TestStorage.Access(_root);

        foreach (var rejected in new[] { "../secret.pdf", "/secret.pdf", @"C:\secret.pdf", "a/./b", "a..b/c", "a/../../secret.pdf" })
        {
            Assert.False(access.FileExists(rejected));
            Assert.False(access.DirectoryExists(rejected));
            Assert.Throws<StorageFailureException>(() => access.OpenRead(rejected));
            Assert.Throws<StorageFailureException>(() => access.DeleteFile(rejected));
            Assert.Throws<StorageFailureException>(() => access.DeleteDirectory(rejected));
            Assert.Throws<StorageFailureException>(() => access.CopyFile(rejected, "copy.pdf"));
            Assert.Throws<StorageFailureException>(() => access.CopyFile("copy.pdf", rejected));
        }

        Assert.Equal("secret", File.ReadAllText(secret));
        Assert.False(File.Exists(Path.Combine(_root, "copy.pdf")));
    }

    [Fact]
    public void Reparse_points_are_refused_as_move_sources()
    {
        var access = TestStorage.Access(_root);
        access.CreateDirectory("real/child");
        var link = Path.Combine(_root, "link");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(_root, "real"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        Assert.True(access.IsReparsePoint("link"));
        Assert.Throws<StorageFailureException>(() => access.MoveDirectory("link", "moved"));
        Assert.True(Directory.Exists(Path.Combine(_root, "real", "child")));
    }

    [Fact]
    public void A_denied_operation_becomes_a_storage_failure_without_the_root_in_message_or_log()
    {
        var logger = new CapturingLogger<SharedStorageAccess>();
        var access = new SharedStorageAccess(new SharedStorageOptions { RootPath = _root }, logger, isProduction: false);
        Directory.CreateDirectory(Path.Combine(_root, "Poster.pdf"));

        var failure = Assert.Throws<StorageFailureException>(() => access.OpenRead("Poster.pdf"));

        Assert.Null(failure.InnerException);
        Assert.DoesNotContain(_root, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_parent, string.Join('\n', logger.Messages), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Messages, message => message.Contains("OpenRead", StringComparison.Ordinal) && message.Contains("ProcessIdentity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_storage_failure_keeps_the_existing_user_messages()
    {
        var failing = new FailingAccess(TestStorage.Access(_root));
        var drafts = new DraftFileStore(failing);
        var draftId = DraftIds.Create();
        failing.FailOn = "OpenWrite";

        await using var content = new MemoryStream("%PDF-1.4"u8.ToArray());
        var saved = await drafts.SaveAsync(draftId, DraftFileRole.Poster, "Poster.pdf", content, content.Length, null, CancellationToken.None);
        Assert.False(saved.Saved);
        Assert.Equal("The file could not be stored. Choose it again.", saved.Error);

        failing.FailOn = null;
        await using (var again = new MemoryStream("%PDF-1.4"u8.ToArray()))
        {
            Assert.True((await drafts.SaveAsync(draftId, DraftFileRole.Poster, "Poster.pdf", again, again.Length, null, CancellationToken.None)).Saved);
        }

        failing.FailOn = "CopyFile";
        var storage = new AcceptedStorage(failing, drafts);
        var placed = storage.TryPlace(draftId, "POSTER-2026-000900", null, new DateTime(2026, 11, 7), "Ada Lovelace", false, "PDF", out var placement, out var error);
        Assert.False(placed);
        Assert.Null(placement);
        Assert.Equal("The files could not be stored. Choose them again.", error);
        Assert.DoesNotContain(_root, error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(_root, "WITHOUT-EVENT TO BE PRINTED", "2026", "Ada Lovelace - POSTER-2026-000900")));
    }

    [Fact]
    public void Production_refuses_a_network_root_without_a_credential_file()
    {
        var failure = Assert.Throws<StorageConfigurationException>(() =>
            new SharedStorageAccess(new SharedStorageOptions { RootPath = UncRoot }, new CapturingLogger<SharedStorageAccess>(), isProduction: true));

        Assert.DoesNotContain("placeholder", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_network_root_outside_production_and_a_local_root_in_production_use_the_process_identity()
    {
        using var development = new SharedStorageAccess(new SharedStorageOptions { RootPath = UncRoot }, new CapturingLogger<SharedStorageAccess>(), isProduction: false);
        using var production = new SharedStorageAccess(new SharedStorageOptions { RootPath = _root, CredentialFile = " " }, new CapturingLogger<SharedStorageAccess>(), isProduction: true);

        Assert.True(production.DirectoryExists(""));
    }

    [FactSkippedOnWindows]
    public void A_credential_file_on_a_host_that_is_not_windows_stops_startup()
    {
        var failure = Assert.Throws<StorageConfigurationException>(() =>
            new SharedStorageAccess(
                new SharedStorageOptions { RootPath = _root, CredentialFile = Path.Combine(_parent, "credential.bin") },
                new CapturingLogger<SharedStorageAccess>(),
                isProduction: false));

        Assert.DoesNotContain(_parent, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_credential_file_stops_startup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AssertRefused(Path.Combine(_parent, "missing.bin"));
    }

    [Fact]
    public void An_unreadable_credential_file_stops_startup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var file = Path.Combine(_parent, "locked.bin");
        File.WriteAllBytes(file, [1, 2, 3]);
        using var hold = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        AssertRefused(file);
    }

    [Fact]
    public void A_credential_file_that_cannot_be_decrypted_stops_startup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var file = Path.Combine(_parent, "garbage.bin");
        File.WriteAllBytes(file, RandomNumberGenerator.GetBytes(64));

        AssertRefused(file);
    }

    [Fact]
    public void A_credential_without_domain_user_name_and_password_stops_startup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var json in new[]
                 {
                     "{\"Domain\":\"d\",\"UserName\":\"u\"}",
                     "{\"Domain\":\"\",\"UserName\":\"u\",\"Password\":\"p\"}",
                     "{\"Domain\":\"d\",\"UserName\":\"u\",\"Password\":\"\"}",
                     "[1,2]"
                 })
        {
            AssertRefused(WriteCredential(json));
        }
    }

    [Fact]
    public void Qumulo_account_mode_runs_every_operation_through_the_impersonated_token()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // LOGON32_LOGON_NEW_CREDENTIALS does not validate the password, so a placeholder credential
        // produces a token. This exercises DPAPI, token creation, and impersonation locally;
        // it does not establish SMB connectivity.
        var file = WriteCredential("{\"Domain\":\"placeholder\",\"UserName\":\"placeholder\",\"Password\":\"placeholder\"}");
        var logger = new CapturingLogger<SharedStorageAccess>();
        using var access = new SharedStorageAccess(
            new SharedStorageOptions { RootPath = _root, CredentialFile = file },
            logger,
            isProduction: true);

        access.CreateDirectory("a/b");
        using (var write = access.OpenWrite("a/b/file.bin"))
        {
            write.WriteByte(7);
        }

        Assert.Equal(1, access.FileLength("a/b/file.bin"));
        using (var read = access.OpenRead("a/b/file.bin"))
        {
            Assert.Equal(7, read.ReadByte());
        }

        Assert.Contains(logger.Messages, message => message.Contains("QumuloAccount", StringComparison.Ordinal));
        var logged = string.Join('\n', logger.Messages);
        Assert.DoesNotContain("placeholder", logged, StringComparison.Ordinal);
        Assert.DoesNotContain(_parent, logged, StringComparison.OrdinalIgnoreCase);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private string WriteCredential(string json)
    {
        var file = Path.Combine(_parent, "credential-" + Guid.NewGuid().ToString("N") + ".bin");
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json),
            Encoding.UTF8.GetBytes("PosterPrintRequest.SharedStorage.v1"),
            DataProtectionScope.LocalMachine);
        File.WriteAllBytes(file, protectedBytes);
        return file;
    }

    private void AssertRefused(string credentialFile)
    {
        var failure = Assert.Throws<StorageConfigurationException>(() =>
            new SharedStorageAccess(
                new SharedStorageOptions { RootPath = _root, CredentialFile = credentialFile },
                new CapturingLogger<SharedStorageAccess>(),
                isProduction: true));

        Assert.DoesNotContain(_parent, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("placeholder", failure.Message, StringComparison.Ordinal);
    }

    private sealed class FailingAccess : ISharedStorageAccess
    {
        private readonly ISharedStorageAccess _inner;

        public FailingAccess(ISharedStorageAccess inner) => _inner = inner;

        public string? FailOn { get; set; }

        private void Check(string operation)
        {
            if (FailOn == operation)
            {
                throw new StorageFailureException(operation);
            }
        }

        public bool DirectoryExists(string relativePath) => _inner.DirectoryExists(relativePath);

        public bool FileExists(string relativePath) => _inner.FileExists(relativePath);

        public void CreateDirectory(string relativePath) { Check("CreateDirectory"); _inner.CreateDirectory(relativePath); }

        public IReadOnlyList<StorageEntry> ListEntries(string relativePath) { Check("ListEntries"); return _inner.ListEntries(relativePath); }

        public Stream OpenRead(string relativePath) { Check("OpenRead"); return _inner.OpenRead(relativePath); }

        public Stream OpenWrite(string relativePath) { Check("OpenWrite"); return _inner.OpenWrite(relativePath); }

        public void CopyFile(string sourceRelative, string destinationRelative) { Check("CopyFile"); _inner.CopyFile(sourceRelative, destinationRelative); }

        public void MoveDirectory(string sourceRelative, string destinationRelative) { Check("MoveDirectory"); _inner.MoveDirectory(sourceRelative, destinationRelative); }

        public void DeleteFile(string relativePath) { Check("DeleteFile"); _inner.DeleteFile(relativePath); }

        public void DeleteDirectory(string relativePath) { Check("DeleteDirectory"); _inner.DeleteDirectory(relativePath); }

        public long FileLength(string relativePath) => _inner.FileLength(relativePath);

        public bool IsReparsePoint(string relativePath) => _inner.IsReparsePoint(relativePath);
    }
}
