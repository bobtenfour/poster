using Microsoft.Extensions.FileProviders;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

/// <summary>Owner UI change of 26 November 2026: stylesheet links carry a version taken from the file content.</summary>
public sealed class StaticAssetVersionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("asset-version-").FullName;

    [Fact]
    public void The_version_follows_the_file_content_and_a_missing_file_has_none()
    {
        Directory.CreateDirectory(Path.Combine(_root, "css"));
        var path = Path.Combine(_root, "css", "site.css");
        File.WriteAllText(path, "body { color: red; }");
        using var files = new PhysicalFileProvider(_root);

        var first = StaticAssetVersion.Compute(files, "css/site.css");
        var same = StaticAssetVersion.Compute(files, "css/site.css");
        File.WriteAllText(path, "body { color: blue; }");
        var changed = StaticAssetVersion.Compute(files, "css/site.css");

        Assert.Matches("^css/site\\.css\\?v=[0-9a-f]{12}$", first);
        Assert.Equal(first, same);
        Assert.NotEqual(first, changed);
        Assert.Equal("css/missing.css", StaticAssetVersion.Compute(files, "css/missing.css"));
    }

    [Fact]
    public void The_real_stylesheets_get_a_version()
    {
        using var files = new PhysicalFileProvider(Path.Combine(RepositoryPaths.Root(), "src", "PosterPrintRequest.Web", "wwwroot"));

        Assert.Matches("^css/design-system/foundation\\.css\\?v=[0-9a-f]{12}$", StaticAssetVersion.Href(files, "css/design-system/foundation.css"));
        Assert.Matches("^css/design-system/tokens\\.css\\?v=[0-9a-f]{12}$", StaticAssetVersion.Href(files, "css/design-system/tokens.css"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
