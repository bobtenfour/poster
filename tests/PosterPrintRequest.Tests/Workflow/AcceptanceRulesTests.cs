using System.Globalization;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Tests.Requests;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

public sealed class AcceptanceRulesTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "poster-rules-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public AcceptanceRulesTests()
    {
        _root = Path.Combine(_parent, "root");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Poster_ids_are_public_sequential_and_separate_from_a_database_key()
    {
        Assert.Equal("POSTER-2026-000001", PosterIds.Format(2026, 1));
        Assert.Equal("POSTER-2026-000123", PosterIds.Format(2026, 123));
        Assert.True(PosterIds.TryParse("POSTER-2026-000123", out var year, out var sequence));
        Assert.Equal(2026, year);
        Assert.Equal(123, sequence);
        Assert.False(PosterIds.IsPublic("41"));
        Assert.False(PosterIds.IsPublic("POSTER-2026-000000"));
        Assert.Equal(8, PosterIds.NextSequence(["POSTER-2026-000007", "POSTER-2025-000099", "draft"], 2026));
        Assert.Equal(PosterIds.MaximumSequence + 1, PosterIds.NextSequence([PosterIds.Format(2026, PosterIds.MaximumSequence)], 2026));
    }

    [Fact]
    public void Storage_names_keep_event_text_and_reject_traversal()
    {
        Assert.Equal(
            "Example event, mentor and approval sheet required",
            StorageNames.Segment("Example event, mentor and approval sheet required"));
        Assert.Equal("CRD 2026", StorageNames.Segment("CRD/2026"));
        Assert.Null(StorageNames.Segment(@"..\..\outside"));
        Assert.Null(StorageNames.Segment(".."));
        Assert.Equal("2026", StorageNames.Year(new DateTime(2026, 10, 6)));
        Assert.Equal("Example event 2026", StorageNames.EventFolder("Example event", new DateTime(2026, 10, 6)));
        Assert.Equal("Ada Lovelace - POSTER-2026-000001", StorageNames.PosterFolder("Ada Lovelace", "POSTER-2026-000001"));
        Assert.Equal("POSTER-2026-000001", StorageNames.PosterIdFromFolder("Ada Lovelace - POSTER-2026-000001"));
        Assert.Null(StorageNames.PosterIdFromFolder("POSTER-2026-000001"));
        Assert.Equal(StorageNames.PosterPptx, StorageNames.PosterFileName("PPTX"));
        Assert.Equal(StorageNames.PosterPdf, StorageNames.PosterFileName("PDF"));
    }

    [Fact]
    public void Approval_sheet_must_be_a_readable_pdf_without_poster_dimension_rules()
    {
        Assert.Null(ApprovalSheetRules.Validate(new MemoryStream(SamplePosters.Pdf(80, 80))));
        Assert.Null(ApprovalSheetRules.Validate(new MemoryStream(SamplePosters.Pages([(8.5m, 11m, 0), (8.5m, 11m, 0)]))));
        Assert.Equal(ApprovalSheetRules.MustBePdf, ApprovalSheetRules.Validate(new MemoryStream("sheet-bytes"u8.ToArray())));
        Assert.Equal(ApprovalSheetRules.Unreadable, ApprovalSheetRules.Validate(new MemoryStream(SamplePosters.EmptyPdf())));
    }

    [Fact]
    public void Stored_paths_stay_inside_the_configured_root()
    {
        var secret = Path.Combine(_parent, "secret.pdf");
        File.WriteAllText(secret, "secret");
        var storage = new AcceptedStorage(
            Options.Create(new SharedStorageOptions { RootPath = _root }),
            new DraftFileStore(Options.Create(new SharedStorageOptions { RootPath = _root })));

        var resolved = storage.Resolve("WITHOUT-EVENT/2026/Ada Lovelace - POSTER-2026-000001/Poster.pdf");
        Assert.NotNull(resolved);
        Assert.StartsWith(_root, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("WITHOUT-EVENT", "2026", "Ada Lovelace - POSTER-2026-000001", "Poster.pdf"),
            resolved,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(storage.Resolve("../secret.pdf"));
        Assert.Null(storage.Resolve("/secret.pdf"));
        Assert.Null(storage.Resolve(@"C:\secret.pdf"));
        storage.DeletePlacement("../secret.pdf");
        Assert.Equal("secret", File.ReadAllText(secret));
    }

    [Fact]
    public async Task Accepted_placement_uses_the_event_year_or_without_event_year()
    {
        var files = new DraftFileStore(Options.Create(new SharedStorageOptions { RootPath = _root }));
        var storage = new AcceptedStorage(Options.Create(new SharedStorageOptions { RootPath = _root }), files);
        var poster = SamplePosters.Pdf(24, 36);
        var sheet = SamplePosters.Pdf(8.5m, 11m);
        var draftId = DraftIds.Create();
        await SaveAsync(files, draftId, DraftFileRole.Poster, "original.pdf", poster);
        await SaveAsync(files, draftId, DraftFileRole.ApprovalSheet, "original-sheet.pdf", sheet);
        var dateIn = new DateTime(2026, 10, 6, 15, 4, 0);

        Assert.True(storage.TryPlace(
            draftId,
            "POSTER-2026-000004",
            "Example event",
            dateIn,
            "Ada Lovelace",
            includeApproval: true,
            "PDF",
            out var placed,
            out var error));
        Assert.Null(error);
        Assert.Equal("EVENTS/Example event 2026/Ada Lovelace - POSTER-2026-000004/Poster.pdf", placed!.PosterRelative);
        Assert.Equal("EVENTS/Example event 2026/Ada Lovelace - POSTER-2026-000004/Approval-Sheet.pdf", placed.ApprovalRelative);
        Assert.Equal(poster, await File.ReadAllBytesAsync(storage.Resolve(placed.PosterRelative)!));
        Assert.Equal(sheet, await File.ReadAllBytesAsync(storage.Resolve(placed.ApprovalRelative)!));
        Assert.Empty(Directory.EnumerateFiles(storage.Resolve(placed.DirectoryRelative)!, "*.txt"));

        var openDraft = DraftIds.Create();
        var slides = SamplePosters.Pptx(20, 30);
        await SaveAsync(files, openDraft, DraftFileRole.Poster, "slides.pptx", slides);
        await SaveAsync(files, openDraft, DraftFileRole.ApprovalSheet, "unused-sheet.pdf", sheet);
        Assert.True(storage.TryPlace(
            openDraft,
            "POSTER-2026-000005",
            null,
            dateIn,
            "Ada Lovelace",
            includeApproval: true,
            "PPTX",
            out var withoutEvent,
            out _));
        Assert.Equal("WITHOUT-EVENT/2026/Ada Lovelace - POSTER-2026-000005/Poster.pptx", withoutEvent!.PosterRelative);
        Assert.Null(withoutEvent.ApprovalRelative);
        var withoutDirectory = storage.Resolve(withoutEvent.DirectoryRelative)!;
        Assert.Equal(slides, await File.ReadAllBytesAsync(Path.Combine(withoutDirectory, StorageNames.PosterPptx)));
        Assert.False(File.Exists(Path.Combine(withoutDirectory, StorageNames.ApprovalSheet)));
        Assert.Empty(Directory.EnumerateFiles(withoutDirectory, "*.txt"));

        var laterDraft = DraftIds.Create();
        var laterPoster = SamplePosters.Pdf(24, 48);
        await SaveAsync(files, laterDraft, DraftFileRole.Poster, "later.pdf", laterPoster);
        Assert.True(storage.TryPlace(
            laterDraft,
            "POSTER-2027-000001",
            null,
            new DateTime(2027, 3, 2, 9, 0, 0),
            "Grace Hopper",
            includeApproval: false,
            "PDF",
            out var later,
            out _));
        Assert.Equal("WITHOUT-EVENT/2027/Grace Hopper - POSTER-2027-000001/Poster.pdf", later!.PosterRelative);
        Assert.Equal(
            new[] { "2026", "2027" },
            Directory.GetDirectories(Path.Combine(_root, StorageNames.WithoutEvent)).Select(Path.GetFileName).OrderBy(name => name).ToArray());
        Assert.DoesNotContain(
            Directory.GetDirectories(Path.Combine(_root, StorageNames.WithoutEvent)).Select(Path.GetFileName),
            name => name is { Length: 2 });
    }

    public void Dispose()
    {
        if (Directory.Exists(_parent))
        {
            Directory.Delete(_parent, recursive: true);
        }
    }

    private static async Task SaveAsync(DraftFileStore files, string draftId, DraftFileRole role, string name, byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        var saved = await files.SaveAsync(draftId, role, name, stream, bytes.Length, null, CancellationToken.None);
        Assert.True(saved.Saved);
    }
}
