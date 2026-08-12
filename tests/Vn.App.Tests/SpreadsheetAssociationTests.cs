using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// .xlsx 기본 앱 판정 (챕터 v2 8단계). 실사례: 엑셀 없는 기계에서 .xlsx가 챗지피티에
/// 연결돼 있어 더블클릭이 챗지피티를 열었다 — 목록에 없는 앱은 스프레드시트가 아니다.
/// </summary>
public sealed class SpreadsheetAssociationTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE")]
    [InlineData(@"C:\Program Files\LibreOffice\program\scalc.exe")]
    [InlineData(@"C:\Program Files\LibreOffice\program\soffice.exe")]
    [InlineData(@"C:\Users\me\AppData\Local\Kingsoft\WPS Office\et.exe")]
    public void 스프레드시트_앱은_통과한다(string handler) =>
        Assert.True(SpreadsheetAssociation.IsSpreadsheetHandler(handler));

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\ChatGPT\ChatGPT.exe")]
    [InlineData(@"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void 모르는_앱과_빈_연결은_아니다(string? handler) =>
        Assert.False(SpreadsheetAssociation.IsSpreadsheetHandler(handler));
}
