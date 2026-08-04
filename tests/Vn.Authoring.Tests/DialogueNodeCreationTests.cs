using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>X4 — 대사노드는 생성 즉시 편집 대상이다. 전용 대본과 첫 빈 줄이 함께 태어난다.</summary>
public class DialogueNodeCreationTests
{
    private static (ProjectEditor Editor, StoryFile File) BuildEditor()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_x4", "테스트", "story/x4.vnstory.json");
        project.Files.Add(file);
        int next = 0;
        return (new ProjectEditor(project, newLineId: () => $"ln_{++next:D3}"), file);
    }

    [Fact]
    public void 노드를_만들면_전용_대본과_첫_빈_줄이_함께_생긴다()
    {
        (ProjectEditor editor, StoryFile file) = BuildEditor();

        DialogueNode node = editor.AddDialogueNode(file.Id, name: "장면 1");

        ScriptDocument script = Assert.Single(editor.Project.Scripts);
        Assert.Equal(script.Id, node.ScriptId);
        Assert.Equal("장면 1 대본", script.Name);

        // 생성 직후 빈 라인 목록이 아니라 첫 줄이 준비되어 바로 타이핑한다.
        ScriptLine first = Assert.Single(script.ActiveLines);
        Assert.Equal("ln_001", first.Id);
        Assert.Equal(LocalizedLine.Empty, script.Text(first.Id));

        // 노드·대본·첫 줄이 한 번의 편집이다 — 되돌리기 한 번에 전부 사라진다.
        editor.Undo();
        Assert.Empty(editor.Project.Scripts);
        Assert.Empty(editor.Project.EnumerateNodes());
    }

    [Fact]
    public void 명시한_대본을_주면_그_대본을_그대로_쓴다()
    {
        (ProjectEditor editor, StoryFile file) = BuildEditor();
        ScriptDocument shared = editor.AddScript("공유 대본");

        DialogueNode node = editor.AddDialogueNode(file.Id, name: "장면", scriptId: shared.Id);

        Assert.Equal(shared.Id, node.ScriptId);
        Assert.Single(editor.Project.Scripts); // 새 대본을 만들지 않는다
    }

    [Fact]
    public void 대본_없는_옛_노드는_EnsureDialogueScript로_처음_쓸_수_있게_된다()
    {
        // 가져오기 시절 프로젝트를 로드한 상태 — ScriptId가 null인 노드.
        (ProjectEditor editor, StoryFile file) = BuildEditor();
        DialogueNode legacy = editor.AddNode(file.Id, new DialogueNode(name: "옛 노드"));
        Assert.Null(legacy.ScriptId);

        ScriptDocument script = editor.EnsureDialogueScript(legacy.Id);

        Assert.Equal(script.Id, legacy.ScriptId);
        Assert.Same(script, editor.EnsureDialogueScript(legacy.Id)); // 두 번째는 그대로
    }

    [Fact]
    public void 자동_대본도_저장_형식은_기존_그대로_왕복한다()
    {
        // 로드 호환 — 새 형식 필드 없이 기존 scripts/files 구조로 저장된다.
        (ProjectEditor editor, StoryFile file) = BuildEditor();
        editor.AddDialogueNode(file.Id, name: "장면");

        StoryProject decoded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(editor.Project));

        DialogueNode node = Assert.Single(decoded.EnumerateNodes().OfType<DialogueNode>());
        Assert.NotNull(decoded.FindScript(node.ScriptId));
    }
}
