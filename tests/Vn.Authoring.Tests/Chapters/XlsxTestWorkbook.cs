using System.IO.Compression;
using System.Text;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 테스트가 쓰는 최소 .xlsx 작성기.
///
/// 오류 케이스마다 이진 견본 파일을 저장소에 넣으면 무엇이 잘못됐는지 파일을 열어 봐야 안다.
/// 시트 내용을 테스트 코드 안에 두면 "이 표에서 이 오류가 나온다"가 한눈에 읽힌다.
/// 값은 전부 inlineStr로 쓴다 — 리더가 공유 문자열·inlineStr 양쪽을 다루므로 어느 쪽으로 써도
/// 되지만, 공유 문자열 표가 없는 쪽이 테스트 코드가 짧다.
/// </summary>
internal static class XlsxTestWorkbook
{
    /// <summary>시트 이름 → 행 목록(행은 셀 문자열 배열). null 셀은 빈 칸이다.</summary>
    public static string Write(string directory, string fileName, params (string Name, string?[][] Rows)[] sheets)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        AddEntry(archive, "[Content_Types].xml", ContentTypes(sheets.Length));
        AddEntry(archive, "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        AddEntry(archive, "xl/workbook.xml", Workbook(sheets));
        AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelations(sheets.Length));

        for (int index = 0; index < sheets.Length; index++)
        {
            AddEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", Sheet(sheets[index].Rows));
        }

        return path;
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using Stream stream = entry.Open();
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string ContentTypes(int sheetCount)
    {
        var builder = new StringBuilder();
        builder.Append("""
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
            """);

        for (int index = 1; index <= sheetCount; index++)
        {
            builder.Append($"""
                <Override PartName="/xl/worksheets/sheet{index}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                """);
        }

        builder.Append("</Types>");
        return builder.ToString();
    }

    private static string Workbook((string Name, string?[][] Rows)[] sheets)
    {
        var builder = new StringBuilder();
        builder.Append("""
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
            """);

        for (int index = 0; index < sheets.Length; index++)
        {
            builder.Append(
                $"""<sheet name="{Escape(sheets[index].Name)}" sheetId="{index + 1}" r:id="rId{index + 1}"/>""");
        }

        builder.Append("</sheets></workbook>");
        return builder.ToString();
    }

    private static string WorkbookRelations(int sheetCount)
    {
        var builder = new StringBuilder();
        builder.Append("""
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            """);

        for (int index = 1; index <= sheetCount; index++)
        {
            builder.Append($"""
                <Relationship Id="rId{index}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{index}.xml"/>
                """);
        }

        builder.Append("</Relationships>");
        return builder.ToString();
    }

    private static string Sheet(string?[][] rows)
    {
        var builder = new StringBuilder();
        builder.Append("""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
            """);

        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            int rowNumber = rowIndex + 1;
            builder.Append($"""<row r="{rowNumber}">""");

            string?[] cells = rows[rowIndex];

            for (int columnIndex = 0; columnIndex < cells.Length; columnIndex++)
            {
                string? value = cells[columnIndex];

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                string reference = ColumnName(columnIndex + 1) + rowNumber;
                builder.Append(
                    $"""<c r="{reference}" t="inlineStr"><is><t xml:space="preserve">{Escape(value)}</t></is></c>""");
            }

            builder.Append("</row>");
        }

        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static string ColumnName(int column)
    {
        var name = new Stack<char>();
        int value = column;

        while (value > 0)
        {
            int remainder = (value - 1) % 26;
            name.Push((char)('A' + remainder));
            value = (value - 1) / 26;
        }

        return new string(name.ToArray());
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
