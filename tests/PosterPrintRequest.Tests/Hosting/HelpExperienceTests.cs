namespace PosterPrintRequest.Tests.Hosting;

public sealed class HelpExperienceTests
{
    [Fact]
    public async Task Help_explains_the_approved_poster_requirements()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "usera");

        var response = await client.GetAsync("/help");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("epx-blueignix-shell", html, StringComparison.Ordinal);
        Assert.Contains("Skip to main content", html, StringComparison.Ordinal);
        Assert.Contains("Prepare Your Poster", html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", html, StringComparison.Ordinal);
        Assert.Contains(">Help<", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/help\"", html, StringComparison.Ordinal);
        Assert.Contains("36 inches", html, StringComparison.Ordinal);
        Assert.Contains("72 inches", html, StringComparison.Ordinal);
        Assert.Contains("Recommended width", html, StringComparison.Ordinal);
        Assert.Contains("Maximum width", html, StringComparison.Ordinal);
        Assert.Contains("Recommended length", html, StringComparison.Ordinal);
        Assert.Contains(
            "There is no strict maximum length. We recommend keeping the poster at or below 72 inches for best results.",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Maximum length", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Longer than 72 inches is not accepted.", html, StringComparison.Ordinal);
        Assert.Contains("/img/help/BlueIgnix_Poster_Dimensions_Final.png", html, StringComparison.Ordinal);
        Assert.Contains("/img/help/BlueIgnix_Poster_Dimensions.png", html, StringComparison.Ordinal);
        Assert.Contains(
            "A poster dimension diagram showing a maximum width of 36 inches and a recommended length of 72 inches, with inches and millimeters marked. The diagram states there is no strict maximum length.",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "A poster coming from a wide-format printer with horizontal and vertical dimension annotations showing inches and millimeters.",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("epx-dimension-svg", html, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/", html, StringComparison.Ordinal);
        Assert.Contains("exactly one page", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly one slide", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PDF", html, StringComparison.Ordinal);
        Assert.Contains("PPTX", html, StringComparison.Ordinal);
        Assert.Contains("Approval Sheet", html, StringComparison.Ordinal);
        Assert.Contains(">Good<", html, StringComparison.Ordinal);
        Assert.Contains(">Not accepted<", html, StringComparison.Ordinal);
        Assert.Contains("id=\"poster-size\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Help topics\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/help#poster-size\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/help#acceptable-preparation\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiresApprovalSheet", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ApprovalSheetUploaded", html, StringComparison.Ordinal);
        Assert.DoesNotContain("StoragePath", html, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\posters", html, StringComparison.OrdinalIgnoreCase);

        var diagram = await client.GetAsync("/img/help/BlueIgnix_Poster_Dimensions_Final.png");
        diagram.EnsureSuccessStatusCode();
        Assert.Equal("image/png", diagram.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await File.ReadAllBytesAsync(ApprovedFile("BlueIgnix_Poster_Dimensions_Final.png")), await diagram.Content.ReadAsByteArrayAsync());

        var printer = await client.GetAsync("/img/help/BlueIgnix_Poster_Dimensions.png");
        printer.EnsureSuccessStatusCode();
        Assert.Equal("image/png", printer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await File.ReadAllBytesAsync(ApprovedFile("BlueIgnix_Poster_Dimensions.png")), await printer.Content.ReadAsByteArrayAsync());
    }

    private static string ApprovedFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", fileName)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "docs", fileName);
    }

    [Fact]
    public async Task Home_links_to_the_help_experience()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "usera");

        var html = await client.GetStringAsync("/");

        Assert.Contains("href=\"/help\"", html, StringComparison.Ordinal);
        Assert.Contains(">Help<", html, StringComparison.Ordinal);
        Assert.Contains("Prepare your poster", html, StringComparison.Ordinal);
        Assert.Contains("Internal system for students and staff to request poster printing.", html, StringComparison.Ordinal);
    }
}
