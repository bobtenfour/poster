namespace PosterPrintRequest.Infrastructure.Storage;

public sealed class SharedStorageOptions
{
    public const string SectionName = "SharedStorage";

    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Location of the DPAPI-protected credential file. When not configured the service
    /// runs as the process identity. Never holds a credential itself.
    /// </summary>
    public string? CredentialFile { get; set; }
}
