using System.IO.Compression;
using System.Xml.Linq;

namespace Vn.Authoring.Chapters;

/// <summary>워크북 자체를 열 수 없을 때. 어느 파일인지 반드시 담는다.</summary>
public sealed class XlsxReadException : Exception
{
    public XlsxReadException(string path, string message, Exception? inner = null)
        : base(message, inner)
    {
        Path = path;
    }

    public string Path { get; }
}

/// <summary>
/// 읽기 전용 .xlsx 최소 리더.
///
/// <b>왜 라이브러리를 쓰지 않는가</b> — 이 저장소는 중앙 패키지 관리(<c>Directory.Packages.props</c>)로
/// 의존성을 좁게 유지한다. 우리가 필요한 것은 "셀의 표시 문자열과 그 A1 좌표"뿐이고, 그건
/// zip + XML 두 개로 끝난다. 서식·수식·차트를 읽을 계획이 없으므로 스프레드시트 라이브러리
/// 하나를 통째로 들이는 값이 이득보다 크다.
///
/// <b>범위</b>: 값만 읽는다. 수식은 캐시된 결과값(<c>v</c>)을 쓴다. 서식·날짜 변환은 하지 않는다 —
/// 날짜 서식 셀은 엑셀 일련번호 문자열로 나온다. 이 레이어의 규격에 날짜 열이 없어서 문제되지 않지만,
/// 생기면 그때 명시적으로 다룬다(조용히 추측하지 않는다).
/// </summary>
internal sealed class XlsxWorkbook
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace Relationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRelationship =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private XlsxWorkbook(string path, IReadOnlyList<XlsxSheet> sheets)
    {
        Path = path;
        Sheets = sheets;
    }

    public string Path { get; }

    public IReadOnlyList<XlsxSheet> Sheets { get; }

    public XlsxSheet? Find(string sheetName) =>
        Sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, sheetName, StringComparison.Ordinal));

    public static XlsxWorkbook Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new XlsxReadException(path, $"워크북 파일이 없습니다: {path}");
        }

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);

            string[] shared = ReadSharedStrings(archive);
            XDocument workbook = LoadEntry(archive, "xl/workbook.xml")
                ?? throw new XlsxReadException(path, "xl/workbook.xml이 없습니다. .xlsx 파일이 아닌 것 같습니다.");

            Dictionary<string, string> relations = ReadRelations(archive);
            var sheets = new List<XlsxSheet>();
            int fallbackIndex = 0;

            foreach (XElement element in workbook.Descendants(Main + "sheet"))
            {
                fallbackIndex++;
                string name = element.Attribute("name")?.Value ?? $"Sheet{fallbackIndex}";
                string? relationId = element.Attribute(Relationship + "id")?.Value;

                string target = relationId is not null && relations.TryGetValue(relationId, out string? mapped)
                    ? mapped
                    : $"worksheets/sheet{fallbackIndex}.xml";

                XDocument? sheetXml = LoadEntry(archive, NormalizeTarget(target));

                if (sheetXml is null)
                {
                    // 시트를 못 찾으면 빈 시트로 두지 않고 알린다 — 조용히 사라지는 시트가 최악이다.
                    throw new XlsxReadException(path, $"시트 '{name}'의 XML({target})을 찾을 수 없습니다.");
                }

                sheets.Add(ReadSheet(name, sheetXml, shared));
            }

            return new XlsxWorkbook(path, sheets);
        }
        catch (XlsxReadException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new XlsxReadException(path, $"워크북을 읽지 못했습니다: {exception.Message}", exception);
        }
    }

    private static string NormalizeTarget(string target)
    {
        string value = target.Replace('\\', '/').TrimStart('/');
        return value.StartsWith("xl/", StringComparison.Ordinal) ? value : "xl/" + value;
    }

    private static XDocument? LoadEntry(ZipArchive archive, string entryPath)
    {
        ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(item =>
            string.Equals(item.FullName.Replace('\\', '/'), entryPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return null;
        }

        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Dictionary<string, string> ReadRelations(ZipArchive archive)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        XDocument? document = LoadEntry(archive, "xl/_rels/workbook.xml.rels");

        if (document is null)
        {
            return map;
        }

        foreach (XElement element in document.Descendants(PackageRelationship + "Relationship"))
        {
            string? id = element.Attribute("Id")?.Value;
            string? target = element.Attribute("Target")?.Value;

            if (id is not null && target is not null)
            {
                map[id] = target;
            }
        }

        return map;
    }

    /// <summary>
    /// 공유 문자열 표. 후리가나(<c>rPh</c>)의 <c>t</c>는 본문이 아니므로 제외한다 —
    /// 포함하면 한국어·일본어 셀에서 발음이 본문 뒤에 붙어 나온다.
    /// </summary>
    private static string[] ReadSharedStrings(ZipArchive archive)
    {
        XDocument? document = LoadEntry(archive, "xl/sharedStrings.xml");

        if (document is null)
        {
            return Array.Empty<string>();
        }

        return document.Descendants(Main + "si")
            .Select(TextOf)
            .ToArray();
    }

    private static string TextOf(XElement container) =>
        string.Concat(container.Descendants(Main + "t")
            .Where(text => text.Ancestors(Main + "rPh").Any() is false)
            .Select(text => text.Value));

    private static XlsxSheet ReadSheet(string name, XDocument document, string[] shared)
    {
        var rows = new Dictionary<int, Dictionary<int, string>>();

        foreach (XElement rowElement in document.Descendants(Main + "row"))
        {
            foreach (XElement cellElement in rowElement.Elements(Main + "c"))
            {
                string? reference = cellElement.Attribute("r")?.Value;

                if (reference is null || !TryParseReference(reference, out int row, out int column))
                {
                    continue;
                }

                string value = ReadCellValue(cellElement, shared);

                if (value.Length == 0)
                {
                    continue;
                }

                if (!rows.TryGetValue(row, out Dictionary<int, string>? cells))
                {
                    cells = new Dictionary<int, string>();
                    rows[row] = cells;
                }

                cells[column] = value;
            }
        }

        return new XlsxSheet(name, rows);
    }

    private static string ReadCellValue(XElement cell, string[] shared)
    {
        string type = cell.Attribute("t")?.Value ?? "n";

        switch (type)
        {
            case "s":
            {
                string raw = cell.Element(Main + "v")?.Value ?? string.Empty;
                return int.TryParse(raw, out int index) && index >= 0 && index < shared.Length
                    ? shared[index].Trim()
                    : string.Empty;
            }

            case "inlineStr":
            {
                XElement? inline = cell.Element(Main + "is");
                return inline is null ? string.Empty : TextOf(inline).Trim();
            }

            case "b":
            {
                string raw = cell.Element(Main + "v")?.Value ?? string.Empty;
                return raw == "1" ? "TRUE" : raw == "0" ? "FALSE" : raw.Trim();
            }

            default:
                // "str"(수식 결과 문자열), "n"(숫자), "e"(오류)는 모두 v의 원문을 그대로 쓴다.
                return (cell.Element(Main + "v")?.Value ?? string.Empty).Trim();
        }
    }

    /// <summary>"AB12" → row 12, column 28. 열은 1부터다.</summary>
    internal static bool TryParseReference(string reference, out int row, out int column)
    {
        row = 0;
        column = 0;
        int index = 0;

        while (index < reference.Length && char.IsAsciiLetter(reference[index]))
        {
            column = (column * 26) + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }

        if (column == 0 || index >= reference.Length)
        {
            return false;
        }

        return int.TryParse(reference[index..], out row) && row > 0;
    }

    /// <summary>1 → "A", 28 → "AB". 진단 메시지가 사람이 보는 열 이름을 쓰게 한다.</summary>
    internal static string ColumnName(int column)
    {
        if (column <= 0)
        {
            return "?";
        }

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
}

/// <summary>값이 있는 셀만 담은 한 시트. 빈 셀은 존재하지 않는 것과 같게 다룬다.</summary>
internal sealed class XlsxSheet
{
    private readonly Dictionary<int, Dictionary<int, string>> _rows;

    public XlsxSheet(string name, Dictionary<int, Dictionary<int, string>> rows)
    {
        Name = name;
        _rows = rows;
    }

    public string Name { get; }

    /// <summary>값이 하나라도 있는 행 번호. 오름차순.</summary>
    public IReadOnlyList<int> RowNumbers => _rows.Keys.OrderBy(number => number).ToList();

    public string? Cell(int row, int column) =>
        _rows.TryGetValue(row, out Dictionary<int, string>? cells) &&
        cells.TryGetValue(column, out string? value)
            ? value
            : null;

    /// <summary>그 행에서 값이 있는 가장 오른쪽 열. 값이 없으면 0.</summary>
    public int LastColumn(int row) =>
        _rows.TryGetValue(row, out Dictionary<int, string>? cells) && cells.Count > 0
            ? cells.Keys.Max()
            : 0;

    public bool RowHasValues(int row) =>
        _rows.TryGetValue(row, out Dictionary<int, string>? cells) && cells.Count > 0;
}
