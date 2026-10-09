using System.Diagnostics;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

public sealed class TechnicianLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-library-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "poster-outside-" + Guid.NewGuid().ToString("N"));

    public TechnicianLibraryTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    [Fact]
    public void Library_stays_inside_the_approved_storage_shape()
    {
        var poster = Path.Combine(_root, PrintFolderPaths.WithoutEventToBePrinted, "2026", "Ada Lovelace - POSTER-2026-000010");
        Directory.CreateDirectory(poster);
        File.WriteAllBytes(Path.Combine(poster, StorageNames.PosterPdf), "pdf"u8.ToArray());
        File.WriteAllBytes(Path.Combine(poster, StorageNames.ApprovalSheet), "sheet"u8.ToArray());
        File.WriteAllText(Path.Combine(poster, "Ada Lovelace.txt"), "");
        File.WriteAllText(Path.Combine(poster, "notes.doc"), "hidden");
        Directory.CreateDirectory(Path.Combine(_root, PrintFolderPaths.WithoutEventToBePrinted, "10"));
        File.WriteAllText(Path.Combine(_root, PrintFolderPaths.WithoutEventToBePrinted, "10", "hidden-month.txt"), "hidden");
        Directory.CreateDirectory(Path.Combine(_root, StorageNames.WithoutEvent, "2026"));
        var later = Path.Combine(_root, PrintFolderPaths.WithoutEventToBePrinted, "2027", "Grace Hopper - POSTER-2027-000001");
        Directory.CreateDirectory(later);
        File.WriteAllBytes(Path.Combine(later, StorageNames.PosterPdf), "later"u8.ToArray());
        Directory.CreateDirectory(Path.Combine(_root, "drafts"));
        File.WriteAllText(Path.Combine(_root, "drafts", "secret.txt"), "draft-secret");
        Directory.CreateDirectory(Path.Combine(_root, "requests"));
        var eventPoster = Path.Combine(_root, StorageNames.Events, "Example event 2026 TO BE PRINTED", "Ada Lovelace - POSTER-2026-000011");
        Directory.CreateDirectory(Path.Combine(_root, StorageNames.Events, "Example event 2026"));
        Directory.CreateDirectory(eventPoster);
        File.WriteAllBytes(Path.Combine(eventPoster, StorageNames.PosterPptx), "pptx"u8.ToArray());
        File.WriteAllBytes(Path.Combine(eventPoster, StorageNames.ApprovalSheet), "event-sheet"u8.ToArray());
        Directory.CreateDirectory(Path.Combine(_root, "Example event, mentor required", "POSTER-2026-000099"));
        File.WriteAllText(Path.Combine(_outside, "secret.pdf"), "outside-secret");
        var link = Path.Combine(_root, "linked-out");
        CreateJunction(link, _outside);

        var library = Create(_root);
        var root = library.List(null);
        Assert.NotNull(root);
        Assert.True(root.Available);
        Assert.Equal(4, root.Entries.Count);
        Assert.Equal("TO BE PRINTED", root.Entries[0].Name);
        Assert.Equal("Events", root.Entries[0].Kind);
        Assert.Equal(PrintFolderPaths.ToBePrintedTone, root.Entries[0].Tone);
        Assert.Equal("/technician/library/EVENTS%20TO%20BE%20PRINTED", root.Entries[0].NavigateHref);
        Assert.Equal("PRINTED", root.Entries[1].Name);
        Assert.Equal("Events", root.Entries[1].Kind);
        Assert.Equal(PrintFolderPaths.PrintedTone, root.Entries[1].Tone);
        Assert.Equal("/technician/library/EVENTS%20PRINTED", root.Entries[1].NavigateHref);
        Assert.Equal("TO BE PRINTED", root.Entries[2].Name);
        Assert.Equal("Without event", root.Entries[2].Kind);
        Assert.Equal(PrintFolderPaths.ToBePrintedTone, root.Entries[2].Tone);
        Assert.Equal("/technician/library/WITHOUT-EVENT%20TO%20BE%20PRINTED", root.Entries[2].NavigateHref);
        Assert.Equal("PRINTED", root.Entries[3].Name);
        Assert.Equal("Without event", root.Entries[3].Kind);
        Assert.Equal(PrintFolderPaths.PrintedTone, root.Entries[3].Tone);
        Assert.DoesNotContain(root.Entries, entry => entry.Name == StorageNames.WithoutEvent);
        Assert.DoesNotContain(root.Entries, entry => entry.Name.Equals("drafts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(root.Entries, entry => entry.Name.Equals("requests", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(root.Entries, entry => entry.Name == "linked-out");
        Assert.DoesNotContain(root.Entries, entry => entry.Name == "Example event, mentor required");
        Assert.All(root.Entries, entry => Assert.DoesNotContain(_root, entry.NavigateHref ?? "", StringComparison.OrdinalIgnoreCase));

        var years = library.List(PrintFolderPaths.WithoutEventToBePrinted);
        Assert.NotNull(years);
        Assert.Contains(years.Entries, entry => entry.Name == "2026" && entry.Kind == "Year");
        Assert.Contains(years.Entries, entry => entry.Name == "2027" && entry.Kind == "Year");
        Assert.DoesNotContain(years.Entries, entry => entry.Name == "10");

        var posters = library.List(PrintFolderPaths.WithoutEventToBePrinted + "/2026");
        Assert.NotNull(posters);
        var posterEntry = Assert.Single(posters.Entries);
        Assert.Equal("Ada Lovelace - POSTER-2026-000010", posterEntry.Name);
        Assert.Equal("/technician/work/POSTER-2026-000010", posterEntry.WorkHref);

        var files = library.List(PrintFolderPaths.WithoutEventToBePrinted + "/2026/Ada Lovelace - POSTER-2026-000010");
        Assert.NotNull(files);
        var posterFile = Assert.Single(files.Entries);
        Assert.Equal(StorageNames.PosterPdf, posterFile.Name);
        Assert.Equal("/technician/files/POSTER-2026-000010/poster?inline=true", posterFile.ViewHref);
        Assert.DoesNotContain(files.Entries, entry => entry.Name == StorageNames.ApprovalSheet);
        Assert.DoesNotContain(files.Entries, entry => entry.Name == "Ada Lovelace.txt");
        Assert.DoesNotContain(files.Entries, entry => entry.Name == "notes.doc");
        Assert.All(files.Entries, entry =>
        {
            Assert.DoesNotContain(_root, entry.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(_root, entry.ViewHref ?? "", StringComparison.OrdinalIgnoreCase);
        });

        var laterPosters = library.List(PrintFolderPaths.WithoutEventToBePrinted + "/2027");
        Assert.NotNull(laterPosters);
        Assert.Contains(laterPosters.Entries, entry => entry.Name == "Grace Hopper - POSTER-2027-000001");

        var events = library.List(PrintFolderPaths.EventsToBePrinted);
        Assert.NotNull(events);
        Assert.Contains(events.Entries, entry => entry.Name == "Example event 2026 TO BE PRINTED" && entry.Kind == "Event" && entry.Tone == PrintFolderPaths.ToBePrintedTone);
        Assert.DoesNotContain(events.Entries, entry => entry.Name == "Example event 2026");
        var printedEvents = library.List(PrintFolderPaths.EventsPrinted);
        Assert.NotNull(printedEvents);
        Assert.Empty(printedEvents.Entries);

        var eventFiles = library.List("EVENTS TO BE PRINTED/Example event 2026 TO BE PRINTED/Ada Lovelace - POSTER-2026-000011");
        Assert.NotNull(eventFiles);
        Assert.Contains(eventFiles.Entries, entry => entry.Name == StorageNames.PosterPptx && entry.ViewHref == "/technician/files/POSTER-2026-000011/poster?inline=true");
        Assert.Contains(eventFiles.Entries, entry => entry.Name == StorageNames.ApprovalSheet && entry.ViewHref == "/technician/files/POSTER-2026-000011/approval?inline=true");
        Assert.Contains("%20", TechnicianLibrary.LibraryHref(["Example event 2026"]), StringComparison.Ordinal);

        Assert.Null(library.List("drafts"));
        Assert.Null(library.List("requests"));
        Assert.Null(library.List(".."));
        Assert.Null(library.List("WITHOUT-EVENT/../drafts"));
        Assert.Null(library.List("WITHOUT-EVENT/10"));
        Assert.Null(library.List("WITHOUT-EVENT/2026/Ada Lovelace - POSTER-2026-000010/Poster.pdf"));
        Assert.Null(library.List("linked-out"));
        Assert.Null(library.List(_outside));
        Assert.Null(library.List(@"\\server\share"));
        Assert.Null(library.List("Example event, mentor required"));
        Assert.DoesNotContain("outside-secret", files.Entries.Select(entry => entry.Name), StringComparer.Ordinal);

        var missingRoot = Create(Path.Combine(_root, "missing-root"));
        var unavailable = missingRoot.List(null);
        Assert.NotNull(unavailable);
        Assert.False(unavailable.Available);
        Assert.Empty(unavailable.Entries);
        Assert.Null(missingRoot.List("drafts"));
    }

    public void Dispose()
    {
        var link = Path.Combine(_root, "linked-out");
        if (Directory.Exists(link))
        {
            Directory.Delete(link);
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        if (Directory.Exists(_outside))
        {
            Directory.Delete(_outside, recursive: true);
        }
    }

    private static TechnicianLibrary Create(string root)
    {
        var options = Options.Create(new SharedStorageOptions { RootPath = root });
        return new TechnicianLibrary(options, new AcceptedStorage(options, new DraftFileStore(options)));
    }

    private static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.NotNull(process);
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd());
    }
}
