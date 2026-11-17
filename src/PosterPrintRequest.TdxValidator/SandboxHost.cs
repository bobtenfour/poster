namespace PosterPrintRequest.TdxValidator;

/// <summary>Contract §4. In the repository, BasePath is only the placeholder; the owner replaces it locally before publishing.</summary>
public static class SandboxHost
{
    public const string FixedHost = "help.uillinois.edu";

    // Before publishing, replace EVERYTHING between the quotes, including the < and > signs.
    // Keep the quotes. Path only: no https://, no host, no /api at the end.
    //   Before: "<TDX_SANDBOX_API_PATH>"
    //   After:  "/EXAMPLE/TDWebApi"
    public const string BasePath = "<TDX_SANDBOX_API_PATH>";
}
