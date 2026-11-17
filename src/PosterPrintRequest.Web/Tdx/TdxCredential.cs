using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PosterPrintRequest.Web.Tdx;

/// <summary>The TDX credential cannot be used. The message names the condition and never contains a secret or a path.</summary>
public sealed class TdxCredentialException : InvalidOperationException
{
    public TdxCredentialException(string condition)
        : base(condition)
    {
    }
}

/// <summary>The TDX service-account credential. Kept in memory only; never logged.</summary>
public sealed class TdxCredentialValue
{
    public TdxCredentialValue(string userName, string password)
    {
        UserName = userName;
        Password = password;
    }

    public string UserName { get; }

    public string Password { get; }

    public override string ToString() => "TdxCredential";
}

/// <summary>
/// Reads the DPAPI (LocalMachine) protected TDX credential: JSON with UserName and Password
/// (TDX Integration Contract section 9.2). The only file access of PHASE 15 outside the storage boundary
/// (section 9.5, Storage Access Contract section 14 amendment (e)).
/// </summary>
public static class TdxCredential
{
    public const string Entropy = "PosterPrintRequest.Tdx.v1";

    public static TdxCredentialValue Read(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new TdxCredentialException("The TDX credential file is not configured.");
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new TdxCredentialException("The TDX credential file cannot be decrypted on this platform.");
        }

        return ReadOnWindows(file);
    }

    [SupportedOSPlatform("windows")]
    private static TdxCredentialValue ReadOnWindows(string file)
    {
        byte[] protectedBytes;
        try
        {
            if (!File.Exists(file))
            {
                throw new TdxCredentialException("The TDX credential file is missing.");
            }

            protectedBytes = File.ReadAllBytes(file);
        }
        catch (TdxCredentialException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new TdxCredentialException("The TDX credential file cannot be read.");
        }

        if (protectedBytes.Length == 0)
        {
            throw new TdxCredentialException("The TDX credential file is empty.");
        }

        byte[]? plain = null;
        try
        {
            try
            {
                plain = ProtectedData.Unprotect(
                    protectedBytes,
                    Encoding.UTF8.GetBytes(Entropy),
                    DataProtectionScope.LocalMachine);
            }
            catch (Exception)
            {
                throw new TdxCredentialException("The TDX credential file cannot be decrypted.");
            }

            return Parse(plain);
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }

    /// <summary>Parses the decrypted JSON. Public for tests; the error never contains the content.</summary>
    public static TdxCredentialValue Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("UserName", out var user) && user.ValueKind == JsonValueKind.String
                && root.TryGetProperty("Password", out var password) && password.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(user.GetString())
                && !string.IsNullOrEmpty(password.GetString()))
            {
                return new TdxCredentialValue(user.GetString()!.Trim(), password.GetString()!);
            }
        }
        catch (JsonException)
        {
        }

        throw new TdxCredentialException("The TDX credential file does not contain UserName and Password.");
    }
}
