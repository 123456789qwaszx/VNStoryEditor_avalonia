using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// 무대 조절창 [자주 쓰는] 칩의 데이터 규칙 (2026-08-22 소유자: "엄청 자주 쓰이고
/// 유용한 것들을 … 일종의 핫키 느낌으로 정리").
///
/// 여기서 지키는 것 셋: <b>기본 목록은 코드가 쥔다</b>(손대지 않은 프로젝트 파일에는
/// 키가 없다) · <b>빈 목록은 기본이 아니다</b>(다 지운 사람에게 열한 개를 되돌려 주지
/// 않는다) · <b>기본 칩은 눌러서 유효한 커맨드가 된다</b>(기본값 없는 필수 인자를 칩이
/// 채워 둔다 — 넛지의 distance가 그것이다).
/// </summary>
public sealed class StageQuickCommandTests
{
    [Fact]
    public void 기본_칩은_전부_카탈로그에_있고_필수_인자가_채워져_있다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        Assert.NotEmpty(StageQuickCommands.Default);

        foreach (StageQuickCommand chip in StageQuickCommands.Default)
        {
            PresentationCommandDefinition definition = catalog.Find(chip.DefinitionId)
                ?? throw new Xunit.Sdk.XunitException(
                    $"기본 칩 '{chip.DisplayName}'의 정의 '{chip.DefinitionId}'가 카탈로그에 없다.");

            // 칩이 적은 인자는 실재하는 파라미터여야 한다 — 오타는 조용히 사라지는 인자가 된다.
            foreach (string name in chip.Arguments.Keys)
            {
                Assert.True(
                    definition.FindParameter(name) is not null,
                    $"'{chip.DisplayName}'의 인자 '{name}'이 {definition.Id}에 없다.");
            }

            // 대상 슬롯은 누를 때 채운다. 그 밖의 필수 인자는 기본값이나 칩이 대야 한다.
            foreach (PresentationCommandParameter parameter in definition.Parameters)
            {
                if (!parameter.Required || ArgumentTokenCandidates.IsStageTargetType(parameter.Type))
                {
                    continue;
                }

                Assert.True(
                    parameter.Default is not null || chip.Arguments.ContainsKey(parameter.Name),
                    $"'{chip.DisplayName}'이 필수 인자 '{parameter.Name}'을 안 채웠다 — 누르면 무효한 커맨드가 된다.");
            }
        }
    }

    [Fact]
    public void 손대지_않은_프로젝트는_기본을_쓰고_저장물에_키가_없다()
    {
        var project = new StoryProject();

        Assert.Null(project.QuickCommands);
        Assert.Equal(StageQuickCommands.Default.Count, project.EffectiveQuickCommands.Count);
        Assert.DoesNotContain("quickCommands", ProjectManifestJson.Write(project), StringComparison.Ordinal);
    }

    [Fact]
    public void 담기는_기본을_실체화한_뒤_뒤에_붙고_같은_것을_두_번_담지_않는다()
    {
        var editor = new ProjectEditor(new StoryProject());
        var chip = new StageQuickCommand(
            "화면 흔들기",
            "shot.shot_track",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["x"] = "0.2u" });

        int index = editor.PinQuickCommand(chip);

        Assert.Equal(StageQuickCommands.Default.Count, index);
        Assert.Equal(StageQuickCommands.Default.Count + 1, editor.Project.EffectiveQuickCommands.Count);
        Assert.Equal("화면 흔들기", editor.Project.EffectiveQuickCommands[index].DisplayName);

        // 이름만 다른 같은 커맨드·같은 인자는 새 칩이 아니다 — 이미 있는 자리를 돌려준다.
        Assert.Equal(index, editor.PinQuickCommand(chip with { DisplayName = "다른 이름" }));
        Assert.Equal(StageQuickCommands.Default.Count + 1, editor.Project.EffectiveQuickCommands.Count);
    }

    [Fact]
    public void 이름은_나중에_고치고_빈_이름은_고치지_않은_것으로_친다()
    {
        var editor = new ProjectEditor(new StoryProject());

        editor.RenameQuickCommandAt(0, "  확 당기기  ");
        Assert.Equal("확 당기기", editor.Project.EffectiveQuickCommands[0].DisplayName);

        // 빈 이름은 칩을 이름 없는 단추로 만든다 — 고치지 않은 것으로 친다.
        editor.RenameQuickCommandAt(0, "   ");
        Assert.Equal("확 당기기", editor.Project.EffectiveQuickCommands[0].DisplayName);

        // 커맨드·인자는 이름과 무관하다.
        Assert.Equal(StageQuickCommands.Default[0].DefinitionId, editor.Project.EffectiveQuickCommands[0].DefinitionId);
        Assert.Equal(StageQuickCommands.Default[0].Arguments, editor.Project.EffectiveQuickCommands[0].Arguments);
    }

    [Fact]
    public void 칩의_수치는_따로_고치고_나머지_인자는_안_건드린다()
    {
        var editor = new ProjectEditor(new StoryProject());

        editor.SetQuickCommandArgument(0, "zoom", "2");

        StageQuickCommand chip = editor.Project.EffectiveQuickCommands[0];
        Assert.Equal("2", chip.Arguments["zoom"]);
        Assert.Equal(StageQuickCommands.Default[0].Arguments["duration"], chip.Arguments["duration"]);
        Assert.Equal(StageQuickCommands.Default[0].DisplayName, chip.DisplayName);

        // 같은 값을 다시 쓰는 것은 편집이 아니다 — undo 스택에 빈 단계를 쌓지 않는다.
        editor.SetQuickCommandArgument(0, "zoom", "2");
        editor.Undo();
        Assert.Null(editor.Project.QuickCommands);
    }

    [Fact]
    public void 전부_빼면_빈_목록이_남고_기본으로_되돌아가지_않는다()
    {
        var editor = new ProjectEditor(new StoryProject());

        while (editor.Project.EffectiveQuickCommands.Count > 0)
        {
            editor.RemoveQuickCommandAt(0);
        }

        Assert.Empty(editor.Project.EffectiveQuickCommands);
        Assert.NotNull(editor.Project.QuickCommands);

        // 저장 → 다시 읽어도 빈 목록이다. 빈 배열과 키 없음을 구분하지 않으면 여기서 열한 개가 돌아온다.
        ProjectManifest manifest = ProjectManifestJson.Read(ProjectManifestJson.Write(editor.Project));
        Assert.NotNull(manifest.QuickCommands);
        Assert.Empty(manifest.QuickCommands!);

        // [기본값 복원]은 키를 아예 지운다.
        editor.ResetQuickCommands();
        Assert.Null(editor.Project.QuickCommands);
        Assert.Equal(StageQuickCommands.Default.Count, editor.Project.EffectiveQuickCommands.Count);
    }

    [Fact]
    public void 되돌리기가_담은_칩을_손대지_않은_상태까지_원복한다()
    {
        var editor = new ProjectEditor(new StoryProject());

        editor.PinQuickCommand(new StageQuickCommand(
            "긴 암전", "transition.tx_daze_in", new Dictionary<string, string>(StringComparer.Ordinal)));
        Assert.NotNull(editor.Project.QuickCommands);

        editor.Undo();

        Assert.Null(editor.Project.QuickCommands);
        Assert.Equal(StageQuickCommands.Default.Count, editor.Project.EffectiveQuickCommands.Count);
    }

    [Fact]
    public void 칩은_manifest와_undo_스냅샷_양쪽을_왕복한다()
    {
        var project = new StoryProject
        {
            QuickCommands =
            [
                new StageQuickCommand(
                    "짧은 사이",
                    "common_control.pause",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["seconds"] = "0.3" })
            ]
        };

        ProjectManifest manifest = ProjectManifestJson.Read(ProjectManifestJson.Write(project));
        StageQuickCommand fromManifest = Assert.Single(manifest.QuickCommands!);
        Assert.Equal("짧은 사이", fromManifest.DisplayName);
        Assert.Equal("common_control.pause", fromManifest.DefinitionId);
        Assert.Equal("0.3", fromManifest.Arguments["seconds"]);

        StoryProject decoded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(project));
        StageQuickCommand fromSnapshot = Assert.Single(decoded.QuickCommands!);
        Assert.Equal("0.3", fromSnapshot.Arguments["seconds"]);
    }
}
