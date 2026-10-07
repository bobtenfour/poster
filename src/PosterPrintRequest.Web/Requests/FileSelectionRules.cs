namespace PosterPrintRequest.Web.Requests;

public readonly record struct FileSelection(string? Format, string? Error)
{
    public bool Succeeded => Error is null && Format is not null;
}

public static class FileSelectionRules
{
    public static FileSelection InspectPoster(string? fileName, long size) =>
        Inspect(fileName, size, allowPowerPoint: true, wrongTypeMessage: "Choose a PDF or PPTX file.");

    public static FileSelection InspectApprovalSheet(string? fileName, long size) =>
        Inspect(fileName, size, allowPowerPoint: false, wrongTypeMessage: "Choose a PDF approval sheet.");

    private static FileSelection Inspect(string? fileName, long size, bool allowPowerPoint, string wrongTypeMessage)
    {
        if (size <= 0)
        {
            return new FileSelection(null, "Choose a file that is not empty.");
        }

        if (size > UploadLimits.MaxBytes)
        {
            return new FileSelection(null, $"Choose a file up to {UploadLimits.MaxLabel}.");
        }

        var extension = Path.GetExtension(Path.GetFileName(fileName ?? "")).ToLowerInvariant();
        if (extension == ".pdf")
        {
            return new FileSelection("PDF", null);
        }

        if (allowPowerPoint && extension == ".pptx")
        {
            return new FileSelection("PPTX", null);
        }

        return new FileSelection(null, wrongTypeMessage);
    }
}
