using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Requests;

public sealed class FileSelectionRulesTests
{
    [Theory]
    [InlineData("Poster.pdf")]
    [InlineData("Poster.PDF")]
    [InlineData("slides.pptx")]
    [InlineData("slides.PPTX")]
    public void Poster_accepts_pdf_and_pptx(string fileName)
    {
        var selection = FileSelectionRules.InspectPoster(fileName, 1024);

        Assert.True(selection.Succeeded);
        Assert.Null(selection.Error);
    }

    [Theory]
    [InlineData("notes.docx")]
    [InlineData("poster.png")]
    [InlineData("poster.ppt")]
    [InlineData("poster.pdf.exe")]
    public void Poster_rejects_other_file_types(string fileName)
    {
        var selection = FileSelectionRules.InspectPoster(fileName, 1024);

        Assert.False(selection.Succeeded);
        Assert.Equal("Choose a PDF or PPTX file.", selection.Error);
    }

    [Fact]
    public void Approval_sheet_accepts_only_pdf()
    {
        Assert.True(FileSelectionRules.InspectApprovalSheet("Approval.pdf", 1024).Succeeded);
        Assert.Equal(
            "Choose a PDF approval sheet.",
            FileSelectionRules.InspectApprovalSheet("Approval.pptx", 1024).Error);
    }

    [Fact]
    public void Empty_and_oversized_files_are_rejected()
    {
        Assert.Equal("Choose a file that is not empty.", FileSelectionRules.InspectPoster("Poster.pdf", 0).Error);
        Assert.Equal(
            $"Choose a file up to {UploadLimits.MaxLabel}.",
            FileSelectionRules.InspectPoster("Poster.pdf", UploadLimits.MaxBytes + 1).Error);
    }
}
