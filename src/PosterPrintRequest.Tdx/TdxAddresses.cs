namespace PosterPrintRequest.Tdx;

public sealed class TdxAddressException : Exception
{
    public TdxAddressException(string message) : base(message)
    {
    }
}

public class TdxRequestRefusedException : Exception
{
    public TdxRequestRefusedException(string message) : base(message)
    {
    }

    public TdxRequestRefusedException(string message, Exception inner) : base(message, inner)
    {
    }
}

public static class TdxAddresses
{
    public static Uri ValidateBaseAddress(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (!baseAddress.IsAbsoluteUri)
            throw new TdxAddressException("The base address must be absolute.");
        if (!string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new TdxAddressException("The scheme must be https.");
        if (!string.IsNullOrEmpty(baseAddress.UserInfo))
            throw new TdxAddressException("The base address must not contain user information.");
        if (baseAddress.Port != 443)
            throw new TdxAddressException("The port must be 443.");
        if (!string.IsNullOrEmpty(baseAddress.Query))
            throw new TdxAddressException("The base path must not contain a query.");
        if (!string.IsNullOrEmpty(baseAddress.Fragment))
            throw new TdxAddressException("The base path must not contain a fragment.");

        RejectRawPath(baseAddress.OriginalString, forBase: true);
        RejectDotAndEncodedSeparators(baseAddress.AbsolutePath);
        if (string.IsNullOrEmpty(baseAddress.AbsolutePath) || baseAddress.AbsolutePath[0] != '/')
            throw new TdxAddressException("The base path must be absolute.");

        return baseAddress;
    }

    public static void EnsureRequestAllowed(Uri baseAddress, Uri requestUri)
    {
        var validatedBase = ValidateBaseAddress(baseAddress);
        ArgumentNullException.ThrowIfNull(requestUri);
        if (!requestUri.IsAbsoluteUri)
            throw new TdxRequestRefusedException("The request URI must be absolute.");
        if (!string.Equals(requestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new TdxRequestRefusedException("The request scheme is not https.");
        if (!string.IsNullOrEmpty(requestUri.UserInfo))
            throw new TdxRequestRefusedException("The request URI contains user information.");
        if (requestUri.Port != 443)
            throw new TdxRequestRefusedException("The request port is not 443.");
        if (!string.Equals(requestUri.IdnHost, validatedBase.IdnHost, StringComparison.OrdinalIgnoreCase))
            throw new TdxRequestRefusedException("The request host does not match the supplied base host.");
        if (!string.IsNullOrEmpty(requestUri.Fragment))
            throw new TdxRequestRefusedException("The request URI contains a fragment.");

        RejectRawPath(requestUri.OriginalString, forBase: false);
        var requestPath = NormalizePath(requestUri.AbsolutePath);
        var basePath = NormalizePath(validatedBase.AbsolutePath);
        if (!PathIsInsideBase(basePath, requestPath))
            throw new TdxRequestRefusedException("The request path is outside the supplied base path.");
    }

    public static Uri Combine(Uri baseAddress, string relativePath, string? query = null)
    {
        var validated = ValidateBaseAddress(baseAddress);
        var basePath = validated.AbsolutePath.TrimEnd('/');
        var relative = relativePath.TrimStart('/');
        var builder = new UriBuilder(validated)
        {
            Path = basePath + "/" + relative,
            Query = query ?? string.Empty,
            Fragment = string.Empty,
            Port = 443
        };
        var uri = builder.Uri;
        EnsureRequestAllowed(validated, uri);
        return uri;
    }

    private static void RejectRawPath(string original, bool forBase)
    {
        var path = original;
        var scheme = original.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var slash = original.IndexOf('/', scheme + 3);
            path = slash >= 0 ? original[slash..] : "/";
        }

        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
            path = path[..cut];

        if (path.Contains("%2f", StringComparison.OrdinalIgnoreCase)
            || path.Contains("%5c", StringComparison.OrdinalIgnoreCase))
            Fail(forBase, "an encoded separator");

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0)
                continue;
            if (segment is "." or "..")
                Fail(forBase, "a dot segment");

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException)
            {
                Fail(forBase, "invalid percent-encoding");
                return;
            }

            if (decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\'))
                Fail(forBase, "an encoded dot segment or separator");
        }
    }

    private static void Fail(bool forBase, string reason)
    {
        var message = "The path contains " + reason + ".";
        if (forBase)
            throw new TdxAddressException(message);
        throw new TdxRequestRefusedException(message);
    }

    private static void RejectDotAndEncodedSeparators(string path)
    {
        if (ContainsEncodedSeparator(path))
            throw new TdxAddressException("The base path contains an encoded separator.");

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0)
                continue;
            if (segment is "." or "..")
                throw new TdxAddressException("The base path contains a dot segment.");
            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException)
            {
                throw new TdxAddressException("The base path contains invalid percent-encoding.");
            }

            if (decoded is "." or "..")
                throw new TdxAddressException("The base path contains an encoded dot segment.");
            if (decoded.Contains('/') || decoded.Contains('\\'))
                throw new TdxAddressException("The base path contains an encoded separator.");
        }
    }

    private static string NormalizePath(string path)
    {
        if (ContainsEncodedSeparator(path))
            throw new TdxRequestRefusedException("The request path contains an encoded separator.");

        var stack = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;

            var decoded = DecodeSegment(segment);
            if (decoded.Contains('/') || decoded.Contains('\\'))
                throw new TdxRequestRefusedException("The request path contains an encoded separator.");
            if (decoded == ".")
                continue;
            if (decoded == ".." || segment == "..")
            {
                if (stack.Count == 0)
                    throw new TdxRequestRefusedException("The request path escapes the base path.");
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            stack.Add(decoded);
        }

        return "/" + string.Join('/', stack);
    }

    private static bool PathIsInsideBase(string basePath, string requestPath)
    {
        var root = TrimTrailingSlash(basePath);
        var candidate = TrimTrailingSlash(requestPath);
        if (root.Length == 0)
            root = "/";
        if (candidate.Length == 0)
            candidate = "/";

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSlash(string path) =>
        path.Length > 1 ? path.TrimEnd('/') : path;

    private static bool ContainsEncodedSeparator(string path) =>
        path.Contains("%2f", StringComparison.OrdinalIgnoreCase)
        || path.Contains("%5c", StringComparison.OrdinalIgnoreCase);

    private static string DecodeSegment(string segment)
    {
        try
        {
            return Uri.UnescapeDataString(segment);
        }
        catch (UriFormatException ex)
        {
            throw new TdxRequestRefusedException("The path contains invalid percent-encoding.", ex);
        }
    }
}
