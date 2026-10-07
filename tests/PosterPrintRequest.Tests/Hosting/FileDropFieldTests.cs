using Bunit;
using PosterPrintRequest.Web.Components.Foundation;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class FileDropFieldTests : TestContext
{
    [Fact]
    public void Uploaded_file_shows_its_name_status_and_remove_action()
    {
        var cut = RenderComponent<FileDropField>(parameters => parameters
            .Add(field => field.InputId, "poster-file")
            .Add(field => field.Label, "Poster")
            .Add(field => field.Required, true)
            .Add(field => field.FileName, "Poster.pdf")
            .Add(field => field.SizeLabel, "12 KB")
            .Add(field => field.Status, "Poster uploaded.")
            .Add(field => field.Error, "Choose a PDF or PPTX file."));

        Assert.Contains("Poster.pdf", cut.Markup);
        Assert.Contains("12 KB", cut.Markup);
        Assert.Contains("Poster uploaded.", cut.Find("[aria-live='polite']").TextContent);
        Assert.Contains("Remove", cut.Markup);
        Assert.Contains("Choose a PDF or PPTX file.", cut.Find("[role='alert']").TextContent);
        Assert.Equal("true", cut.Find("input").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void Upload_progress_is_exposed()
    {
        var cut = RenderComponent<FileDropField>(parameters => parameters
            .Add(field => field.InputId, "poster-file")
            .Add(field => field.Label, "Poster")
            .Add(field => field.Uploading, true)
            .Add(field => field.Progress, 40)
            .Add(field => field.Status, "Uploading 40%."));

        var progress = cut.Find("[role='progressbar']");
        Assert.Equal("40", progress.GetAttribute("aria-valuenow"));
        Assert.Equal("0", progress.GetAttribute("aria-valuemin"));
        Assert.Equal("100", progress.GetAttribute("aria-valuemax"));
    }
}
