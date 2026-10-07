using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Requests;

public sealed class DraftFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-draft-" + Guid.NewGuid().ToString("N"));
    private readonly DraftFileStore _store;

    public DraftFileStoreTests()
    {
        Directory.CreateDirectory(_root);
        _store = new DraftFileStore(Options.Create(new SharedStorageOptions { RootPath = _root }));
    }

    [Fact]
    public async Task Save_keeps_a_poster_and_replaces_it()
    {
        var draftId = DraftIds.Create();
        var saved = await SaveAsync(draftId, DraftFileRole.Poster, "Poster.PDF", "poster");

        Assert.True(saved.Saved);
        Assert.Equal("PDF", saved.Format);
        Assert.True(_store.Exists(draftId, DraftFileRole.Poster));
        Assert.EndsWith("poster.pdf", _store.PosterPath(draftId), StringComparison.OrdinalIgnoreCase);

        var replaced = await SaveAsync(draftId, DraftFileRole.Poster, "Slides.pptx", "slides");

        Assert.True(replaced.Saved);
        Assert.Equal("PPTX", replaced.Format);
        Assert.EndsWith("poster.pptx", _store.PosterPath(draftId), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_root, "drafts", draftId, "poster.pdf")));
    }

    [Fact]
    public async Task Rejected_file_is_not_written()
    {
        var draftId = DraftIds.Create();

        var saved = await SaveAsync(draftId, DraftFileRole.ApprovalSheet, "Sheet.docx", "sheet");

        Assert.False(saved.Saved);
        Assert.Equal("Choose a PDF approval sheet.", saved.Error);
        Assert.False(_store.Exists(draftId, DraftFileRole.ApprovalSheet));
    }

    [Fact]
    public async Task Remove_deletes_only_the_selected_role()
    {
        var draftId = DraftIds.Create();
        await SaveAsync(draftId, DraftFileRole.Poster, "Poster.pdf", "poster");
        await SaveAsync(draftId, DraftFileRole.ApprovalSheet, "Sheet.pdf", "sheet");

        _store.Remove(draftId, DraftFileRole.ApprovalSheet);

        Assert.True(_store.Exists(draftId, DraftFileRole.Poster));
        Assert.False(_store.Exists(draftId, DraftFileRole.ApprovalSheet));
    }

    [Fact]
    public async Task Invalid_draft_id_is_rejected()
    {
        var saved = await SaveAsync("../escape", DraftFileRole.Poster, "Poster.pdf", "poster");

        Assert.False(saved.Saved);
        Assert.False(Directory.Exists(Path.Combine(_root, "drafts")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<DraftSaveResult> SaveAsync(string draftId, DraftFileRole role, string fileName, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        await using var stream = new MemoryStream(bytes);
        return await _store.SaveAsync(draftId, role, fileName, stream, bytes.Length, null, CancellationToken.None);
    }
}
