using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace PosterPrintRequest.Web;

/// <summary>
/// The Entra authentication configuration is invalid; the application must not start.
/// The message names the condition and never contains a secret, a path, or an account name.
/// </summary>
public sealed class EntraConfigurationException : InvalidOperationException
{
    public EntraConfigurationException(string condition)
        : base(condition)
    {
    }
}

/// <summary>
/// Reads the DPAPI (LocalMachine) protected Entra client secret once, at startup
/// (Authentication Contract section 6).
/// </summary>
public static class EntraClientSecret
{
    public const string Entropy = "PosterPrintRequest.Authentication.v1";

    public static string Read(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new EntraConfigurationException("The Entra client secret file is not configured.");
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new EntraConfigurationException("The Entra client secret file cannot be decrypted.");
        }

        return ReadOnWindows(file);
    }

    [SupportedOSPlatform("windows")]
    private static string ReadOnWindows(string file)
    {
        byte[] protectedBytes;
        try
        {
            if (!File.Exists(file))
            {
                throw new EntraConfigurationException("The Entra client secret file is missing.");
            }

            protectedBytes = File.ReadAllBytes(file);
        }
        catch (EntraConfigurationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new EntraConfigurationException("The Entra client secret file cannot be read.");
        }

        if (protectedBytes.Length == 0)
        {
            throw new EntraConfigurationException("The Entra client secret file is empty.");
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
                throw new EntraConfigurationException("The Entra client secret file cannot be decrypted.");
            }

            var secret = Encoding.UTF8.GetString(plain);
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new EntraConfigurationException("The Entra client secret file is empty.");
            }

            return secret;
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }
}
