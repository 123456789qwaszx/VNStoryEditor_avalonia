using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// W19 텍스트 입력. 파싱과 병기 텍스트가 같은 카탈로그 규칙 하나를 지나므로
/// 입력한 것이 그대로 되돌아 나와야 하고, 틀린 입력은 추측 보정 없이 즉시 오류다.
/// </summary>
public class CommandTextTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static string RoundTrip(string input)
    {
        CommandTextParseResult parsed = CommandText.Parse(input, Catalog);
        Assert.True(parsed.Success, parsed.Error);
        return CommandText.Format(parsed.Definition, parsed.Definition!.Id, parsed.Arguments!);
    }

    [Fact]
    public void 입력_구조_병기_텍스트가_왕복한다()
    {
        Assert.Equal("<<face_swap @1 5 4fr>>", RoundTrip("<<face_swap @1 5 4fr>>"));
        Assert.Equal("<<bg_sprite bg0 street_night>>", RoundTrip("bg_sprite bg0 street_night"));
    }

    [Fact]
    public void 꺾쇠는_있어도_없어도_된다()
    {
        CommandTextParseResult withBrackets = CommandText.Parse("<<fade_out c1>>", Catalog);
        CommandTextParseResult without = CommandText.Parse("fade_out c1", Catalog);

        Assert.True(withBrackets.Success);
        Assert.True(without.Success);
        Assert.Equal(withBrackets.Definition!.Id, without.Definition!.Id);
        Assert.Equal(withBrackets.Arguments, without.Arguments);
    }

    [Fact]
    public void 병기_텍스트는_이미터처럼_기본값을_명시한다()
    {
        // cast의 variantKey(a)·emotionKey(1)는 기본값이 채워져 명시 출력된다 —
        // 이미터가 쓰는 규칙(기본값 의존을 남기지 않는 명시 출력) 그대로다.
        // 병기 텍스트가 이미터와 다른 문장을 만들면 화면과 파일이 다른 이야기를 한다.
        Assert.Equal("<<cast c1 laru a 1>>", RoundTrip("cast c1 laru"));
        Assert.Equal("<<cast c1 laru b 1>>", RoundTrip("cast c1 laru b"));
    }

    [Fact]
    public void 미지의_커맨드는_보정_없이_즉시_오류다()
    {
        CommandTextParseResult parsed = CommandText.Parse("<<face_swop c1 5>>", Catalog);

        Assert.False(parsed.Success);
        Assert.Contains("face_swop", parsed.Error, StringComparison.Ordinal);
        // 비슷한 이름을 추측해 주지 않는다.
        Assert.DoesNotContain("face_swap", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void 인자_초과는_오류다()
    {
        CommandTextParseResult parsed = CommandText.Parse("<<fade_out c1 10fr 남는것>>", Catalog);

        Assert.False(parsed.Success);
        Assert.Contains("최대", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void 숫자_타입_불일치는_오류다()
    {
        // char_flip_horizontal의 angle은 int다.
        CommandTextParseResult parsed = CommandText.Parse("<<char_flip_horizontal c1 넘김>>", Catalog);

        Assert.False(parsed.Success);
        Assert.Contains("정수", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void 필수_인자_누락은_오류다()
    {
        CommandTextParseResult parsed = CommandText.Parse("<<face_swap c1>>", Catalog);

        Assert.False(parsed.Success);
        Assert.Contains("emotion", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void 같은_outputCommand가_여럿이면_카탈로그_첫_정의로_결정된다()
    {
        // 정의 Id로도 정확히 지목할 수 있다.
        CommandTextParseResult byId = CommandText.Parse("char_rig_acting.hop c1", Catalog);
        Assert.True(byId.Success);
        Assert.Equal("char_rig_acting.hop", byId.Definition!.Id);

        CommandTextParseResult byOutput = CommandText.Parse("hop c1", Catalog);
        Assert.True(byOutput.Success);
        Assert.Equal("char_rig_acting.hop", byOutput.Definition!.Id);
    }

    [Fact]
    public void 빈_입력은_오류다()
    {
        Assert.False(CommandText.Parse("   ", Catalog).Success);
        Assert.False(CommandText.Parse("<<>>", Catalog).Success);
    }

    // ── 인자 편집·최근 사용 (편집 명령 수준) ──────────────────────────────

    private static (ProjectEditor Editor, PresentationNode Node) BuildEditor()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_w19", "테스트", "story/w19.vnstory.json");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);
        PresentationNode node = editor.AddPresentationNode(file.Id, name: "연출");
        return (editor, node);
    }

    [Fact]
    public void 인자_하나를_바꾸고_빈_값이면_기본값으로_돌아간다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();
        PresentationCommandInstance command = editor.AddPresentationSetupCommand(
            node.Id, "char_rig_presentation.fade_in", new Dictionary<string, string> { ["slot"] = "c1" });

        editor.SetPresentationCommandArgument(node.Id, command.Id, "duration", "4fr");
        Assert.Equal("4fr", command.Arguments["duration"]);

        editor.SetPresentationCommandArgument(node.Id, command.Id, "duration", null);
        Assert.False(command.Arguments.ContainsKey("duration")); // 카탈로그 기본값으로

        editor.Undo();
        Assert.Equal("4fr", editor.Project
            .FindPresentation(node.Id)!.SetupCommands.Single().Arguments["duration"]);
    }

    [Fact]
    public void 추가한_커맨드는_최근_사용에_최신순으로_쌓인다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();

        editor.AddPresentationSetupCommand(node.Id, "background.bg_spawn");
        editor.AddPresentationSetupCommand(node.Id, "char_rig_cast.slot");
        editor.AddPresentationSetupCommand(node.Id, "background.bg_spawn"); // 재사용 → 맨 앞, 중복 없음

        Assert.Equal(
            ["background.bg_spawn", "char_rig_cast.slot"],
            editor.Project.RecentCommandIds);

        for (int i = 0; i < 10; i++)
        {
            editor.AddPresentationSetupCommand(node.Id, $"cmd.{i}");
        }

        Assert.Equal(StoryProject.MaxRecentCommands, editor.Project.RecentCommandIds.Count);
    }

    [Fact]
    public void 최근_사용이_manifest와_스냅샷을_왕복한다()
    {
        var project = new StoryProject();
        project.RecentCommandIds.AddRange(["a.one", "b.two"]);

        ProjectManifest manifest = ProjectManifestJson.Read(ProjectManifestJson.Write(project));
        Assert.Equal(["a.one", "b.two"], manifest.RecentCommandIds);

        StoryProject decoded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(project));
        Assert.Equal(["a.one", "b.two"], decoded.RecentCommandIds);

        // 비어 있으면 키 자체가 없다 — 기존 프로젝트 파일이 바뀌지 않는다.
        Assert.DoesNotContain("recentCommands", ProjectManifestJson.Write(new StoryProject()), StringComparison.Ordinal);
    }
}
