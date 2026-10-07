using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace PosterPrintRequest.Tests.Requests;

internal static class SamplePosters
{
    public static byte[] Pdf(decimal widthInches, decimal heightInches, int rotate = 0) =>
        Pages([(widthInches, heightInches, rotate)]);

    public static byte[] Pages(IReadOnlyList<(decimal WidthInches, decimal HeightInches, int Rotate)> pages)
    {
        var objects = new List<string>
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"
        };
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(index => $"{index + 3} 0 R"));
        objects.Add($"2 0 obj\n<< /Type /Pages /Count {pages.Count} /Kids [{kids}] >>\nendobj\n");
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var rotation = page.Rotate == 0 ? "" : $" /Rotate {page.Rotate}";
            objects.Add(
                $"{index + 3} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Points(page.WidthInches)} {Points(page.HeightInches)}]{rotation} >>\nendobj\n");
        }

        return Assemble(objects);
    }

    public static byte[] EmptyPdf() =>
        Assemble(
        [
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Count 0 /Kids [] >>\nendobj\n"
        ]);

    public static byte[] Pptx(decimal widthInches, decimal heightInches, int slides = 1, bool includeSlideParts = true)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var slideList = new StringBuilder();
            var relationships = new StringBuilder();
            var contentTypes = new StringBuilder();
            contentTypes.Append("""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>

                """);
            relationships.Append("""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">

                """);
            for (var slide = 1; slide <= slides; slide++)
            {
                slideList.Append($"""<p:sldId id="{255 + slide}" r:id="rId{slide}"/>""");
                relationships.Append($"""
                    <Relationship Id="rId{slide}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide{slide}.xml"/>

                    """);
                contentTypes.Append($"""
                      <Override PartName="/ppt/slides/slide{slide}.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>

                    """);
                if (includeSlideParts)
                {
                    Write(zip, $"ppt/slides/slide{slide}.xml", """
                        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                        <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld><p:spTree/></p:cSld></p:sld>
                        """);
                }
            }

            relationships.Append("</Relationships>");
            contentTypes.Append("</Types>");
            Write(zip, "[Content_Types].xml", contentTypes.ToString());
            Write(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
                </Relationships>
                """);
            Write(zip, "ppt/presentation.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:presentation xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
                  <p:sldSz cx="{Emus(widthInches)}" cy="{Emus(heightInches)}"/>
                  <p:sldIdLst>{slideList}</p:sldIdLst>
                </p:presentation>
                """);
            Write(zip, "ppt/_rels/presentation.xml.rels", relationships.ToString());
        }

        return stream.ToArray();
    }

    public static byte[] WordDocument()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Write(zip, "word/document.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body/></w:document>
                """);
        }

        return stream.ToArray();
    }

    public static byte[] BrokenPdf() => Encoding.ASCII.GetBytes("%PDF-1.7\nnot a pdf\n%%EOF\n");

    public static byte[] BrokenZip() => [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00];

    public static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static byte[] Assemble(IReadOnlyList<string> objects)
    {
        using var stream = new MemoryStream();
        var encoder = Encoding.ASCII;
        void WriteText(string text)
        {
            var bytes = encoder.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        WriteText("%PDF-1.4\n");
        var offsets = new long[objects.Count + 1];
        for (var index = 0; index < objects.Count; index++)
        {
            offsets[index + 1] = stream.Position;
            WriteText(objects[index]);
        }

        var startxref = stream.Position;
        WriteText($"xref\n0 {objects.Count + 1}\n");
        WriteText("0000000000 65535 f \n");
        for (var index = 1; index < offsets.Length; index++)
        {
            WriteText($"{offsets[index]:0000000000} 00000 n \n");
        }

        WriteText($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{startxref}\n%%EOF\n");
        return stream.ToArray();
    }

    private static string Points(decimal inches) =>
        (inches * 72m).ToString("0.####", CultureInfo.InvariantCulture);

    private static string Emus(decimal inches) =>
        decimal.Round(inches * 914400m, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

    private static void Write(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
