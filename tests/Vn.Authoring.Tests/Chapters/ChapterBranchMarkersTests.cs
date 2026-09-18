using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 챕터 그래프가 <b>분기를 볼 수 있어야 한다</b> (2026-09-18 소유자: *"챕터 그래프가 보면,
/// 분기는 선택지로 안 이어 주고 있거든?"*).
///
/// ⛔ <b>간선이 아니다.</b> 문구도 스탯변화도 없고(소유자: *"분기로 이어지는 것은 선택지와
/// 다르게 라벨이나 스탯변화를 주지도 않을거야"*), 뚫고 떼는 것은 연출 그래프의 일이다 —
/// 여기서는 <b>읽기만</b> 한다.
/// </summary>
public sealed class ChapterBranchMarkersTests
{
    [Fact]
    public void 뚫은_분기가_챕터에서_보인다()
    {
        (ProjectEditor editor, DialogueNode caller) = World();

        Assert.Empty(ChapterBranchMarkers.For(editor.Project, "ch01"));

        string lineId = editor.Project.FindScript(caller.ScriptId)!.ActiveLines.First().Id;
        DialogueNode branch = editor.AddBranchMarker(caller.Id, lineId);

        ChapterBranchMarkers.Marker marker =
            Assert.Single(ChapterBranchMarkers.For(editor.Project, "ch01"));

        Assert.Equal("root", marker.FromEpisodeId);
        Assert.Equal(EpisodeNaming.EpisodeIdOf(branch), marker.ToEpisodeId);
        Assert.Equal(branch.Id, marker.ToNodeId);
        Assert.Equal(lineId, marker.LineId);
    }

    [Fact]
    public void 대본에_적힌_차례_그대로_선다()
    {
        // ⚠ 확장(LineExtensions)의 순서는 <b>손댄 차례</b>라 사람이 읽는 차례가 아니다.
        (ProjectEditor editor, DialogueNode caller) = World();

        editor.InsertScriptLine(caller.ScriptId!);
        editor.InsertScriptLine(caller.ScriptId!);

        List<string> lines = editor.Project.FindScript(caller.ScriptId)!.ActiveLines
            .Select(line => line.Id).ToList();

        // 뒤에서 앞으로 뚫는다 — 순서가 손댄 차례라면 여기서 뒤집혀 나온다.
        editor.AddBranchMarker(caller.Id, lines[2]);
        editor.AddBranchMarker(caller.Id, lines[0]);

        Assert.Equal(
            [lines[0], lines[2]],
            ChapterBranchMarkers.For(editor.Project, "ch01").Select(marker => marker.LineId));
    }

    [Fact]
    public void 한_에피소드가_분기를_여럿_가질_수_있다()
    {
        (ProjectEditor editor, DialogueNode caller) = World();

        editor.InsertScriptLine(caller.ScriptId!);

        List<string> lines = editor.Project.FindScript(caller.ScriptId)!.ActiveLines
            .Select(line => line.Id).ToList();

        editor.AddBranchMarker(caller.Id, lines[0]);
        editor.AddBranchMarker(caller.Id, lines[1]);

        // 신원은 줄이다 — 출발·도착만으로는 둘을 못 가른다.
        Assert.Equal(2, ChapterBranchMarkers.For(editor.Project, "ch01").Count);
    }

    [Fact]
    public void 다른_판의_노드를_가리키면_안_낸다()
    {
        // ⚠ 그릴 카드가 없는 선은 판이 거짓말을 하는 것이다 (구판 프로젝트의 자유 씬).
        (ProjectEditor editor, DialogueNode caller) = World();

        StoryFile elsewhere = editor.AddStoryFile("낙서");
        DialogueNode outsider = editor.AddDialogueNode(elsewhere.Id, 0, 0, "바깥");

        string lineId = editor.Project.FindScript(caller.ScriptId)!.ActiveLines.First().Id;
        caller.RequireExtension(lineId).DetourTargetNodeId = outsider.Id;

        Assert.Empty(ChapterBranchMarkers.For(editor.Project, "ch01"));
    }

    [Fact]
    public void 챕터가_없으면_빈_목록이다()
    {
        (ProjectEditor editor, _) = World();

        Assert.Empty(ChapterBranchMarkers.For(editor.Project, "없는챕터"));
    }

    private static (ProjectEditor Editor, DialogueNode Caller) World()
    {
        var editor = new ProjectEditor(new StoryProject());

        editor.EnsureChapter("ch01");
        editor.EnsureChapterBoard("ch01");
        editor.AddEpisode("ch01", "root", title: "root", 0, 0);

        return (editor, EpisodeNaming.CardFor(editor.Project, "ch01", "root")!);
    }
}
