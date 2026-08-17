using Vn.App.Views;

namespace Vn.App.Tests;

/// <summary>
/// 캐스팅 목록의 출처는 셋이다 (2026-08-17 소유자 보고: "연출 노드에서 슬롯을 만들고
/// 캐릭터를 캐스팅 하려는데 기획자가 지정한 캐릭터도 안 보이고").
///
/// 챕터 `화자` 시트의 캐릭터키가 빠져 있었다 — 시나리오 그래프의 [표정] 단추와 같은
/// 구멍이다. 초상화가 아직 없어도 <b>이름은 정해진 것</b>이니 골라야 한다.
/// </summary>
public sealed class CastingCandidateTests
{
    [Fact]
    public void 챕터_화자_시트의_캐릭터키도_고를_수_있다()
    {
        string[] candidates = StageSceneView.CastingCandidates(
            portraits: ["willo"],
            defined: ["laru"],
            chapterSheet: ["merchant"]);

        Assert.Equal(["laru", "merchant", "willo"], candidates);
    }

    [Fact]
    public void 초상화가_없어도_이름만으로_선다()
    {
        // 기획자가 챕터 시트에 이름만 적어 둔 단계 — 그림은 나중에 온다.
        string[] candidates = StageSceneView.CastingCandidates(
            portraits: [],
            defined: [],
            chapterSheet: ["merchant"]);

        Assert.Equal(["merchant"], candidates);
    }

    [Fact]
    public void 세_출처가_겹쳐도_한_번씩만_선다()
    {
        string[] candidates = StageSceneView.CastingCandidates(
            portraits: ["laru"],
            defined: ["laru"],
            chapterSheet: ["laru"]);

        Assert.Equal(["laru"], candidates);
    }

    [Fact]
    public void 캐릭터키가_빈_화자는_목록에_안_선다()
    {
        // 정의 파일의 speakers는 캐릭터키가 없을 수 있다 — 캐스팅할 대상이 아니다.
        string[] candidates = StageSceneView.CastingCandidates(
            portraits: [],
            defined: [null, string.Empty, "  ", "laru"],
            chapterSheet: []);

        Assert.Equal(["laru"], candidates);
    }
}
