namespace PosterPrintRequest.Tests.Hosting;

public sealed class RequestPageTests
{
    [Fact]
    public async Task Request_form_is_in_the_shell_and_keeps_help_available()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "usera");

        var response = await client.GetAsync("/request");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("epx-blueignix-shell", html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", html, StringComparison.Ordinal);
        Assert.Contains(">Request<", html, StringComparison.Ordinal);
        Assert.Contains("id=\"requester-name\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"requester-mentor\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"requester-department\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"requester-reason\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"requester-lamination\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"poster-file\"", html, StringComparison.Ordinal);
        Assert.Contains("Example Department", html, StringComparison.Ordinal);
        Assert.Contains("Example event, mentor required", html, StringComparison.Ordinal);
        Assert.Contains("Example event, approval sheet required", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/help#poster-size\"", html, StringComparison.Ordinal);
        Assert.Contains("one page or a PPTX with one slide", html, StringComparison.Ordinal);
        Assert.DoesNotContain("does not check page count", html, StringComparison.Ordinal);
        Assert.Contains("Submit request", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/technician\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Posters pending printing", html, StringComparison.Ordinal);
        Assert.Contains("js/poster-draft.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain("StoragePath", html, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\posters", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Retired_save_page_is_not_available()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/request/received");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Request saved", html, StringComparison.Ordinal);
        Assert.DoesNotContain("StoragePath", html, StringComparison.Ordinal);
    }
}
