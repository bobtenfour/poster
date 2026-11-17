namespace PosterPrintRequest.TdxValidator;

public static class BasePathRules
{
    public static string? Validate(string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return "The base path is missing.";
        // A real path never contains < or >; their presence means the placeholder was not replaced.
        if (basePath.IndexOfAny(['<', '>']) >= 0)
            return "The sandbox base path placeholder was not replaced. Replace everything between the quotes in SandboxHost.BasePath, including the < and > signs.";
        if (basePath.Contains('?') || basePath.Contains('#'))
            return "The base path contains a query or a fragment.";
        if (basePath[0] != '/')
            return "The base path is not absolute.";
        if (basePath.Contains("%2f", StringComparison.OrdinalIgnoreCase)
            || basePath.Contains("%5c", StringComparison.OrdinalIgnoreCase))
            return "The base path contains an encoded separator.";

        foreach (var segment in basePath.Split('/'))
        {
            if (segment.Length == 0)
                continue;
            if (segment is "." or "..")
                return "The base path contains a dot segment.";

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException)
            {
                return "The base path contains invalid percent-encoding.";
            }

            if (decoded is "." or "..")
                return "The base path contains an encoded dot segment.";
            if (decoded.Contains('/') || decoded.Contains('\\'))
                return "The base path contains an encoded separator.";
        }

        return null;
    }
}
