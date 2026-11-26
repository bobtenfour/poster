using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;

namespace PosterPrintRequest.Web.Requests;

/// <summary>
/// Owner UI change of 26 November 2026: stylesheet links carry a version taken from the file content, so a new
/// release makes browsers download the new styles instead of reusing a cached copy. The value is computed once per
/// path while the application runs; a missing file gets no version.
/// </summary>
public static class StaticAssetVersion
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static string Href(IFileProvider files, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(files);
        return Cache.GetOrAdd(relativePath, path => Compute(files, path));
    }

    public static string Compute(IFileProvider files, string relativePath)
    {
        var file = files.GetFileInfo(relativePath);
        if (!file.Exists || file.IsDirectory)
        {
            return relativePath;
        }

        using var stream = file.CreateReadStream();
        var hash = SHA256.HashData(stream);
        return relativePath + "?v=" + Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }
}
