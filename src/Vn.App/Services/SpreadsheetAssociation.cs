using System.Runtime.InteropServices;
using System.Text;

namespace Vn.App.Services;

/// <summary>
/// .xlsx의 OS 기본 앱이 정말 스프레드시트인지 묻는다.
///
/// 더블클릭 열기가 OS 연결을 맹신하면, 엑셀이 없는 기계에서 .xlsx를 가로챈 엉뚱한 앱이
/// 뜬다(실사례: 챗지피티). 워크북은 편집하라고 여는 것이므로, 편집할 수 없는 앱에
/// 던지느니 폴더에서 파일을 보여 주고 사유를 말하는 편이 낫다.
/// </summary>
internal static class SpreadsheetAssociation
{
    /// <summary>
    /// 알려진 스프레드시트 앱의 실행 파일 표식. <b>목록에 없으면 스프레드시트가 아니라고
    /// 판정한다</b> — 모르는 앱을 신뢰하는 쪽보다 안전한 방향으로 틀린다.
    /// </summary>
    private static readonly string[] HandlerMarkers =
    [
        "excel",       // Microsoft Excel
        "scalc",       // LibreOffice Calc (직접 연결)
        "soffice",     // LibreOffice (공용 진입점)
        "et.exe",      // WPS Office 스프레드시트
        "wps",         // WPS Office (공용 진입점)
        "onlyoffice",  // OnlyOffice
        "gnumeric",    // Gnumeric
        "planmaker"    // SoftMaker PlanMaker
    ];

    /// <summary>.xlsx 기본 핸들러의 실행 파일 경로. 연결이 없으면 null.</summary>
    public static string? ResolveXlsxHandler()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var buffer = new StringBuilder(1024);
        uint length = (uint)buffer.Capacity;

        uint result = AssocQueryString(
            flags: 0, str: AssocStrExecutable, ".xlsx", "open", buffer, ref length);

        return result == 0 ? buffer.ToString() : null;
    }

    /// <summary>실행 파일 경로로 스프레드시트 앱인지 판정한다. null(연결 없음)은 아니다.</summary>
    public static bool IsSpreadsheetHandler(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        string fileName = Path.GetFileName(executablePath);

        return HandlerMarkers.Any(marker =>
            fileName.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private const uint AssocStrExecutable = 2;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
    private static extern uint AssocQueryString(
        uint flags, uint str, string assoc, string? extra, StringBuilder buffer, ref uint length);
}
