using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace PosterPrintRequest.TdxValidator;

/// <summary>
/// Contract §5. Asks for the sandbox username and password in the standard Windows credential window.
/// The password field is masked by Windows. Nothing is saved: the "remember" option is disabled and
/// the values live in memory for this execution only.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsCredentialPrompt
{
    private const int MaxUserName = 513;
    private const int MaxPassword = 256;
    private const int ErrorCancelled = 1223;

    private const int FlagDoNotPersist = 0x2;
    private const int FlagExcludeCertificates = 0x8;
    private const int FlagAlwaysShowUi = 0x80;
    private const int FlagGenericCredentials = 0x40000;

    /// <summary>Returns the credentials, or null when the user cancels the window.</summary>
    public static (string UserName, string Password)? Prompt()
    {
        var info = new CredUiInfo
        {
            cbSize = Marshal.SizeOf<CredUiInfo>(),
            hwndParent = IntPtr.Zero,
            pszMessageText = "TDX SANDBOX account. The credentials are used for this execution only and are not saved.",
            pszCaptionText = "TDX Validation Tool",
            hbmBanner = IntPtr.Zero
        };
        var user = new StringBuilder(MaxUserName);
        var password = new StringBuilder(MaxPassword);
        var save = false;
        var flags = FlagGenericCredentials | FlagDoNotPersist | FlagAlwaysShowUi | FlagExcludeCertificates;

        try
        {
            var result = CredUIPromptForCredentialsW(
                ref info, "TDX Sandbox", IntPtr.Zero, 0,
                user, MaxUserName, password, MaxPassword, ref save, flags);

            if (result == ErrorCancelled)
                return null;
            if (result != 0)
                throw new InvalidOperationException($"The Windows credential window failed with code {result}.");

            return (user.ToString().Trim(), password.ToString());
        }
        finally
        {
            user.Clear();
            password.Clear();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CredUiInfo
    {
        public int cbSize;
        public IntPtr hwndParent;
        public string pszMessageText;
        public string pszCaptionText;
        public IntPtr hbmBanner;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode)]
    private static extern int CredUIPromptForCredentialsW(
        ref CredUiInfo pUiInfo,
        string pszTargetName,
        IntPtr reserved,
        int dwAuthError,
        StringBuilder pszUserName,
        int ulUserNameMaxChars,
        StringBuilder pszPassword,
        int ulPasswordMaxChars,
        [MarshalAs(UnmanagedType.Bool)] ref bool pfSave,
        int dwFlags);
}
