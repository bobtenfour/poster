using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PosterPrintRequest.Infrastructure.Storage;

/// <summary>
/// Reads the DPAPI-protected credential file once and produces the network-credential token.
/// The plaintext password lives only in a buffer that is cleared right after LogonUser.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class QumuloCredential
{
    internal const string Entropy = "PosterPrintRequest.SharedStorage.v1";

    private const int Logon32LogonNewCredentials = 9;
    private const int Logon32ProviderWinnt50 = 3;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LogonUserW")]
    private static extern bool LogonUser(
        string userName,
        string domain,
        IntPtr password,
        int logonType,
        int logonProvider,
        out SafeAccessTokenHandle token);

    internal static SafeAccessTokenHandle CreateToken(string credentialFile)
    {
        byte[] protectedBytes;
        try
        {
            if (!File.Exists(credentialFile))
            {
                throw new StorageConfigurationException("The shared-storage credential file is missing.");
            }

            protectedBytes = File.ReadAllBytes(credentialFile);
        }
        catch (StorageConfigurationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new StorageConfigurationException("The shared-storage credential file cannot be read.");
        }

        byte[]? plain = null;
        char[]? password = null;
        var passwordLength = 0;
        var passwordBuffer = IntPtr.Zero;
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
                throw new StorageConfigurationException("The shared-storage credential file cannot be decrypted.");
            }

            string? domain = null;
            string? userName = null;
            try
            {
                var reader = new Utf8JsonReader(plain);
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException();
                }

                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    var name = reader.GetString();
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.String)
                    {
                        reader.Skip();
                        continue;
                    }

                    if (string.Equals(name, "Domain", StringComparison.OrdinalIgnoreCase))
                    {
                        domain = reader.GetString();
                    }
                    else if (string.Equals(name, "UserName", StringComparison.OrdinalIgnoreCase))
                    {
                        userName = reader.GetString();
                    }
                    else if (string.Equals(name, "Password", StringComparison.OrdinalIgnoreCase))
                    {
                        var capacity = reader.HasValueSequence
                            ? checked((int)reader.ValueSequence.Length)
                            : reader.ValueSpan.Length;
                        password = new char[capacity];
                        passwordLength = reader.CopyString(password);
                    }
                }
            }
            catch (JsonException)
            {
                throw Incomplete();
            }

            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(userName) || passwordLength == 0 || password is null)
            {
                throw Incomplete();
            }

            passwordBuffer = Marshal.AllocHGlobal((passwordLength + 1) * sizeof(char));
            Marshal.Copy(password, 0, passwordBuffer, passwordLength);
            Marshal.WriteInt16(passwordBuffer, passwordLength * sizeof(char), 0);

            if (!LogonUser(userName, domain, passwordBuffer, Logon32LogonNewCredentials, Logon32ProviderWinnt50, out var token)
                || token.IsInvalid)
            {
                var code = Marshal.GetLastWin32Error();
                throw new StorageConfigurationException(
                    "The shared-storage network credential token could not be created (Win32 error " + code + ").");
            }

            return token;
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }

            if (password is not null)
            {
                Array.Clear(password);
            }

            if (passwordBuffer != IntPtr.Zero)
            {
                for (var offset = 0; offset < (passwordLength + 1) * sizeof(char); offset += sizeof(char))
                {
                    Marshal.WriteInt16(passwordBuffer, offset, 0);
                }

                Marshal.FreeHGlobal(passwordBuffer);
            }
        }
    }

    private static StorageConfigurationException Incomplete() =>
        new("The shared-storage credential file does not contain a domain, user name, and password.");
}
