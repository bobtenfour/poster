using Bunit;
using PosterPrintRequest.Web.Components.Foundation;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class PosterPreflightReportTests : TestContext
{
    [Fact]
    public void Passed_checks_show_the_measured_poster()
    {
        var cut = RenderComponent<PosterPreflightReport>(parameters => parameters.Add(
            report => report.Result,
            new PosterPreflightResult
            {
                Passed = true,
                Summary = "PDF, 1 page, 24 inches wide and 48 inches long.",
                DetectedFormat = "PDF",
                PageCount = 1,
                WidthInches = 24,
                LengthInches = 48,
                Checks =
                [
                    new PosterPreflightCheck { Label = "Format", Passed = true, Detail = "PDF" },
                    new PosterPreflightCheck { Label = "Pages", Passed = true, Detail = "1 page" },
                    new PosterPreflightCheck { Label = "Width", Passed = true, Detail = "24 inches wide. The maximum is 36 inches." },
                    new PosterPreflightCheck { Label = "Length", Passed = true, Detail = "48 inches long. The maximum is 72 inches." }
                ]
            }));

        Assert.Contains("Poster check", cut.Markup);
        Assert.Contains("PDF, 1 page, 24 inches wide and 48 inches long.", cut.Markup);
        Assert.Equal(4, cut.FindAll(".epx-status-success").Count);
        Assert.Contains("Width is measured across and length is measured down.", cut.Markup);
        Assert.DoesNotContain("Needs a correction", cut.Markup);
        Assert.DoesNotContain("StoragePath", cut.Markup);
    }

    [Fact]
    public void Failed_checks_name_the_correction()
    {
        var cut = RenderComponent<PosterPreflightReport>(parameters => parameters.Add(
            report => report.Result,
            new PosterPreflightResult
            {
                Passed = false,
                Summary = "The PowerPoint file has 2 slides. A poster must contain exactly one slide. Correct the file and upload it again.",
                DetectedFormat = "PPTX",
                PageCount = 2,
                Checks =
                [
                    new PosterPreflightCheck { Label = "Format", Passed = true, Detail = "PPTX" },
                    new PosterPreflightCheck { Label = "Slides", Passed = false, Detail = "2 slides. A poster must contain exactly one slide." }
                ]
            }));

        Assert.Contains("Needs a correction", cut.Markup);
        Assert.Contains("exactly one slide", cut.Markup);
        Assert.Contains("Passed", cut.Markup);
        Assert.DoesNotContain("Width is measured across", cut.Markup);
    }
}
