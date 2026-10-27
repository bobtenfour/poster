using Microsoft.AspNetCore.Components;

namespace PosterPrintRequest.Web;

public static class ApplicationUris
{
    public static string BrowserPath(NavigationManager navigation, string? applicationPath) =>
        BrowserPath(navigation.BaseUri, applicationPath);

    public static string BrowserPath(string baseUri, string? applicationPath)
    {
        var root = new Uri(baseUri, UriKind.Absolute).AbsolutePath;
        if (!root.EndsWith('/'))
        {
            root += "/";
        }

        if (string.IsNullOrWhiteSpace(applicationPath) || applicationPath == "/")
        {
            return root;
        }

        return root + applicationPath.TrimStart('/');
    }

    public static string Relative(string? applicationPath)
    {
        if (string.IsNullOrWhiteSpace(applicationPath) || applicationPath == "/")
        {
            return "./";
        }

        return applicationPath.TrimStart('/');
    }
}
