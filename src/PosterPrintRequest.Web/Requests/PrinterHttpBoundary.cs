using System.Net;

namespace PosterPrintRequest.Web.Requests;

/// <summary>
/// The HTTP boundary between the application and the configured printer (Printer Monitoring Contract v1):
/// one request, no redirects, at most 1 MiB of body, one total time limit.
/// </summary>
public static class PrinterHttpBoundary
{
    /// <summary>Maximum response body, contract section 5.1.</summary>
    public const int MaxResponseBytes = 1_048_576;

    public const string TooLargeMessage = "The printer response is larger than 1 MiB.";

    /// <summary>Any 3xx status ends the consultation (contract section 4.2).</summary>
    public static bool IsRedirect(HttpStatusCode status) => (int)status is >= 300 and <= 399;

    public static string RedirectRefused(HttpStatusCode status) =>
        "Redirect refused (HTTP " + ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture) + ").";

    public static string TimedOut(TimeSpan limit) =>
        "The printer did not answer within " + Math.Round(limit.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) + " seconds.";

    /// <summary>True when the response declares a body above the limit (contract section 5.2).</summary>
    public static bool DeclaresTooMuch(HttpResponseMessage response) =>
        response.Content.Headers.ContentLength is > MaxResponseBytes;

    /// <summary>
    /// Reads the body as a stream and stops once more than the limit has arrived (contract section 5.3).
    /// Returns null when the body is larger than the limit.
    /// </summary>
    public static async Task<byte[]?> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }
    }
}
