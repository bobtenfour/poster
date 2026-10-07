namespace PosterPrintRequest.Tests.Hosting;

public sealed class FrontendFoundationTests
{
    [Fact]
    public async Task Home_uses_the_blueignix_shell_and_welcome()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "usera");

        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("epx-blueignix-shell", html, StringComparison.Ordinal);
        Assert.Contains("Skip to main content", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#epx-main-content\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"epx-main-content\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Primary\"", html, StringComparison.Ordinal);
        Assert.Contains("BlueIgnix", html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", html, StringComparison.Ordinal);
        Assert.Contains(">Home<", html, StringComparison.Ordinal);
        Assert.Contains("Students and staff", html, StringComparison.Ordinal);
        Assert.Contains("What this system does", html, StringComparison.Ordinal);
        Assert.Contains("Start a request", html, StringComparison.Ordinal);
        Assert.Contains("Prepare your poster", html, StringComparison.Ordinal);
        Assert.Contains("Personal activity", html, StringComparison.Ordinal);
        Assert.DoesNotContain("No TDX link", html, StringComparison.Ordinal);
        Assert.DoesNotContain("The application does not integrate with TDX.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Manual TDX ticket", html, StringComparison.Ordinal);
        Assert.Contains("css/design-system/tokens.css", html, StringComparison.Ordinal);
        Assert.Contains("css/design-system/foundation.css", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Error_and_unknown_routes_stay_inside_the_shell()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = factory.CreateClient();

        var error = await client.GetAsync("/Error");
        error.EnsureSuccessStatusCode();
        var errorHtml = await error.Content.ReadAsStringAsync();
        Assert.Contains("epx-blueignix-shell", errorHtml, StringComparison.Ordinal);
        Assert.Contains("An error occurred", errorHtml, StringComparison.Ordinal);
        Assert.Contains("The request could not be completed.", errorHtml, StringComparison.Ordinal);

        var missing = await client.GetAsync("/not-a-page");
        var missingHtml = await missing.Content.ReadAsStringAsync();
        Assert.Contains("Page not found", missingHtml, StringComparison.Ordinal);
        Assert.Contains("epx-nav-primary", missingHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shared_styles_carry_the_blueignix_tokens_and_breakpoints()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = factory.CreateClient();

        var tokens = await client.GetStringAsync("/css/design-system/tokens.css");
        Assert.Contains("--epx-blueignix-bg: #06111f", tokens, StringComparison.Ordinal);
        Assert.Contains("--epx-blueignix-cyan: #3bb7ff", tokens, StringComparison.Ordinal);
        Assert.Contains("--epx-blueignix-text: #f4f8ff", tokens, StringComparison.Ordinal);
        Assert.Contains("--epx-blueignix-shell-width: 17rem", tokens, StringComparison.Ordinal);
        Assert.Contains("--epx-color-text-primary: #0B1F3A", tokens, StringComparison.Ordinal);

        var foundation = await client.GetStringAsync("/css/design-system/foundation.css");
        Assert.Contains(".epx-blueignix-shell", foundation, StringComparison.Ordinal);
        Assert.Contains(".epx-button-primary", foundation, StringComparison.Ordinal);
        Assert.Contains(".epx-input", foundation, StringComparison.Ordinal);
        Assert.Contains(".epx-status-danger", foundation, StringComparison.Ordinal);
        Assert.Contains(".epx-nav-link:focus-visible", foundation, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 768px)", foundation, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1199.98px)", foundation, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 575.98px)", foundation, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", foundation, StringComparison.Ordinal);
    }
}
