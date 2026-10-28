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
        // frame_wait의 n은 int다 — W65 이후 카탈로그에 남은 유일한 int 인자다.
        // (출력이 <<Nfr>> 형태인 합성 항목이라 정의 Id로 지목한다.)
        CommandTextParseResult parsed = CommandText.Parse("control.frame_wait 넘김", Catalog);

        Assert.False(parsed.Success);
        Assert.Contains("정수", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ease_인자는_왕복하고_미지정이면_생략된다()
    {
        // W67 — 다섯째 인자. 지정하면 왕복하고, 없으면 텍스트가 기존 그대로다(최소 diff).
        CommandTextParseResult withEase = CommandText.Parse("<<move_by c1 +2u 0u 12fr Linear>>", Catalog);
        Assert.True(withEase.Success);
        Assert.Equal("Linear", withEase.Arguments!["ease"]);
        Assert.Equal(
            "<<move_by c1 +2u 0u 12fr Linear>>",
            CommandText.Format(withEase.Definition, "move_by", withEase.Arguments!));

        CommandTextParseResult without = CommandText.Parse("<<move_by c1 +2u 0u 12fr>>", Catalog);
        Assert.True(without.Success);
        Assert.False(without.Arguments!.ContainsKey("ease"));
        Assert.Equal(
            "<<move_by c1 +2u 0u 12fr>>",
            CommandText.Format(without.Definition, "move_by", without.Arguments!));
    }

    [Fact]
    public void 잘못된_ease_이름은_오류다()
    {
        // 런타임은 모르는 이름을 로그만 남기고 OutCubic으로 굴러간다 — 오타는 저작에서
        // 짚는 것이 유일한 방어다. 판정은 런타임 YarnEaseParser와 같다(대소문자 무시·숫자 거부).
        Assert.True(CommandText.Parse("<<move_by c1 +2u 0u 12fr outcubic>>", Catalog).Success);

        CommandTextParseResult typo = CommandText.Parse("<<move_by c1 +2u 0u 12fr OutQubic>>", Catalog);
        Assert.False(typo.Success);
        Assert.Contains("이징", typo.Error, StringComparison.Ordinal);

        CommandTextParseResult numeric = CommandText.Parse("<<move_by c1 +2u 0u 12fr 5>>", Catalog);
        Assert.False(numeric.Success);
        Assert.Contains("이징", numeric.Error, StringComparison.Ordinal);
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
        // 정의 Id로도, outputCommand로도 같은 정의를 지목한다.
        CommandTextParseResult byId = CommandText.Parse("char_rig_staging.sibling_front c1", Catalog);
        Assert.True(byId.Success);
        Assert.Equal("char_rig_staging.sibling_front", byId.Definition!.Id);

        CommandTextParseResult byOutput = CommandText.Parse("sibling_front c1", Catalog);
        Assert.True(byOutput.Success);
        Assert.Equal("char_rig_staging.sibling_front", byOutput.Definition!.Id);
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

    [Fact]
    public void 중간이_빈_인자는_자리표로_메워_자리가_안_밀린다()
    {
        // ⚠ 2026-08-21 `gesture`가 드러낸 결함의 고정. 예전에는 값 없는 파라미터에서
        // 끊어서, 뒤에 값이 있으면 그 인자가 앞 자리로 밀렸다 — yEase만 적었는데
        // <<gesture c1 0u 1u 24fr @bump>>가 나와 @bump가 가로 곡선이 됐다
        // (세로로 흔들려던 것이 좌우로 흔들린다).
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        string text = CommandText.Format(
            catalog.Find("char_rig_staging.gesture"), "char_rig_staging.gesture",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["slot"] = "c1", ["yAmp"] = "1u", ["duration"] = "24fr", ["yEase"] = "@bump"
            });

        Assert.Equal("<<gesture c1 0u 1u 24fr \"\" @bump>>", text);

        // 되읽어도 같은 자리다 — 자리표는 "안 적은 것"과 같게 받힌다.
        CommandTextParseResult parsed = CommandText.Parse(text, catalog);
        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal("@bump", parsed.Arguments!["yEase"]);

        // 트레일링 생략은 그대로다 — 뒤에 값이 없으면 자리표도 안 붙는다.
        Assert.Equal(
            "<<gesture c1 0.3u 0u 12fr>>",
            CommandText.Format(
                catalog.Find("char_rig_staging.gesture"), "char_rig_staging.gesture",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["slot"] = "c1", ["xAmp"] = "0.3u", ["duration"] = "12fr"
                }));
    }
}
