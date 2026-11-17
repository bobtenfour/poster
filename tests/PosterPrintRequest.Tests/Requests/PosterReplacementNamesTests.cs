using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Requests;

/// <summary>TDX Integration Contract sections 17.5, 17.6 and 17.8: names, note rule and the reverse folder mapping.</summary>
public sealed class PosterReplacementNamesTests
{
    [Fact]
    public void Archive_folder_and_files_follow_the_contract_names()
    {
        var at = new DateTime(2026, 11, 16, 9, 5, 7);

        Assert.Equal("2026-11-16 POSTER-2026-000123 TDX-2262488", ReplacementNames.Folder(at, "POSTER-2026-000123", 2262488));
        Assert.Equal("2026-11-16 POSTER-2026-000123 TDX-PENDING", ReplacementNames.Folder(at, "POSTER-2026-000123", null));
        Assert.Equal("090507 - Poster.pdf", ReplacementNames.ArchivedPoster(at, "Poster.pdf"));
        Assert.Equal("090507 - Poster.pptx", ReplacementNames.ArchivedPoster(at, "Poster.pptx"));
        Assert.Equal("090507 - Note.txt", ReplacementNames.Note(at));
    }

    [Theory]
    [InlineData("2026-11-16 POSTER-2026-000123 TDX-2262488", true)]
    [InlineData("2026-11-16 POSTER-2026-000123 TDX-PENDING", true)]
    [InlineData("2026-13-16 POSTER-2026-000123 TDX-1", false)]
    [InlineData("2026-11-16 POSTER-2026-000123", false)]
    [InlineData("2026-11-16 POSTER-2026-000123 TDX-", false)]
    [InlineData("2026-11-16 POSTER-2026-000123 TDX-12a", false)]
    [InlineData("Ada - POSTER-2026-000123", false)]
    [InlineData("..", false)]
    public void Archive_folder_names_are_recognized(string name, bool valid)
    {
        Assert.Equal(valid, ReplacementNames.IsArchiveFolder(name));
    }

    [Theory]
    [InlineData("090507 - Poster.pdf", true)]
    [InlineData("090507 - Poster.pptx", true)]
    [InlineData("090507 - Note.txt", true)]
    [InlineData("250507 - Poster.pdf", false)]
    [InlineData("090507 - Other.pdf", false)]
    [InlineData("Poster.pdf", false)]
    public void Archive_file_names_are_recognized(string name, bool valid)
    {
        Assert.Equal(valid, ReplacementNames.IsArchiveFile(name));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("Wrong author list", true)]
    [InlineData("Line\nbreak", false)]
    public void The_note_is_required_and_on_one_line(string note, bool valid)
    {
        Assert.Equal(valid, ReplacementNames.CheckNote(note) is null);
    }

    [Fact]
    public void The_note_is_at_most_500_characters()
    {
        Assert.Null(ReplacementNames.CheckNote(new string('n', 500)));
        Assert.NotNull(ReplacementNames.CheckNote(new string('n', 501)));
    }

    [Fact]
    public void A_printed_file_maps_back_to_its_to_be_printed_folder()
    {
        Assert.True(PrintFolderPaths.TryMapToBePrintedFile("EVENTS/Research Day 2026 PRINTED/Ada - POSTER-2026-000001/Poster.pdf", out var events));
        Assert.Equal("EVENTS/Research Day 2026 TO BE PRINTED/Ada - POSTER-2026-000001/Poster.pdf", events);
        Assert.True(PrintFolderPaths.TryMapToBePrintedFile("WITHOUT-EVENT PRINTED/2026/Ada - POSTER-2026-000001/Poster.pptx", out var without));
        Assert.Equal("WITHOUT-EVENT TO BE PRINTED/2026/Ada - POSTER-2026-000001/Poster.pptx", without);
        Assert.False(PrintFolderPaths.TryMapToBePrintedFile("EVENTS/Research Day 2026 TO BE PRINTED/Ada - POSTER-2026-000001/Poster.pdf", out _));
        Assert.False(PrintFolderPaths.TryMapToBePrintedFile("WITHOUT-EVENT TO BE PRINTED/2026/Ada - POSTER-2026-000001/Poster.pdf", out _));

        Assert.True(PrintFolderPaths.TryMapPrintedFile(events, out var roundTrip));
        Assert.Equal("EVENTS/Research Day 2026 PRINTED/Ada - POSTER-2026-000001/Poster.pdf", roundTrip);
    }
}
