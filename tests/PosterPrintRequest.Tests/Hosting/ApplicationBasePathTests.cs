using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PosterPrintRequest.Web;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class ApplicationBasePathTests
{
    [Fact]
    public async Task Root_host_keeps_documents_at_the_origin()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "usera");

        var html = await client.GetStringAsync("/");

        Assert.Contains("<base href=\"http://localhost/\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PosterPrintRequest/", html, StringComparison.Ordinal);
        AssertStylesheets(html);
        Assert.Contains("src=\"js/poster-draft.js\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"_framework/blazor.web.js\"", html, StringComparison.Ordinal);

        var tokens = await client.GetAsync("/css/design-system/tokens.css");
        tokens.EnsureSuccessStatusCode();
        var script = await client.GetAsync("/_framework/blazor.web.js");
        script.EnsureSuccessStatusCode();
        Assert.Contains("blazor", await script.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Virtual_directory_resolves_the_document_navigation_and_static_assets()
    {
        using var factory = new PosterWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, VirtualDirectoryStartup>());
        });
        using var client = await PosterSignIn.SignInAsync(factory, "usera");

        var home = await client.GetAsync("/PosterPrintRequest/");
        home.EnsureSuccessStatusCode();
        Assert.EndsWith("/PosterPrintRequest/staff", home.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("<base href=\"http://localhost/PosterPrintRequest/\"", html, StringComparison.Ordinal);
        AssertStylesheets(html);
        Assert.Contains("src=\"js/poster-draft.js\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"_framework/blazor.web.js\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/PosterPrintRequest/img/entry/plotter-technician.jpg\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/PosterPrintRequest/request\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/PosterPrintRequest/help\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/css/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"/_framework/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"/js/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/request\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/help\"", html, StringComparison.Ordinal);

        foreach (var path in new[]
        {
            "/PosterPrintRequest/css/design-system/tokens.css",
            "/PosterPrintRequest/css/design-system/foundation.css",
            "/PosterPrintRequest/js/poster-draft.js",
            "/PosterPrintRequest/_framework/blazor.web.js"
        })
        {
            var asset = await client.GetAsync(path);
            Assert.True(asset.IsSuccessStatusCode, path + " returned " + (int)asset.StatusCode);
        }

        var tokens = await client.GetStringAsync("/PosterPrintRequest/css/design-system/tokens.css");
        Assert.Contains("--epx-blueignix-bg", tokens, StringComparison.Ordinal);
        var blazor = await client.GetStringAsync("/PosterPrintRequest/_framework/blazor.web.js");
        Assert.Contains("blazor", blazor, StringComparison.OrdinalIgnoreCase);

        using var both = await PosterSignIn.SignInAsync(factory, "both");
        var technician = await both.GetAsync("/PosterPrintRequest/");
        technician.EnsureSuccessStatusCode();
        Assert.EndsWith("/PosterPrintRequest/technician", technician.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var technicianHtml = await technician.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/PosterPrintRequest/staff\"", technicianHtml, StringComparison.Ordinal);
        Assert.Contains(">Staff<", technicianHtml, StringComparison.Ordinal);

        var staff = await both.GetAsync("/PosterPrintRequest/staff");
        staff.EnsureSuccessStatusCode();
        Assert.Contains("Personal activity", await staff.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var technicianOnly = await PosterSignIn.SignInAsync(factory, "usero");
        var denied = await technicianOnly.GetAsync("/PosterPrintRequest/staff");
        Assert.Contains("/PosterPrintRequest/Account/AccessDenied", denied.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static void AssertStylesheets(string html)
    {
        var tokens = html.IndexOf("href=\"css/design-system/tokens.css\"", StringComparison.Ordinal);
        var foundation = html.IndexOf("href=\"css/design-system/foundation.css\"", StringComparison.Ordinal);
        Assert.True(tokens >= 0 && foundation > tokens);
        Assert.DoesNotContain("PosterPrintRequest.Web.styles.css", html, StringComparison.Ordinal);
        Assert.DoesNotContain(".razor.css", html, StringComparison.Ordinal);
    }

    private sealed class VirtualDirectoryStartup : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.UsePathBase("/PosterPrintRequest");
                next(app);
            };
    }
}
