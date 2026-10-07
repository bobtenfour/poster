namespace PosterPrintRequest.Web.Requests;

public enum DraftFileRole
{
    Poster,
    ApprovalSheet
}

public sealed record DraftSaveResult(bool Saved, string? Error, string? Format);

public interface IDraftFileStore
{
    Task<DraftSaveResult> SaveAsync(
        string draftId,
        DraftFileRole role,
        string originalFileName,
        Stream content,
        long size,
        IProgress<int>? progress,
        CancellationToken cancellationToken);

    void Remove(string draftId, DraftFileRole role);

    bool Exists(string draftId, DraftFileRole role);
}
