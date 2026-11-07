using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PosterPrintRequest.Web.Requests;

public sealed record PrinterCartridge(string Name, int Percent);

public sealed class PrinterReading
{
    private PrinterReading(bool available, IReadOnlyList<PrinterCartridge> cartridges)
    {
        Available = available;
        Cartridges = cartridges;
    }

    public bool Available { get; }

    public IReadOnlyList<PrinterCartridge> Cartridges { get; }

    public static PrinterReading Unavailable { get; } = new(false, []);

    public static PrinterReading Create(IReadOnlyList<PrinterCartridge> cartridges)
    {
        ArgumentNullException.ThrowIfNull(cartridges);
        if (cartridges.Count != 6)
        {
            throw new ArgumentOutOfRangeException(nameof(cartridges));
        }

        var copy = new PrinterCartridge[6];
        for (var index = 0; index < 6; index++)
        {
            copy[index] = cartridges[index];
        }

        return new PrinterReading(true, copy);
    }
}

public sealed class PrinterSupplies : IDisposable
{
    public const string BaseUrlKey = "PrinterMonitoring:BaseUrl";

    public const string SuppliesPath = "/hp/device/webAccess/index.htm?content=supplies";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private const string CaptionName = "Cartridges";

    private const string CodeHeader = "Code";

    private const string NameHeader = "Ink cartridge";

    private const string StatusHeader = "Status";

    private const string LevelHeader = "Ink level";

    private const string CapacityHeader = "Capacity";

    private const string WarrantyHeader = "Warranty status";

    private const string OrderHeader = "HP Order Information";

    private static readonly (string Code, string Name)[] SupportedCartridges =
    [
        ("G", "Gray"),
        ("PK", "Photo black"),
        ("MK", "Matte black"),
        ("Y", "Yellow"),
        ("M", "Magenta"),
        ("C", "Cyan")
    ];

    private readonly IConfiguration _configuration;

    private readonly HttpClient _http;

    public PrinterSupplies(IConfiguration configuration, HttpClient http)
    {
        _configuration = configuration;
        _http = http;
    }

    public static string? Target(IConfiguration configuration) =>
        Target(configuration[BaseUrlKey]);

    public static string? Target(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return baseUrl.TrimEnd('/') + SuppliesPath;
    }

    public static HttpClient CreateClient() =>
        CreateClient(new SocketsHttpHandler());

    public static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler, disposeHandler: true)
        {
            Timeout = Timeout
        };

    public async Task<PrinterReading> ReadAsync(CancellationToken cancellationToken)
    {
        var html = await ReadBodyAsync(cancellationToken);
        if (html is null)
        {
            return PrinterReading.Unavailable;
        }

        var cartridges = Parse(html);
        return cartridges is null ? PrinterReading.Unavailable : PrinterReading.Create(cartridges);
    }

    private async Task<string?> ReadBodyAsync(CancellationToken cancellationToken)
    {
        var target = Target(_configuration);
        if (target is null)
        {
            return null;
        }

        try
        {
            using var response = await _http.GetAsync(target, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static IReadOnlyList<PrinterCartridge>? Parse(string? html)
    {
        if (string.IsNullOrEmpty(html)
            || !TryExtractCartridgesTable(html, out var table)
            || !TryReadRows(table, out var rows))
        {
            return null;
        }

        var headerIndex = 0;
        if (rows.Count > 0 && rows[0].Count == 1 && rows[0][0] == CaptionName)
        {
            headerIndex = 1;
        }

        if (rows.Count - headerIndex != SupportedCartridges.Length + 1
            || !TryMapColumns(rows[headerIndex], out var codeColumn, out var nameColumn, out var statusColumn, out var levelColumn))
        {
            return null;
        }

        var found = new Dictionary<string, int>(SupportedCartridges.Length, StringComparer.Ordinal);
        for (var index = headerIndex + 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Count != rows[headerIndex].Count
                || row[statusColumn].Length == 0
                || !TryReadPercent(row[levelColumn], out var percent)
                || !TryExpectedName(row[codeColumn], out var name)
                || !NameAgrees(row[nameColumn], name)
                || !found.TryAdd(row[codeColumn], percent))
            {
                return null;
            }
        }

        if (found.Count != SupportedCartridges.Length)
        {
            return null;
        }

        var cartridges = new PrinterCartridge[SupportedCartridges.Length];
        for (var index = 0; index < SupportedCartridges.Length; index++)
        {
            var (code, name) = SupportedCartridges[index];
            if (!found.TryGetValue(code, out var percent))
            {
                return null;
            }

            cartridges[index] = new PrinterCartridge(name, percent);
        }

        return cartridges;
    }

    public void Dispose() => _http.Dispose();

    private static bool TryExpectedName(string code, out string name)
    {
        foreach (var cartridge in SupportedCartridges)
        {
            if (cartridge.Code == code)
            {
                name = cartridge.Name;
                return true;
            }
        }

        name = "";
        return false;
    }

    private static bool NameAgrees(string actual, string expected)
    {
        string? matched = null;
        foreach (var cartridge in SupportedCartridges)
        {
            if (!IsCartridgeName(actual, cartridge.Name))
            {
                continue;
            }

            if (matched is null || cartridge.Name.Length > matched.Length)
            {
                matched = cartridge.Name;
            }
        }

        return matched == expected;
    }

    private static bool IsCartridgeName(string actual, string expected)
    {
        if (actual.Equals(expected, StringComparison.Ordinal))
        {
            return true;
        }

        return actual.Length > expected.Length
            && actual[actual.Length - expected.Length - 1] == ' '
            && actual.EndsWith(expected, StringComparison.Ordinal);
    }

    private static bool TryReadPercent(string text, out int percent)
    {
        percent = 0;
        if (text.Length is < 2 or > 4 || text[^1] != '%')
        {
            return false;
        }

        var digits = text[..^1];
        if (digits.Length > 1 && digits[0] == '0')
        {
            return false;
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out percent)
            && percent is >= 0 and <= 100;
    }

    private static bool TryMapColumns(
        IReadOnlyList<string> header,
        out int codeColumn,
        out int nameColumn,
        out int statusColumn,
        out int levelColumn)
    {
        codeColumn = -1;
        nameColumn = -1;
        statusColumn = -1;
        levelColumn = -1;
        var blankColumn = -1;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < header.Count; index++)
        {
            var cell = header[index];
            if (cell.Length == 0)
            {
                if (blankColumn >= 0)
                {
                    return false;
                }

                blankColumn = index;
                continue;
            }

            if (!seen.Add(cell))
            {
                return false;
            }

            switch (cell)
            {
                case CodeHeader:
                    codeColumn = index;
                    break;
                case NameHeader:
                    nameColumn = index;
                    break;
                case StatusHeader:
                    statusColumn = index;
                    break;
            }

            if (cell == LevelHeader || cell == LevelHeader + "*")
            {
                if (levelColumn >= 0)
                {
                    return false;
                }

                levelColumn = index;
            }
        }

        if (codeColumn < 0)
        {
            codeColumn = blankColumn;
        }
        else if (blankColumn >= 0)
        {
            return false;
        }

        return codeColumn >= 0
            && nameColumn >= 0
            && statusColumn >= 0
            && levelColumn >= 0
            && seen.Contains(CapacityHeader)
            && seen.Contains(WarrantyHeader)
            && seen.Contains(OrderHeader);
    }

    private static bool TryExtractCartridgesTable(string html, out string table)
    {
        table = "";
        var starts = new Stack<int>();
        int? owner = null;
        var captions = 0;
        var index = 0;
        while (index >= 0 && index < html.Length)
        {
            var next = html.IndexOf('<', index);
            if (next < 0)
            {
                break;
            }

            index = next;
            if (TryConsumeNoise(html, ref index))
            {
                continue;
            }

            if (TryMatch(html, next, "table", true, out var afterOpen))
            {
                starts.Push(next);
                index = afterOpen;
                continue;
            }

            if (TryMatch(html, next, "table", false, out var afterClose))
            {
                if (starts.Count == 0)
                {
                    return false;
                }

                var start = starts.Pop();
                if (owner == start)
                {
                    table = html[start..afterClose];
                }

                index = afterClose;
                continue;
            }

            if (TryMatch(html, next, "caption", true, out var captionStart))
            {
                if (!TryReadElement(html, captionStart, "caption", out index, out var inner))
                {
                    return false;
                }

                if (Visible(inner) == CaptionName)
                {
                    if (starts.Count == 0)
                    {
                        return false;
                    }

                    captions++;
                    owner = starts.Peek();
                }

                continue;
            }

            index = next + 1;
        }

        return index >= 0 && captions == 1 && starts.Count == 0 && table.Length > 0;
    }

    private static bool TryReadRows(string tableHtml, out List<List<string>> rows)
    {
        rows = [];
        if (!TryMatch(tableHtml, 0, "table", true, out var index))
        {
            return false;
        }

        var depth = 1;
        while (index >= 0 && index < tableHtml.Length && depth > 0)
        {
            var next = tableHtml.IndexOf('<', index);
            if (next < 0)
            {
                return false;
            }

            index = next;
            if (TryConsumeNoise(tableHtml, ref index))
            {
                continue;
            }

            if (TryMatch(tableHtml, next, "table", true, out _))
            {
                return false;
            }

            if (TryMatch(tableHtml, next, "table", false, out var afterClose))
            {
                depth--;
                index = afterClose;
                continue;
            }

            if (depth == 1 && TryMatch(tableHtml, next, "tr", true, out var rowStart))
            {
                if (!TryReadRow(tableHtml, rowStart, out index, out var cells) || cells.Count == 0)
                {
                    return false;
                }

                rows.Add(cells);
                continue;
            }

            index = next + 1;
        }

        return index >= 0 && depth == 0 && rows.Count > 0;
    }

    private static bool TryReadRow(string html, int start, out int after, out List<string> cells)
    {
        cells = [];
        var index = start;
        while (index >= 0 && index < html.Length)
        {
            var next = html.IndexOf('<', index);
            if (next < 0)
            {
                after = 0;
                return false;
            }

            index = next;
            if (TryConsumeNoise(html, ref index))
            {
                continue;
            }

            if (TryMatch(html, next, "tr", false, out after))
            {
                return true;
            }

            if (TryOpenCell(html, next, out var cellName, out var contentStart))
            {
                if (!TryReadCell(html, contentStart, cellName, out index, out var inner))
                {
                    after = 0;
                    return false;
                }

                cells.Add(inner);
                continue;
            }

            index = next + 1;
        }

        after = 0;
        return false;
    }

    private static bool TryOpenCell(string html, int index, out string name, out int after)
    {
        if (TryMatch(html, index, "td", true, out after))
        {
            name = "td";
            return true;
        }

        if (TryMatch(html, index, "th", true, out after))
        {
            name = "th";
            return true;
        }

        name = "";
        after = index;
        return false;
    }

    private static bool TryReadCell(string html, int contentStart, string name, out int after, out string text)
    {
        text = "";
        if (!TryReadCellMarkup(html, contentStart, name, out after, out var inner))
        {
            return false;
        }

        text = Visible(inner);
        if (!TryReadAltPercent(inner, out var altPercent, out var altFound))
        {
            return false;
        }

        if (!altFound)
        {
            return true;
        }

        var altText = altPercent.ToString(CultureInfo.InvariantCulture) + "%";
        if (text.Length == 0)
        {
            text = altText;
            return true;
        }

        return !TryReadPercent(text, out var visiblePercent) || visiblePercent == altPercent;
    }

    private static bool TryReadCellMarkup(string html, int contentStart, string name, out int after, out string inner)
    {
        var index = contentStart;
        while (index < html.Length)
        {
            var next = html.IndexOf('<', index);
            if (next < 0)
            {
                after = 0;
                inner = "";
                return false;
            }

            index = next;
            if (TryConsumeNoise(html, ref index))
            {
                if (index < 0)
                {
                    after = 0;
                    inner = "";
                    return false;
                }

                continue;
            }

            if (TryMatch(html, next, "table", true, out var nestedStart))
            {
                if (!TrySkipBalanced(html, nestedStart, "table", out index))
                {
                    after = 0;
                    inner = "";
                    return false;
                }

                continue;
            }

            if (TryMatch(html, next, name, false, out after))
            {
                inner = html[contentStart..next];
                return true;
            }

            if (TryMatch(html, next, "tr", false, out _) || TryOpenCell(html, next, out _, out _))
            {
                after = next;
                inner = html[contentStart..next];
                return true;
            }

            index = next + 1;
        }

        after = 0;
        inner = "";
        return false;
    }

    private static bool TrySkipBalanced(string html, int contentStart, string name, out int after)
    {
        after = 0;
        var depth = 1;
        var index = contentStart;
        while (index < html.Length && depth > 0)
        {
            var next = html.IndexOf('<', index);
            if (next < 0)
            {
                after = 0;
                return false;
            }

            index = next;
            if (TryConsumeNoise(html, ref index))
            {
                if (index < 0)
                {
                    after = 0;
                    return false;
                }

                continue;
            }

            if (TryMatch(html, next, name, true, out var openEnd))
            {
                depth++;
                index = openEnd;
                continue;
            }

            if (TryMatch(html, next, name, false, out after))
            {
                depth--;
                index = after;
                continue;
            }

            index = next + 1;
        }

        return depth == 0;
    }

    private static bool TryReadAltPercent(string inner, out int percent, out bool found)
    {
        percent = 0;
        found = false;
        var index = 0;
        while (index < inner.Length)
        {
            var next = inner.IndexOf('<', index);
            if (next < 0)
            {
                return true;
            }

            if (!TryMatch(inner, next, "img", true, out var after))
            {
                index = next + 1;
                continue;
            }

            var tag = inner[next..after];
            if (TryAttribute(tag, "alt", out var alt) && TryReadPercent(alt, out var altPercent))
            {
                if (found && percent != altPercent)
                {
                    return false;
                }

                percent = altPercent;
                found = true;
            }

            index = after;
        }

        return true;
    }

    private static bool TryAttribute(string tag, string name, out string value)
    {
        value = "";
        var cursor = 0;
        while (cursor < tag.Length)
        {
            var start = tag.IndexOf(name, cursor, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return false;
            }

            var before = start == 0 ? ' ' : tag[start - 1];
            var afterName = start + name.Length;
            if (!char.IsWhiteSpace(before) && before is not ('<' or '/'))
            {
                cursor = start + 1;
                continue;
            }

            while (afterName < tag.Length && char.IsWhiteSpace(tag[afterName]))
            {
                afterName++;
            }

            if (afterName >= tag.Length || tag[afterName] != '=')
            {
                cursor = start + 1;
                continue;
            }

            afterName++;
            while (afterName < tag.Length && char.IsWhiteSpace(tag[afterName]))
            {
                afterName++;
            }

            if (afterName >= tag.Length)
            {
                return false;
            }

            if (tag[afterName] is '"' or '\'')
            {
                var quote = tag[afterName];
                var end = tag.IndexOf(quote, afterName + 1);
                if (end < 0)
                {
                    return false;
                }

                value = tag[(afterName + 1)..end];
                return true;
            }

            var unquoted = afterName;
            while (unquoted < tag.Length && !char.IsWhiteSpace(tag[unquoted]) && tag[unquoted] is not ('>' or '/'))
            {
                unquoted++;
            }

            value = tag[afterName..unquoted];
            return true;
        }

        return false;
    }

    private static bool TryReadElement(string html, int contentStart, string name, out int after, out string inner)
    {
        var index = contentStart;
        while (index < html.Length)
        {
            var next = html.IndexOf('<', index);
            if (next < 0)
            {
                after = 0;
                inner = "";
                return false;
            }

            if (TryMatch(html, next, name, false, out after))
            {
                inner = html[contentStart..next];
                return true;
            }

            if (TryMatch(html, next, name, true, out _) || TryMatch(html, next, "table", true, out _))
            {
                after = 0;
                inner = "";
                return false;
            }

            index = next + 1;
        }

        after = 0;
        inner = "";
        return false;
    }

    private static bool TryConsumeNoise(string html, ref int index)
    {
        if ((uint)index >= (uint)html.Length || html[index] != '<')
        {
            return false;
        }

        if (html.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
        {
            var end = html.IndexOf("-->", index + 4, StringComparison.Ordinal);
            index = end < 0 ? -1 : end + 3;
            return true;
        }

        if (TryMatch(html, index, "script", true, out var openEnd))
        {
            index = TryClose(html, openEnd, "script", out var after) ? after : -1;
            return true;
        }

        if (TryMatch(html, index, "style", true, out var styleEnd))
        {
            index = TryClose(html, styleEnd, "style", out var after) ? after : -1;
            return true;
        }

        return false;
    }

    private static bool TryClose(string html, int from, string name, out int after)
    {
        var index = from;
        while (index < html.Length)
        {
            var next = html.IndexOf('<', index);
            if (next < 0)
            {
                after = 0;
                return false;
            }

            if (TryMatch(html, next, name, false, out after))
            {
                return true;
            }

            index = next + 1;
        }

        after = 0;
        return false;
    }

    private static bool TryMatch(string html, int index, string name, bool open, out int after)
    {
        after = index;
        if ((uint)index >= (uint)html.Length || html[index] != '<')
        {
            return false;
        }

        var cursor = index + 1;
        if (!open)
        {
            if ((uint)cursor >= (uint)html.Length || html[cursor] != '/')
            {
                return false;
            }

            cursor++;
        }
        else if ((uint)cursor < (uint)html.Length && html[cursor] == '/')
        {
            return false;
        }

        if (cursor + name.Length > html.Length
            || !html.AsSpan(cursor, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        cursor += name.Length;
        if ((uint)cursor >= (uint)html.Length)
        {
            return false;
        }

        var boundary = html[cursor];
        if (boundary is not ('>' or '/' or ' ' or '\t' or '\r' or '\n' or '\f'))
        {
            return false;
        }

        var end = html.IndexOf('>', cursor);
        if (end < 0)
        {
            return false;
        }

        after = end + 1;
        return true;
    }

    private static string Visible(string fragment)
    {
        var builder = new StringBuilder(fragment.Length);
        var index = 0;
        while (index < fragment.Length)
        {
            if (fragment[index] == '<')
            {
                if (TryMatch(fragment, index, "br", true, out _))
                {
                    builder.Append(' ');
                }

                var end = fragment.IndexOf('>', index);
                if (end < 0)
                {
                    return "";
                }

                index = end + 1;
                continue;
            }

            if (fragment[index] == '&' && TryEntity(fragment, index, out var decoded, out var next))
            {
                builder.Append(decoded);
                index = next;
                continue;
            }

            builder.Append(fragment[index]);
            index++;
        }

        return Collapse(builder.ToString());
    }

    private static bool TryEntity(string text, int index, out char value, out int next)
    {
        value = '\0';
        next = index;
        var end = text.IndexOf(';', index + 1);
        if (end < 0 || end - index > 10)
        {
            return false;
        }

        var token = text.AsSpan(index + 1, end - index - 1);
        next = end + 1;
        if (token.Equals("amp", StringComparison.Ordinal))
        {
            value = '&';
            return true;
        }

        if (token.Equals("lt", StringComparison.Ordinal))
        {
            value = '<';
            return true;
        }

        if (token.Equals("gt", StringComparison.Ordinal))
        {
            value = '>';
            return true;
        }

        if (token.Equals("quot", StringComparison.Ordinal))
        {
            value = '"';
            return true;
        }

        if (token.Equals("apos", StringComparison.Ordinal) || token.Equals("#39", StringComparison.Ordinal))
        {
            value = '\'';
            return true;
        }

        if (token.Equals("nbsp", StringComparison.Ordinal) || token.Equals("#160", StringComparison.Ordinal) || token.Equals("#xA0", StringComparison.OrdinalIgnoreCase))
        {
            value = ' ';
            return true;
        }

        if (token.Length > 1 && token[0] == '#')
        {
            var hex = token[1] is 'x' or 'X';
            var digits = hex ? token[2..] : token[1..];
            var style = hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
            if (int.TryParse(digits, style, CultureInfo.InvariantCulture, out var code) && code is > 0 and <= 0xFFFF)
            {
                value = code == 0xA0 ? ' ' : (char)code;
                return true;
            }
        }

        next = index;
        return false;
    }

    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pending = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pending = builder.Length > 0;
                continue;
            }

            if (pending)
            {
                builder.Append(' ');
                pending = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
