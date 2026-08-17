using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;

namespace Vn.Authoring.Tests;

/// <summary>
/// 아이템·능력의 챕터 네임스페이스 (2026-08-17 소유자: "아이템, 능력은 오직 챕터단위로만
/// 살리는게 맞아. 이건 짧은 스토리 단위를 상정한거야").
///
/// Yarn의 변수 저장소는 하나뿐이라(계약서 D1) 접두 없이는 챕터가 서로를 덮는다.
/// 뿌리는 <b>판 Id</b>다 — 챕터 이름을 바꿔도 아이템이 초기화되지 않는다.
/// </summary>
public sealed class Tier1NamespaceTests
{
    private static (StoryProject Project, ProjectEditor Editor, StoryFile File, DialogueNode Node) Build(
        string fileId, string fileName)
    {
        var project = new StoryProject();
        var file = new StoryFile(fileId, fileName);
        project.Files.Add(file);
        var editor = new ProjectEditor(project);
        DialogueNode node = editor.AddDialogueNode(file.Id, name: "EP00");

        return (project, editor, file, node);
    }

    [Fact]
    public void 접두의_뿌리는_판_Id라_챕터_개명에_흔들리지_않는다()
    {
        (StoryProject project, _, StoryFile file, DialogueNode node) = Build("sf_a1", "ch01");

        string before = Tier1Namespace.PrefixFor(project, node.Id);
        Assert.Equal("__t1_sf_a1_", before);

        file.Name = "완전히 다른 챕터 이름";

        Assert.Equal(before, Tier1Namespace.PrefixFor(project, node.Id));
    }

    [Fact]
    public void 다른_판의_같은_이름은_다른_변수가_된다()
    {
        (StoryProject first, _, _, DialogueNode firstNode) = Build("sf_a1", "ch01");
        var second = new StoryFile("sf_b2", "ch02");
        first.Files.Add(second);
        DialogueNode secondNode = new ProjectEditor(first).AddDialogueNode(second.Id, name: "EP00");

        var empty = new HashSet<string>(StringComparer.Ordinal);
        string one = Tier1Namespace.Apply(
            "열쇠", Tier1Namespace.PrefixFor(first, firstNode.Id), empty);
        string two = Tier1Namespace.Apply(
            "열쇠", Tier1Namespace.PrefixFor(first, secondNode.Id), empty);

        Assert.NotEqual(one, two);
        Assert.Equal("__t1_sf_a1_열쇠", one);
        Assert.Equal("__t1_sf_b2_열쇠", two);
    }

    [Fact]
    public void A계층_스탯과_합성_추적_변수에는_붙지_않는다()
    {
        (StoryProject project, ProjectEditor editor, StoryFile file, DialogueNode node) =
            Build("sf_a1", "ch01");

        // 챕터 조건 공급 노드가 쓰는 이름이 곧 스탯이다 — 추측이 아니라 명시 목록이다.
        SetNode supply = editor.AddSetNode(file.Id, name: EpisodeSyncService.ConditionSupplyNodeName("ch01"));
        editor.AddCondition(supply.Id, "신뢰높음", "$trust >= 3");

        HashSet<string> stats = Tier1Namespace.StatNames(project, node.Id);
        Assert.Equal(["trust"], stats);

        string prefix = Tier1Namespace.PrefixFor(project, node.Id);
        Assert.Equal("trust", Tier1Namespace.Apply("trust", prefix, stats));      // 스탯 — 그대로
        Assert.Equal("__ch_0", Tier1Namespace.Apply("__ch_0", prefix, stats));    // 합성 추적 — 그대로
        Assert.Equal("__t1_sf_a1_열쇠", Tier1Namespace.Apply("열쇠", prefix, stats)); // 아이템 — 접두
    }

    [Fact]
    public void 조건식_안의_변수만_갈아_끼운다()
    {
        var stats = new HashSet<string>(["trust"], StringComparer.Ordinal);

        Assert.Equal(
            "$__t1_sf_a1_열쇠 == true and $trust >= 3",
            Tier1Namespace.ApplyToExpression("$열쇠 == true and $trust >= 3", "__t1_sf_a1_", stats));

        // 변수가 아닌 글자는 손대지 않는다 — 식은 사람이 쓴 원문이다.
        Assert.Equal(
            "not ($__t1_sf_a1_a > 1)",
            Tier1Namespace.ApplyToExpression("not ($a > 1)", "__t1_sf_a1_", stats));
    }

    [Fact]
    public void 판을_모르면_접두가_없다()
    {
        // 판 밖의 노드(있을 수 없지만)나 프로젝트 없는 호출에서 엉뚱한 이름을 만들지 않는다.
        Assert.Equal(string.Empty, Tier1Namespace.PrefixFor(project: null, "nd_x"));
        Assert.Empty(Tier1Namespace.StatNames(project: null, "nd_x"));

        var empty = new HashSet<string>(StringComparer.Ordinal);
        Assert.Equal("열쇠", Tier1Namespace.Apply("$열쇠", string.Empty, empty));
    }
}
