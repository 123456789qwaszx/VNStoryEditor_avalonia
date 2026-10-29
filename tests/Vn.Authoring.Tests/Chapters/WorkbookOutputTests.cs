using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>프로젝트의 챕터·대본 → 워크북</b> (R-F · 2026-09-16, 지시서 §4·§5.3 · 대본은 2026-10-29 C5부터 같은 조정자).
///
/// ⛔ 못 낸 것은 <b>실패가 아니라 미룸이다</b>. 원본이 프로젝트이므로 값은 이미 안전하고,
/// 못 한 것은 그 파일을 <em>지금</em> 쓰는 일뿐이다 — 잠금의 뜻이 <i>"막는다"</i>에서
/// <i>"그 파일을 지금 갱신하지 못했다"</i>로 바뀐 자리다.
/// </summary>
public sealed class WorkbookOutputTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-output", Guid.NewGuid().ToString("N"));

    private readonly WorkbookOutput _output = new();
    private readonly ProjectEditor _editor = new(new StoryProject());

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    private string PathFor(string chapterId) =>
        Path.Combine(_directory, ChapterLibrary.FolderName, chapterId + ".xlsx");

    private string ScriptPathFor(string chapterId, string episodeId) =>
        Path.Combine(_directory, EpisodeLibrary.FolderName, chapterId, episodeId + ".xlsx");

    /// <summary>ch01의 ep01 카드(생성자가 세웠다)에 대사 한 줄을 적는다 — 대본 워크북이 나올 근거.</summary>
    private DialogueNode CardWithLine(string speaker = "윌로", string text = "복도는 조용했다")
    {
        DialogueNode card = EpisodeNaming.CardFor(_editor.Project, "ch01", "ep01")!;
        ScriptDocument script = _editor.Project.FindScript(card.ScriptId)!;
        _editor.SetScriptLineText(script.Id, script.ActiveLines.First().Id, speaker, text);

        return card;
    }

    private static string FirstText(string path) => EpisodeWorkbookReader.Read(path).Rows
        .First(row => row.IsLine && !row.IsBlank)
        .Text;

    public WorkbookOutputTests()
    {
        Directory.CreateDirectory(_directory);
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "챕터를 낸다" });

        _editor.EnsureChapter("ch01");
        _editor.AddEpisode("ch01", "ep01", "복도", 0, 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 내면_워크북이_프로젝트를_그대로_담는다()
    {
        // 뒤집기의 도착점 — 툴에 쓴 것이 엑셀을 채운다.
        _editor.AddEpisode("ch01", "ep02", "옥상", 200, 0);
        _editor.AddEdge("ch01", "ep01", "ep02", optionLabel: "올라간다");

        WorkbookEmitRun run = _output.Emit(_editor.Project, ManifestPath, "ch01");

        Assert.True(run.AllWritten);
        Assert.Equal(string.Empty, run.Notice());   // 잘된 일은 조용하다

        ChapterGraphModel written = ChapterWorkbookReader.Read(PathFor("ch01"));

        Assert.Equal(["ep01", "ep02"], written.Episodes.Select(episode => episode.EpisodeId));
        Assert.Equal(["올라간다"], written.Edges.Select(edge => edge.OptionLabel));
    }

    [Fact]
    public void 저장은_모든_챕터를_낸다()
    {
        // ⛔ <b>낡음을 막는 그물이다</b> (§6.2: "저장 = 프로젝트에 저장 + 워크북 재출력").
        //    편집마다 내는 것은 빠른 길이고, 이쪽은 <b>어느 길로 고쳤든</b> 따라오게 한다 —
        //    화면을 지나지 않는 편집(되돌리기)이 실제 사례다.
        _editor.EnsureChapter("ch02");
        _editor.AddEpisode("ch02", "둘01", "둘", 0, 0);

        WorkbookEmitRun run = _output.EmitAll(_editor.Project, ManifestPath);

        // 챕터마다 그 판의 대본까지 — 에피소드를 더하면 카드·대본이 함께 서므로 대본 대상도 선다.
        Assert.Equal(["ch01", "ch01/ep01", "ch02", "ch02/둘01"], run.Written);
        Assert.True(File.Exists(PathFor("ch01")));
        Assert.True(File.Exists(PathFor("ch02")));
        Assert.True(File.Exists(ScriptPathFor("ch02", "둘01")));
    }

    [Fact]
    public void 되돌려도_저장이_파일을_따라잡게_한다()
    {
        // ⚠ 되돌리기는 화면의 편집 창구를 지나지 않는다 — 그래서 편집마다 내는 길만으로는
        //    워크북이 되돌리기 <em>전</em> 상태로 남았다. 저장이 그것을 메운다.
        _editor.AddEpisode("ch01", "잘못더한것", "실수", 0, 0);
        _output.Emit(_editor.Project, ManifestPath, "ch01");

        Assert.Contains("잘못더한것",
            ChapterWorkbookReader.Read(PathFor("ch01")).Episodes.Select(episode => episode.EpisodeId));

        _editor.Undo();
        _output.EmitAll(_editor.Project, ManifestPath);

        Assert.DoesNotContain("잘못더한것",
            ChapterWorkbookReader.Read(PathFor("ch01")).Episodes.Select(episode => episode.EpisodeId));
    }

    [Fact]
    public void 엑셀이_잡고_있으면_미뤄_두었다가_풀리면_다시_낸다()
    {
        // ⛔ <b>§5.3의 마지막 조각이다</b> — "미룬 것을 잊지 않는다. 실패한 출력 대상을
        //    목록으로 들고, 잠금이 풀리면 다시 낸다."
        _output.Emit(_editor.Project, ManifestPath, "ch01");   // 파일을 먼저 만들어 둔다
        _editor.AddEpisode("ch01", "잠긴동안", "붙들린 사이", 0, 0);

        WorkbookEmitRun deferred;

        using (new FileStream(PathFor("ch01"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            deferred = _output.Emit(_editor.Project, ManifestPath, "ch01");

            Assert.False(deferred.AllWritten);
            Assert.Contains("ch01", _output.Pending);

            // 사람에게 닿는다 — 조용한 실패가 최악이다.
            Assert.Contains("⚠", deferred.Notice());
        }

        // 값은 프로젝트에 이미 있었다 — 못 한 것은 파일을 <b>지금</b> 쓰는 일뿐이다.
        Assert.Contains("잠긴동안",
            _editor.FindChapter("ch01")!.Episodes.Select(episode => episode.EpisodeId));

        // 엑셀이 놓았다 — 사람이 아무것도 안 눌러도 파일이 프로젝트를 따라잡는다.
        WorkbookEmitRun caught = _output.Retry(_editor.Project, ManifestPath);

        Assert.Equal(["ch01"], caught.Written);
        Assert.Empty(_output.Pending);

        Assert.Contains("잠긴동안",
            ChapterWorkbookReader.Read(PathFor("ch01")).Episodes.Select(episode => episode.EpisodeId));
    }

    [Fact]
    public void 미룬_것이_없으면_다시_내지_않는다()
    {
        // 잠금이 움직일 때마다 파일을 여는 것은 그 자체가 값이다 — 낼 것이 없으면 안 연다.
        _output.Emit(_editor.Project, ManifestPath, "ch01");

        Assert.Empty(_output.Retry(_editor.Project, ManifestPath).Written);
    }

    // ── 대본도 같은 조정자다 (C5 · 2026-10-29) ───────────────────────────────

    [Fact]
    public void 대본은_카드_하나씩_낸다()
    {
        // 2026-10-29까지 대본은 대본 탭이 저장 직후 직접 썼다 — 못 낸 것을 기억하지 못했고
        // 저장도 다시 내지 않았다. 이제 챕터와 같은 길이다.
        DialogueNode card = CardWithLine();

        WorkbookEmitRun run = _output.EmitScript(_editor.Project, ManifestPath, card);

        Assert.True(run.AllWritten);
        Assert.Equal([WorkbookOutput.ScriptTarget("ch01", "ep01")], run.Written);
        Assert.Equal("복도는 조용했다", FirstText(ScriptPathFor("ch01", "ep01")));
    }

    [Fact]
    public void 챕터를_내는_것은_대본을_내지_않는다()
    {
        // 간선 하나 고칠 때마다 그 챕터의 대본을 전부 열어 보지 않는다 — 대본은 제 길(EmitScript)과
        // 저장(EmitAll)으로 나간다.
        CardWithLine();

        WorkbookEmitRun run = _output.Emit(_editor.Project, ManifestPath, "ch01");

        Assert.Equal(["ch01"], run.Written);
        Assert.False(File.Exists(ScriptPathFor("ch01", "ep01")));
    }

    [Fact]
    public void 저장은_대본도_전부_낸다()
    {
        // 화면을 지나지 않는 편집(되돌리기·화자 개명)에서 대본 워크북이 낡지 않게 하는 그물.
        DialogueNode card = CardWithLine();
        _output.EmitAll(_editor.Project, ManifestPath);

        ScriptDocument script = _editor.Project.FindScript(card.ScriptId)!;
        _editor.SetScriptLineText(script.Id, script.ActiveLines.First().Id, "윌로", "고쳐 쓴 줄");

        WorkbookEmitRun run = _output.EmitAll(_editor.Project, ManifestPath);

        Assert.Contains(WorkbookOutput.ScriptTarget("ch01", "ep01"), run.Written);
        Assert.Equal("고쳐 쓴 줄", FirstText(ScriptPathFor("ch01", "ep01")));
    }

    [Fact]
    public void 엑셀이_대본을_잡고_있으면_미뤄_두었다가_풀리면_다시_낸다()
    {
        DialogueNode card = CardWithLine();
        _output.EmitScript(_editor.Project, ManifestPath, card);   // 파일을 먼저 만들어 둔다

        ScriptDocument script = _editor.Project.FindScript(card.ScriptId)!;
        _editor.SetScriptLineText(script.Id, script.ActiveLines.First().Id, "윌로", "붙들린 사이에 쓴 줄");

        string target = WorkbookOutput.ScriptTarget("ch01", "ep01");
        WorkbookEmitRun deferred;

        using (new FileStream(ScriptPathFor("ch01", "ep01"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            deferred = _output.EmitScript(_editor.Project, ManifestPath, card);

            Assert.False(deferred.AllWritten);
            Assert.Contains(target, _output.Pending);
            Assert.Contains("⚠", deferred.Notice());
        }

        WorkbookEmitRun caught = _output.Retry(_editor.Project, ManifestPath);

        Assert.Equal([target], caught.Written);
        Assert.Empty(_output.Pending);
        Assert.Equal("붙들린 사이에 쓴 줄", FirstText(ScriptPathFor("ch01", "ep01")));
    }

    [Fact]
    public void 같은_글의_대본은_다시_쓰지_않는다()
    {
        // 저장마다 모든 대본을 다시 내므로, 안 바뀐 파일까지 두드리면 .bak이 굴러가고 감시가
        // 깨어나고 클라우드 동기화가 매번 깨어난다 — 진행 JSON과 같은 규칙이다.
        CardWithLine();
        _output.EmitAll(_editor.Project, ManifestPath);

        string path = ScriptPathFor("ch01", "ep01");
        byte[] first = File.ReadAllBytes(path);

        _output.EmitAll(_editor.Project, ManifestPath);

        Assert.Equal(first, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void 챕터_아닌_판의_카드는_낼_대본이_없다()
    {
        // 작가의 낙서판 — 진행에 안 실리고 워크북도 없다.
        string fileId = _editor.AddStoryFile("낙서").Id;
        DialogueNode card = _editor.AddDialogueNode(fileId, name: "메모");

        WorkbookEmitRun run = _output.EmitScript(_editor.Project, ManifestPath, card);

        Assert.Empty(run.Written);
        Assert.Empty(run.Deferred);
    }

    [Fact]
    public void 지워진_카드의_미룬_출력은_잊는다()
    {
        DialogueNode card = CardWithLine();
        _output.EmitScript(_editor.Project, ManifestPath, card);

        using (new FileStream(ScriptPathFor("ch01", "ep01"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ScriptDocument script = _editor.Project.FindScript(card.ScriptId)!;
            _editor.SetScriptLineText(script.Id, script.ActiveLines.First().Id, "윌로", "바뀐 줄");
            _output.EmitScript(_editor.Project, ManifestPath, card);
        }

        Assert.NotEmpty(_output.Pending);

        _editor.RemoveNode(card.Id);
        _output.Retry(_editor.Project, ManifestPath);

        Assert.Empty(_output.Pending);
    }

    [Fact]
    public void 저장_안_한_프로젝트에는_낼_자리가_없다()
    {
        // 미루는 것도 아니다 — 잠긴 게 아니라 아직 폴더가 안 정해진 것이다.
        WorkbookEmitRun run = _output.EmitAll(_editor.Project, projectManifestPath: null);

        Assert.Empty(run.Written);
        Assert.Empty(run.Deferred);
        Assert.Empty(_output.Pending);
    }
}
