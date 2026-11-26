using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace PosterPrintRequest.Web.Requests;

public sealed record PrinterPrinthead(string Name, string Status, string? Health)
{
    /// <summary>True when the printer reports a status other than OK for this printhead (section 12.4).</summary>
    public bool NeedsAttention { get; init; }
}

public sealed record PrinterStatus(IReadOnlyList<PrinterCartridge> Cartridges, IReadOnlyList<PrinterPrinthead> Printheads);

/// <summary>
/// Reads the EWS document open_status.xml of the configured printer (Printer Monitoring Contract v1, amendment of
/// 25 November 2026). Six cartridges with level = Ink_Consumed / Capacity (Ink_Consumed is the ink that remains),
/// and the printheads with their status and health. Element names are matched by local name, so a namespace or
/// version attribute on the root does not matter. Any other shape returns null.
/// </summary>
public static class PrinterStatusDocument
{
    public const string RelativePath = "/hp/device/webAccess/open_status.xml";

    // Display order and names of the dashboard; each entry lists the identifiers the printer may use.
    private static readonly (string Name, string[] Keys)[] Colors =
    [
        ("Gray", ["G", "GRAY", "GREY"]),
        ("Photo black", ["PK", "PHOTOBLACK"]),
        ("Matte black", ["MK", "MATTEBLACK"]),
        ("Yellow", ["Y", "YELLOW"]),
        ("Magenta", ["M", "MAGENTA"]),
        ("Cyan", ["C", "CYAN"])
    ];

    public static PrinterStatus? Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        var cartridges = ReadCartridges(document);
        if (cartridges is null)
        {
            return null;
        }

        return new PrinterStatus(cartridges, ReadPrintheads(document));
    }

    private static IReadOnlyList<PrinterCartridge>? ReadCartridges(XDocument document)
    {
        var found = new Dictionary<string, PrinterCartridge>(StringComparer.Ordinal);
        foreach (var cartridge in Elements(document, "Cartridge").Where(element => element.Parent?.Name.LocalName == "Cartridges"))
        {
            var name = ColorName(cartridge);
            if (name is null
                || !TryNumber(Child(cartridge, "Capacity"), out var capacity)
                || capacity <= 0
                || !TryNumber(Child(cartridge, "Ink_Consumed"), out var remaining)
                || remaining < 0
                || !found.TryAdd(name, Cartridge(cartridge, name, remaining, capacity)))
            {
                return null;
            }
        }

        if (found.Count != Colors.Length)
        {
            return null;
        }

        return Colors.Select(color => found[color.Name]).ToArray();
    }

    private static PrinterCartridge Cartridge(XElement cartridge, string name, decimal remaining, decimal capacity)
    {
        var (status, attention) = ReadStatus(cartridge);
        return new PrinterCartridge(name, (int)Math.Round(Math.Min(remaining, capacity) * 100m / capacity, MidpointRounding.AwayFromZero))
        {
            Status = status,
            NeedsAttention = attention
        };
    }

    /// <summary>
    /// Section 12.2: the Status element of a cartridge or printhead. Attention when its Id is present and not 0, or,
    /// without Id, when its Description is present and not OK. A missing Status element means no attention.
    /// </summary>
    private static (string? Description, bool Attention) ReadStatus(XElement parent)
    {
        var status = parent.Elements().FirstOrDefault(element => element.Name.LocalName == "Status");
        if (status is null)
        {
            return (null, false);
        }

        var description = Child(status, "Description") ?? (status.HasElements ? null : Trimmed(status.Value));
        var id = Child(status, "Id");
        if (id is null && !status.HasElements && int.TryParse(description, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            // A bare number such as <Status>0</Status> is a status Id without a description.
            id = description;
            description = null;
        }

        var attention = id is not null
            ? id != "0"
            : description is not null && !string.Equals(description, "OK", StringComparison.OrdinalIgnoreCase);
        return (description, attention);
    }

    private static string? Trimmed(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<PrinterPrinthead> ReadPrintheads(XDocument document) =>
        Elements(document, "Printhead")
            .Where(element => element.Parent?.Name.LocalName == "Printheads")
            .Select(printhead =>
            {
                var name = Attribute(printhead, "Color") ?? Attribute(printhead, "Id") ?? "Printhead";
                var (description, attention) = ReadStatus(printhead);
                return new PrinterPrinthead(name, description ?? "Unknown", Child(printhead, "Health")) { NeedsAttention = attention };
            })
            .ToArray();

    private static string? ColorName(XElement cartridge)
    {
        foreach (var candidate in new[] { Attribute(cartridge, "Color"), Attribute(cartridge, "Id"), Child(cartridge, "Color") })
        {
            if (candidate is null)
            {
                continue;
            }

            var key = new string(candidate.Where(char.IsLetter).ToArray()).ToUpperInvariant();
            foreach (var (name, keys) in Colors)
            {
                if (keys.Contains(key, StringComparer.Ordinal))
                {
                    return name;
                }
            }
        }

        return null;
    }

    private static IEnumerable<XElement> Elements(XDocument document, string localName) =>
        document.Descendants().Where(element => element.Name.LocalName == localName);

    private static string? Child(XElement parent, string localName)
    {
        var value = parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? Attribute(XElement element, string localName)
    {
        var value = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool TryNumber(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
