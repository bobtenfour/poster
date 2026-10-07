using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

namespace PosterPrintRequest.Web.Requests;

public sealed class PosterPreflight : IPosterPreflight
{
    public const decimal MaximumWidthInches = 36m;

    public const decimal MaximumLengthInches = 72m;

    private const string Reupload = "Correct the file and upload it again.";

    private const int MaximumPackageEntries = 512;

    private const int MaximumEntryBytes = 2 * 1024 * 1024;

    private readonly DraftFileStore _files;

    public PosterPreflight(DraftFileStore files)
    {
        _files = files;
    }

    public PosterPreflightResult Inspect(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        byte[] bytes;
        try
        {
            bytes = ReadLimited(content);
        }
        catch (IOException)
        {
            return Failure("This file could not be read.");
        }

        if (bytes.Length == 0)
        {
            return Failure("This file is not a PDF or PPTX poster.");
        }

        if (bytes.Length > UploadLimits.MaxBytes)
        {
            return Failure($"Choose a file up to {UploadLimits.MaxLabel}.");
        }

        if (LooksLikePdf(bytes))
        {
            return InspectPdf(bytes);
        }

        if (LooksLikeZip(bytes))
        {
            return InspectPackage(bytes);
        }

        return Failure("This file is not a PDF or PPTX poster.");
    }

    public async Task<PosterPreflightResult?> InspectStoredPosterAsync(string draftId, CancellationToken cancellationToken)
    {
        var path = _files.PosterPath(draftId);
        if (path is null)
        {
            return null;
        }

        try
        {
            var length = new FileInfo(path).Length;
            if (length == 0)
            {
                return Failure("This file is not a PDF or PPTX poster.");
            }

            if (length > UploadLimits.MaxBytes)
            {
                return Failure($"Choose a file up to {UploadLimits.MaxLabel}.");
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var bytes = new byte[length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken);
                if (read == 0)
                {
                    return Failure("This file could not be read.");
                }

                offset += read;
            }

            return Inspect(new MemoryStream(bytes, writable: false));
        }
        catch (IOException)
        {
            return Failure("This file could not be read.");
        }
    }

    private static PosterPreflightResult InspectPdf(byte[] bytes)
    {
        try
        {
            using var document = PdfDocument.Open(bytes);
            var count = document.NumberOfPages;
            if (count != 1)
            {
                return CountResult("PDF", "Pages", "page", "pages", "The PDF", count, null, null);
            }

            var page = document.GetPage(1);
            var box = page.MediaBox.Bounds;
            var across = InchesFromPoints(box.Width);
            var down = InchesFromPoints(box.Height);
            var rotation = ((page.Rotation.Value % 360) + 360) % 360;
            if (rotation is 90 or 270)
            {
                (across, down) = (down, across);
            }

            return CountResult("PDF", "Pages", "page", "pages", "The PDF", count, across, down);
        }
        catch (PdfDocumentEncryptedException)
        {
            return Failure("This PDF is protected and could not be read.");
        }
        catch (PdfDocumentFormatException)
        {
            return Failure("This PDF could not be read.");
        }
        catch (IOException)
        {
            return Failure("This PDF could not be read.");
        }
    }

    private static PosterPreflightResult InspectPackage(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count > MaximumPackageEntries)
            {
                return Failure("This PowerPoint file could not be read.");
            }

            var presentation = FindEntry(zip, "ppt/presentation.xml");
            if (presentation is null)
            {
                return Failure("This file is not a PDF or PPTX poster.");
            }

            var document = LoadXml(presentation);
            var size = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "sldSz");
            decimal? across = null;
            decimal? down = null;
            if (size is not null
                && long.TryParse(size.Attribute("cx")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cx)
                && long.TryParse(size.Attribute("cy")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cy)
                && cx > 0
                && cy > 0)
            {
                across = InchesFromEmus(cx);
                down = InchesFromEmus(cy);
            }

            var slideIds = document.Descendants().Where(element => element.Name.LocalName == "sldId").ToList();
            var relationships = LoadRelationships(zip);
            var slides = 0;
            foreach (var slideId in slideIds)
            {
                var relationshipId = slideId.Attributes()
                    .FirstOrDefault(attribute =>
                        attribute.Name.LocalName == "id" &&
                        attribute.Name.NamespaceName.Contains("relationships", StringComparison.Ordinal))
                    ?.Value;
                if (relationshipId is null
                    || !relationships.TryGetValue(relationshipId, out var target)
                    || FindEntry(zip, target) is null)
                {
                    return Failure("This PowerPoint file could not be read.");
                }

                slides++;
            }

            if (slides == 1 && (across is null || down is null))
            {
                return Failure("This PowerPoint file could not be read.");
            }

            return CountResult("PPTX", "Slides", "slide", "slides", "The PowerPoint file", slides, across, down);
        }
        catch (InvalidDataException)
        {
            return Failure("This file could not be read.");
        }
        catch (XmlException)
        {
            return Failure("This PowerPoint file could not be read.");
        }
        catch (IOException)
        {
            return Failure("This PowerPoint file could not be read.");
        }
    }

    private static PosterPreflightResult CountResult(
        string format,
        string countLabel,
        string singular,
        string plural,
        string subject,
        int count,
        decimal? across,
        decimal? down)
    {
        var checks = new List<PosterPreflightCheck>
        {
            new() { Label = "Format", Passed = true, Detail = format },
            new()
            {
                Label = countLabel,
                Passed = count == 1,
                Detail = count == 1
                    ? $"1 {singular}"
                    : $"{count} {plural}. A poster must contain exactly one {singular}."
            }
        };

        if (count != 1)
        {
            return new PosterPreflightResult
            {
                Passed = false,
                Summary = $"{subject} has {count} {plural}. A poster must contain exactly one {singular}. {Reupload}",
                DetectedFormat = format,
                PageCount = count,
                Checks = checks
            };
        }

        var (width, length) = Upright(across!.Value, down!.Value);
        var widthPassed = width <= MaximumWidthInches;
        var lengthPassed = length <= MaximumLengthInches;
        checks.Add(new PosterPreflightCheck
        {
            Label = "Width",
            Passed = widthPassed,
            Detail = $"{FormatInches(width)} wide. The maximum is {FormatInches(MaximumWidthInches)}."
        });
        checks.Add(new PosterPreflightCheck
        {
            Label = "Length",
            Passed = lengthPassed,
            Detail = $"{FormatInches(length)} long. The maximum is {FormatInches(MaximumLengthInches)}."
        });

        var reasons = new List<string>();
        if (!widthPassed)
        {
            reasons.Add($"The poster is {FormatInches(width)} wide. The maximum width is {FormatInches(MaximumWidthInches)}.");
        }

        if (!lengthPassed)
        {
            reasons.Add($"The poster is {FormatInches(length)} long. The maximum length is {FormatInches(MaximumLengthInches)}.");
        }

        return new PosterPreflightResult
        {
            Passed = reasons.Count == 0,
            Summary = reasons.Count == 0
                ? $"{format}, 1 {singular}, {FormatInches(width)} wide and {FormatInches(length)} long."
                : string.Join(' ', reasons) + " " + Reupload,
            DetectedFormat = format,
            PageCount = count,
            WidthInches = width,
            LengthInches = length,
            Checks = checks
        };
    }

    private static (decimal Width, decimal Length) Upright(decimal across, decimal down) =>
        across > down ? (down, across) : (across, down);

    private static PosterPreflightResult Failure(string reason) =>
        new()
        {
            Passed = false,
            Summary = reason + " " + Reupload,
            Checks =
            [
                new PosterPreflightCheck
                {
                    Label = "File",
                    Passed = false,
                    Detail = reason
                }
            ]
        };

    private static decimal InchesFromPoints(double points) =>
        decimal.Round((decimal)points / 72m, 2, MidpointRounding.AwayFromZero);

    private static decimal InchesFromEmus(long emus) =>
        decimal.Round(emus / 914400m, 2, MidpointRounding.AwayFromZero);

    private static string FormatInches(decimal inches)
    {
        var text = inches.ToString("0.##", CultureInfo.InvariantCulture);
        return text + " inches";
    }

    private static bool LooksLikePdf(byte[] bytes)
    {
        var window = Math.Min(bytes.Length, 1024);
        for (var index = 0; index <= window - 5; index++)
        {
            if (bytes[index] == (byte)'%'
                && bytes[index + 1] == (byte)'P'
                && bytes[index + 2] == (byte)'D'
                && bytes[index + 3] == (byte)'F'
                && bytes[index + 4] == (byte)'-')
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeZip(byte[] bytes) =>
        bytes.Length >= 4
        && bytes[0] == 0x50
        && bytes[1] == 0x4B
        && bytes[2] is 0x03 or 0x05 or 0x07;

    private static Dictionary<string, string> LoadRelationships(ZipArchive zip)
    {
        var entry = FindEntry(zip, "ppt/_rels/presentation.xml.rels");
        if (entry is null)
        {
            return [];
        }

        var document = LoadXml(entry);
        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relationship in document.Descendants().Where(element => element.Name.LocalName == "Relationship"))
        {
            var id = relationship.Attribute("Id")?.Value;
            var type = relationship.Attribute("Type")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (id is null || target is null || type is null || !type.EndsWith("/slide", StringComparison.Ordinal))
            {
                continue;
            }

            var resolved = ResolveTarget(target);
            if (resolved.Length > 0)
            {
                relationships[id] = resolved;
            }
        }

        return relationships;
    }

    private static string ResolveTarget(string target)
    {
        var path = target.Replace('\\', '/');
        if (!path.StartsWith('/'))
        {
            path = "ppt/" + path;
        }

        var stack = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count == 0)
                {
                    return "";
                }

                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            stack.Add(segment);
        }

        return string.Join('/', stack);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var raw = entry.Open();
        using var limited = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        int read;
        while ((read = raw.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaximumEntryBytes)
            {
                throw new XmlException("The package entry is too large.");
            }

            limited.Write(buffer, 0, read);
        }

        limited.Position = 0;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false
        };
        using var reader = XmlReader.Create(limited, settings);
        return XDocument.Load(reader);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string path) =>
        zip.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase));

    private static byte[] ReadLimited(Stream content)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        var total = 0;
        int read;
        while ((read = content.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > UploadLimits.MaxBytes)
            {
                return new byte[UploadLimits.MaxBytes + 1];
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
