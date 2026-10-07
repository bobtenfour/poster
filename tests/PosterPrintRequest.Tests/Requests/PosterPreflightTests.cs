using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Requests;

public sealed class PosterPreflightTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-preflight-" + Guid.NewGuid().ToString("N"));
    private readonly DraftFileStore _files;
    private readonly PosterPreflight _preflight;

    public PosterPreflightTests()
    {
        Directory.CreateDirectory(_root);
        _files = new DraftFileStore(Options.Create(new SharedStorageOptions { RootPath = _root }));
        _preflight = new PosterPreflight(_files);
    }

    [Theory]
    [InlineData(36, 72, 0, "PDF, 1 page, 36 inches wide and 72 inches long.")]
    [InlineData(24, 48, 0, "PDF, 1 page, 24 inches wide and 48 inches long.")]
    [InlineData(72, 36, 0, "PDF, 1 page, 36 inches wide and 72 inches long.")]
    [InlineData(40, 20, 0, "PDF, 1 page, 20 inches wide and 40 inches long.")]
    [InlineData(36, 72, 90, "PDF, 1 page, 36 inches wide and 72 inches long.")]
    [InlineData(72, 36, 90, "PDF, 1 page, 36 inches wide and 72 inches long.")]
    [InlineData(72, 36, 180, "PDF, 1 page, 36 inches wide and 72 inches long.")]
    [InlineData(72, 36, 270, "PDF, 1 page, 36 inches wide and 72 inches long.")]
    public void Pdf_posters_are_measured_upright(int width, int height, int rotate, string summary)
    {
        var result = Inspect(SamplePosters.Pdf(width, height, rotate));

        Assert.True(result.Passed);
        Assert.Equal("PDF", result.DetectedFormat);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(summary, result.Summary);
        Assert.Equal(4, result.Checks.Count);
        Assert.All(result.Checks, check => Assert.True(check.Passed));
        Assert.DoesNotContain("autorotate", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pdf_wider_than_36_inches_is_rejected_with_the_measurement()
    {
        var result = Inspect(SamplePosters.Pdf(40, 60));

        Assert.False(result.Passed);
        Assert.Equal(40m, result.WidthInches);
        Assert.Equal(60m, result.LengthInches);
        Assert.Equal(
            "The poster is 40 inches wide. The maximum width is 36 inches. Correct the file and upload it again.",
            result.Summary);
        Assert.Contains(result.Checks, check => check.Label == "Width" && !check.Passed);
        Assert.Contains(result.Checks, check => check.Label == "Length" && check.Passed);
    }

    [Fact]
    public void Pdf_longer_than_72_inches_is_rejected_with_the_measurement()
    {
        var result = Inspect(SamplePosters.Pdf(30, 80));

        Assert.False(result.Passed);
        Assert.Equal(30m, result.WidthInches);
        Assert.Equal(80m, result.LengthInches);
        Assert.Equal(
            "The poster is 80 inches long. The maximum length is 72 inches. Correct the file and upload it again.",
            result.Summary);
    }

    [Fact]
    public void Horizontal_pdf_over_the_length_reports_the_upright_length()
    {
        var result = Inspect(SamplePosters.Pdf(80, 30));

        Assert.False(result.Passed);
        Assert.Equal(30m, result.WidthInches);
        Assert.Equal(80m, result.LengthInches);
        Assert.Equal(
            "The poster is 80 inches long. The maximum length is 72 inches. Correct the file and upload it again.",
            result.Summary);
    }

    [Fact]
    public void Pdf_over_both_maximums_reports_both_measurements()
    {
        var result = Inspect(SamplePosters.Pdf(40, 80));

        Assert.False(result.Passed);
        Assert.Equal(
            "The poster is 40 inches wide. The maximum width is 36 inches. The poster is 80 inches long. The maximum length is 72 inches. Correct the file and upload it again.",
            result.Summary);
    }

    [Fact]
    public void Pdf_just_over_36_inches_is_rejected()
    {
        var result = Inspect(SamplePosters.Pdf(2593m / 72m, 72m));

        Assert.False(result.Passed);
        Assert.Equal(36.01m, result.WidthInches);
        Assert.Contains("36.01 inches wide", result.Summary, StringComparison.Ordinal);
        Assert.Contains("36 inches", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_with_two_pages_is_rejected()
    {
        var result = Inspect(SamplePosters.Pages([(36m, 72m, 0), (24m, 36m, 0)]));

        Assert.False(result.Passed);
        Assert.Equal("PDF", result.DetectedFormat);
        Assert.Equal(2, result.PageCount);
        Assert.Null(result.WidthInches);
        Assert.Equal(
            "The PDF has 2 pages. A poster must contain exactly one page. Correct the file and upload it again.",
            result.Summary);
    }

    [Fact]
    public void Pdf_with_no_pages_is_rejected()
    {
        var result = Inspect(SamplePosters.EmptyPdf());

        Assert.False(result.Passed);
        Assert.Equal(0, result.PageCount);
        Assert.Equal(
            "The PDF has 0 pages. A poster must contain exactly one page. Correct the file and upload it again.",
            result.Summary);
    }

    [Theory]
    [InlineData(36, 72, "PPTX, 1 slide, 36 inches wide and 72 inches long.")]
    [InlineData(24, 48, "PPTX, 1 slide, 24 inches wide and 48 inches long.")]
    [InlineData(72, 36, "PPTX, 1 slide, 36 inches wide and 72 inches long.")]
    [InlineData(40, 20, "PPTX, 1 slide, 20 inches wide and 40 inches long.")]
    public void Pptx_posters_are_measured_upright(int width, int height, string summary)
    {
        var result = Inspect(SamplePosters.Pptx(width, height));

        Assert.True(result.Passed);
        Assert.Equal("PPTX", result.DetectedFormat);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(summary, result.Summary);
        Assert.Contains(result.Checks, check => check.Label == "Slides" && check.Passed && check.Detail == "1 slide");
    }

    [Fact]
    public void Pptx_wider_than_36_inches_is_rejected_with_the_measurement()
    {
        var result = Inspect(SamplePosters.Pptx(40, 50));

        Assert.False(result.Passed);
        Assert.Equal(40m, result.WidthInches);
        Assert.Equal(
            "The poster is 40 inches wide. The maximum width is 36 inches. Correct the file and upload it again.",
            result.Summary);
    }

    [Fact]
    public void Pptx_longer_than_72_inches_is_rejected_with_the_measurement()
    {
        var result = Inspect(SamplePosters.Pptx(20, 80));

        Assert.False(result.Passed);
        Assert.Equal(80m, result.LengthInches);
        Assert.Equal(
            "The poster is 80 inches long. The maximum length is 72 inches. Correct the file and upload it again.",
            result.Summary);
    }

    [Fact]
    public void Horizontal_pptx_over_the_length_reports_the_upright_length()
    {
        var result = Inspect(SamplePosters.Pptx(80, 20));

        Assert.False(result.Passed);
        Assert.Equal(20m, result.WidthInches);
        Assert.Equal(80m, result.LengthInches);
        Assert.Contains("80 inches long", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("80 inches wide", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Pptx_with_two_slides_is_rejected()
    {
        var result = Inspect(SamplePosters.Pptx(36, 72, slides: 2));

        Assert.False(result.Passed);
        Assert.Equal("PPTX", result.DetectedFormat);
        Assert.Equal(2, result.PageCount);
        Assert.Equal(
            "The PowerPoint file has 2 slides. A poster must contain exactly one slide. Correct the file and upload it again.",
            result.Summary);
    }

    [Fact]
    public void Pptx_with_no_slides_is_rejected()
    {
        var result = Inspect(SamplePosters.Pptx(36, 72, slides: 0));

        Assert.False(result.Passed);
        Assert.Equal(
            "The PowerPoint file has 0 slides. A poster must contain exactly one slide. Correct the file and upload it again.",
            result.Summary);
    }

    [Fact]
    public void Corrupt_pdf_fails_with_a_reread_instruction()
    {
        var result = Inspect(SamplePosters.BrokenPdf());

        Assert.False(result.Passed);
        Assert.Equal("This PDF could not be read. Correct the file and upload it again.", result.Summary);
        Assert.Null(result.DetectedFormat);
    }

    [Fact]
    public void Corrupt_package_fails_safely()
    {
        var result = Inspect(SamplePosters.BrokenZip());

        Assert.False(result.Passed);
        Assert.Equal("This file could not be read. Correct the file and upload it again.", result.Summary);
    }

    [Fact]
    public void Pptx_missing_its_slide_fails_safely()
    {
        var result = Inspect(SamplePosters.Pptx(36, 72, includeSlideParts: false));

        Assert.False(result.Passed);
        Assert.Equal("This PowerPoint file could not be read. Correct the file and upload it again.", result.Summary);
    }

    [Theory]
    [MemberData(nameof(UnsupportedFiles))]
    public void Unsupported_content_is_rejected(byte[] bytes)
    {
        var result = Inspect(bytes);

        Assert.False(result.Passed);
        Assert.Equal("This file is not a PDF or PPTX poster. Correct the file and upload it again.", result.Summary);
    }

    public static IEnumerable<object[]> UnsupportedFiles() =>
    [
        [SamplePosters.Png()],
        [SamplePosters.WordDocument()],
        [Encoding.UTF8.GetBytes("hello")],
        [Array.Empty<byte>()]
    ];

    [Fact]
    public async Task Inspection_does_not_change_the_stored_poster()
    {
        var poster = SamplePosters.Pdf(72, 36, 90);
        var draftId = DraftIds.Create();
        await using (var input = new MemoryStream(poster))
        {
            var saved = await _files.SaveAsync(draftId, DraftFileRole.Poster, "Landscape.pdf", input, poster.Length, null, CancellationToken.None);
            Assert.True(saved.Saved);
        }

        var path = _files.PosterPath(draftId);
        Assert.NotNull(path);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var result = await _preflight.InspectStoredPosterAsync(draftId, CancellationToken.None);
        var after = SHA256.HashData(await File.ReadAllBytesAsync(path));

        Assert.Equal(before, after);
        Assert.NotNull(result);
        Assert.True(result.Passed);
        Assert.Equal(36m, result.WidthInches);
        Assert.Equal(72m, result.LengthInches);
        Assert.DoesNotContain(path, result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PosterPreflightResult Inspect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return _preflight.Inspect(stream);
    }
}
