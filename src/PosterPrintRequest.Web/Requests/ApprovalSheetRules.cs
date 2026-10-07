using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

namespace PosterPrintRequest.Web.Requests;

public interface IApprovalSheetCheck
{
    Task<string?> ValidateStoredAsync(string draftId, CancellationToken cancellationToken);
}

public sealed class ApprovalSheetCheck : IApprovalSheetCheck
{
    private readonly DraftFileStore _files;

    public ApprovalSheetCheck(DraftFileStore files)
    {
        _files = files;
    }

    public async Task<string?> ValidateStoredAsync(string draftId, CancellationToken cancellationToken)
    {
        var path = _files.ApprovalSheetPath(draftId);
        if (path is null)
        {
            return ApprovalSheetRules.Missing;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return ApprovalSheetRules.Validate(stream);
        }
        catch (IOException)
        {
            return ApprovalSheetRules.Unreadable;
        }
    }
}

public static class ApprovalSheetRules
{
    public const string Missing = "Choose the Approval Sheet PDF for this event.";

    public const string MustBePdf = "The Approval Sheet must be a PDF. Correct the file and upload it again.";

    public const string Unreadable = "This Approval Sheet PDF could not be read. Correct the file and upload it again.";

    public static string? Validate(Stream content)
    {
        byte[] bytes;
        try
        {
            using var memory = new MemoryStream();
            content.CopyTo(memory);
            bytes = memory.ToArray();
        }
        catch (IOException)
        {
            return Unreadable;
        }

        if (bytes.Length < 5
            || bytes[0] != (byte)'%'
            || bytes[1] != (byte)'P'
            || bytes[2] != (byte)'D'
            || bytes[3] != (byte)'F')
        {
            return MustBePdf;
        }

        try
        {
            using var document = PdfDocument.Open(bytes);
            return document.NumberOfPages < 1 ? Unreadable : null;
        }
        catch (PdfDocumentEncryptedException)
        {
            return Unreadable;
        }
        catch (PdfDocumentFormatException)
        {
            return Unreadable;
        }
    }
}
