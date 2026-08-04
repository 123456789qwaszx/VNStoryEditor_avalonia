using Vn.App.Services;
using Vn.Authoring.Rendering;

namespace Vn.App.Tests;

/// <summary>
/// K1 ②안의 "보인다" 쪽 — 낡은 산출 파일은 조용히 남지 않고 상태줄에 오른다.
/// 목록 자체가 실제 폴더와 맞는지는 Authoring 쪽 OutputManifestTests가 고정한다.
/// </summary>
public class LiveOutputReportTests
{
    [Fact]
    public void 고아가_없으면_아무_말도_하지_않는다()
    {
        Assert.Null(LiveOutputService.OrphanReport(OrphanOutputScan.Empty));
    }

    [Fact]
    public void 고아_목록과_유니티_영향이_함께_보인다()
    {
        var scan = new OrphanOutputScan(
            [new OrphanOutput("Story_옛이름.yarn", OrphanOutputSource.Recorded)],
            null);

        string message = LiveOutputService.OrphanReport(scan)!;

        Assert.Contains("Story_옛이름.yarn", message, StringComparison.Ordinal);
        Assert.Contains("옛 대사가 재생될 수 있습니다", message, StringComparison.Ordinal);
        Assert.Contains("양식", message, StringComparison.Ordinal); // 전체 목록이 어디 있는지
    }

    [Fact]
    public void 목록이_길면_개수로_접되_숨기지는_않는다()
    {
        var scan = new OrphanOutputScan(
            [
                new OrphanOutput("Story_1.yarn", OrphanOutputSource.Recorded),
                new OrphanOutput("Story_2.yarn", OrphanOutputSource.Recorded),
                new OrphanOutput("Story_3.yarn", OrphanOutputSource.Recorded),
                new OrphanOutput("Story_4.yarn", OrphanOutputSource.NameShape)
            ],
            null);

        string message = LiveOutputService.OrphanReport(scan)!;

        Assert.Contains("4개가 남아 있습니다", message, StringComparison.Ordinal);
        Assert.Contains("외 1개", message, StringComparison.Ordinal);
    }

    [Fact]
    public void 판정하지_못한_사유는_고아가_없어도_알린다()
    {
        // 규칙 14 — 판정을 못 했다는 사실 자체가 숨겨지면 안 된다.
        var scan = new OrphanOutputScan([], "출력 기록을 읽지 못했습니다.");

        Assert.Equal("출력 기록을 읽지 못했습니다.", LiveOutputService.OrphanReport(scan));
    }
}
