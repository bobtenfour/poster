namespace PosterPrintRequest.Infrastructure.Storage;

public sealed class SharedStorageOptions
{
    public const string SectionName = "SharedStorage";

    public string RootPath { get; set; } = string.Empty;
}
