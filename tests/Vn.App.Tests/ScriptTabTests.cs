using Avalonia.Controls;
using Avalonia.VisualTree;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;
using Path = System.IO.Path;

namespace Vn.App.Tests;

/// <summary>
/// <b>[대본] 탭 — 작가의 자리</b> (R-E · 지시서 §6).
///
/// 못 박는 것: ① 챕터 → 에피소드를 고르면 그 글만 보인다 ② 고쳐 반영하면 <b>줄의 신원이
/// 보존된다</b> ③ 지우기는 두 번 눌러야 한다 ④ 화면에 `#line:` 태그가 안 보인다.
///
/// ⚠ ②가 이 탭의 존재 이유다. 작가가 글을 고칠 때마다 신원이 갈리면 그 줄에 매달린
/// 연출·세이브가 통째로 끊긴다 — 그래서 <b>전량 재생성이 아니라 diff</b>여야 한다.
/// </summary>
public sealed class ScriptTabTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-script-tab", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    public ScriptTabTests()
    {
        Directory.CreateDirectory(_directory);
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "작가의 자리" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 챕터와_에피소드를_고르면_그_글이_보인다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "복도는 조용했다."), ("라루", "같이 갈까?"));

        var box = view.FindControl<TextBox>("ScriptBox")!;

        // ⛔ 2026-09-16에 목록 둘이 트리 하나가 됐다 (R6 S-2) — 챕터와 에피소드 사이에
        //    장면이 설 자리를 내려고. 구조는 줄 목록으로 잰다(`docs/plans/R6-explorer.md` §8).
        Assert.Equal(["ch01"], Rows(view, SceneTreeRowKind.Chapter));
        Assert.Equal(["ep01"], Rows(view, SceneTreeRowKind.Episode));

        Assert.Contains("윌로: 복도는 조용했다.", box.Text!);
        Assert.Contains("라루: 같이 갈까?", box.Text!);
    });

    [Fact]
    public void 화면에_LineId_태그가_보이지_않는다() => HeadlessUi.Run(() =>
    {
        // §6.3 — `#line:` 태그가 보이면 지저분하고 작가가 지운다. 신원은 diff가 붙든다.
        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "한 줄"));

        Assert.DoesNotContain("#line:", view.FindControl<TextBox>("ScriptBox")!.Text!);
    });

    [Fact]
    public void 고쳐_반영해도_줄의_신원이_보존된다() => HeadlessUi.Run(() =>
    {
        // ⛔ 이 탭의 존재 이유. 신원이 갈리면 그 줄에 매달린 연출이 통째로 끊긴다.
        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "복도는 조용했다."));

        string before = LineIds(session, node).Single();

        var box = view.FindControl<TextBox>("ScriptBox")!;
        box.Text = "윌로: 복도는 조용하지 않았다.";
        Click(view, "ApplyButton");

        Assert.Equal(["복도는 조용하지 않았다."], Texts(session, node));
        Assert.Equal(before, LineIds(session, node).Single());
    });

    [Fact]
    public void 줄을_더하면_그_줄만_새로_선다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        string before = LineIds(session, node).Single();

        var box = view.FindControl<TextBox>("ScriptBox")!;
        box.Text = "윌로: 첫 줄\n라루: 새로 쓴 줄";
        Click(view, "ApplyButton");

        Assert.Equal(["첫 줄", "새로 쓴 줄"], Texts(session, node));

        // 앞 줄은 제 신원을 그대로 들고 있다 — 새 줄만 새 신원을 받았다.
        Assert.Equal(before, LineIds(session, node)[0]);
        Assert.NotEqual(before, LineIds(session, node)[1]);
    });

    [Fact]
    public void 지우기는_한_번_더_눌러야_지운다() => HeadlessUi.Run(() =>
    {
        // ⚠ 붙여넣기 한 번이 줄을 통째로 날릴 수 있고, 그 줄에는 연출이 매달려 있다.
        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "첫 줄"), ("라루", "둘째 줄"));

        var box = view.FindControl<TextBox>("ScriptBox")!;
        box.Text = "윌로: 첫 줄";

        Click(view, "ApplyButton");

        // 첫 번째는 묻기만 한다 — 둘째 줄이 아직 살아 있다.
        Assert.Equal(["첫 줄", "둘째 줄"], Texts(session, node));
        Assert.Contains("한 번 더", session.StatusMessage);

        Click(view, "ApplyButton");

        Assert.Equal(["첫 줄"], Texts(session, node));
    });

    // ── 저장은 Ctrl+S 하나다 (2026-09-17 소유자) ──────────────────────────

    [Fact]
    public void 저장이_입력칸의_글을_먼저_넣는다() => HeadlessUi.Run(() =>
    {
        // ⛔ <b>여기가 조용한 사고였다.</b> 입력칸에 글을 쓰고 Ctrl+S를 누르면 상태줄은
        //    저장했다고 말하는데, 글은 `ApplyScenarioText`를 안 지났으므로 프로젝트에
        //    <b>없었다</b> — 저장은 프로젝트만 쓴다. 껍데기가 저장 전에 부르는 것이
        //    이 함수이고, 그것이 없던 것이 문제였다.
        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        view.FindControl<TextBox>("ScriptBox")!.Text = "윌로: 첫 줄\n라루: 저장으로 들어간 줄";

        Assert.True(view.HasUnsavedText);

        Assert.Equal(ScriptSaveOutcome.Saved, view.SaveText());

        Assert.Equal(["첫 줄", "저장으로 들어간 줄"], Texts(session, node));
        Assert.False(view.HasUnsavedText);
    });

    [Fact]
    public void 고친_데가_없으면_저장이_지나간다() => HeadlessUi.Run(() =>
    {
        // 저장마다 같은 글을 되넣으면 되돌리기가 그것으로 차고, 워크북이 매번 다시 난다.
        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        Assert.False(view.HasUnsavedText);
        Assert.Equal(ScriptSaveOutcome.Nothing, view.SaveText());
    });

    [Fact]
    public void 안_들어간_글은_다시_그리기가_덮지_않는다() => HeadlessUi.Run(() =>
    {
        // ⛔ 자유롭게 고치는 구조에서 <b>제일 잃기 쉬운 자리</b>다. 예전에는 포커스만 보고
        //    "떠났으면 넣었겠지"로 덮었는데, 저장이 엔터·단추에 묶여 있을 때만 맞는 가정이다.
        //    쓰다 말고 마우스를 옮기는 것은 정상이고, 그 사이 다른 탭의 변경 알림 한 번이면
        //    초고가 사라졌다.
        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        var box = view.FindControl<TextBox>("ScriptBox")!;
        box.Text = "윌로: 아직 저장 안 한 초고";

        // 다른 탭이 프로젝트를 건드린 것과 같다 — 세션이 알림을 낸다.
        session.Editor.AddScript("남의 대본");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("윌로: 아직 저장 안 한 초고", box.Text);
        Assert.True(view.HasUnsavedText);
    });

    [Fact]
    public void 지우기_확인_중에는_저장이_아직_안_끝났다고_말한다() => HeadlessUi.Run(() =>
    {
        // 두 번 누르기 규율은 그대로다 — 다만 "누르기"가 이제 "저장"이다.
        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "첫 줄"), ("라루", "둘째 줄"));

        view.FindControl<TextBox>("ScriptBox")!.Text = "윌로: 첫 줄";

        Assert.Equal(ScriptSaveOutcome.NeedsConfirmation, view.SaveText());
        Assert.Equal(["첫 줄", "둘째 줄"], Texts(session, node));

        // ⚠ 아직 안 들어갔으므로 <b>여전히 안 저장된 상태</b>다 — 제목의 *가 이것을 본다.
        Assert.True(view.HasUnsavedText);

        Assert.Equal(ScriptSaveOutcome.Saved, view.SaveText());
        Assert.Equal(["첫 줄"], Texts(session, node));
        Assert.False(view.HasUnsavedText);
    });

    [Fact]
    public void 판에서_노드를_지우면_쓴_글은_고아로_남고_화면은_그것을_안_말한다() => HeadlessUi.Run(() =>
    {
        // ⚠ <b>지금 동작을 그대로 못 박는 테스트다</b> (2026-09-17 조사). 옳다고 주장하는
        //    것이 아니라, 무엇이 실제로 일어나는지를 글이 아니라 코드로 남긴다.
        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "잃으면 안 되는 줄"));
        string scriptId = node.ScriptId!;

        // [연출 그래프]에서 카드를 지운 것과 같다.
        session.Editor.RemoveNode(node.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // ① 에피소드는 살아 있다 — 트리의 원천은 챕터이므로 줄이 남는 것은 <b>맞다</b>.
        Assert.Contains("ep01", session.Editor.FindChapter("ch01")!.Episodes.Select(e => e.EpisodeId));

        // ② 글은 <b>지워지지 않았다</b> — 고아 대본으로 프로젝트에 그대로 있다.
        Assert.NotNull(session.Project.FindScript(scriptId));

        // ③ ⛔ 그런데 화면은 "아직"이라고 말한다 — 쓴 적이 없다는 뜻이다.
        Assert.Contains("아직 빈 대본", view.FindControl<TextBlock>("EmptyText")!.Text!);

        // ④ ⛔ 그리고 [＋ 대본]은 <b>새 대본</b>을 만든다 — 고아를 되찾지 않는다.
        Click(view, "EmptyAddScriptButton");

        DialogueNode rebuilt = session.Project.EnumerateNodes().OfType<DialogueNode>()
            .Single(item => EpisodeNaming.EpisodeIdOf(item) == "ep01");

        Assert.NotEqual(scriptId, rebuilt.ScriptId);
        Assert.Empty(Texts(session, rebuilt));
    });

    // ── 툴에 쓴 것이 엑셀을 채운다 (§6.2) ──────────────────────────────────

    [Fact]
    public void 반영하면_대본_워크북이_다시_나온다() => HeadlessUi.Run(() =>
    {
        // ⛔ <b>뒤집기의 도착점이다.</b> 지시서의 한 문장 — "엑셀에 쓴 것이 툴에 반영되는
        //    것이 아니라, 툴에 쓴 것이 엑셀을 채운다" — 에서 '채운다'가 여기다.
        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "첫 줄"), ("라루", "둘째 줄"));

        // 한 줄은 그대로 두고 한 줄만 고친다 — 지우기가 아니므로 한 번에 반영된다.
        view.FindControl<TextBox>("ScriptBox")!.Text = "윌로: 첫 줄\n라루: 툴에서 고친 대사";
        Click(view, "ApplyButton");

        EpisodeWorkbookModel written = EpisodeWorkbookReader.Read(WorkbookPath("ch01", "ep01"));

        Assert.Empty(written.Errors);
        Assert.Equal(["첫 줄", "툴에서 고친 대사"], written.Rows.Select(row => row.Text));
        Assert.Equal(["윌로", "라루"], written.Rows.Select(row => row.Speaker));

        // 인덱스는 출력 열이라 10·20으로 새로 매겨진다 (§4.2) — 신원은 프로젝트가 갖는다.
        Assert.Equal([10, 20], written.Rows.Select(row => row.Index));
    });

    [Fact]
    public void 화자가_빈_줄은_지문으로_나간다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        view.FindControl<TextBox>("ScriptBox")!.Text = "문이 열렸다.";
        Click(view, "ApplyButton");

        EpisodeWorkbookModel written = EpisodeWorkbookReader.Read(WorkbookPath("ch01", "ep01"));

        Assert.Equal(["문이 열렸다."], written.Rows.Select(row => row.Text));
        Assert.Equal([string.Empty], written.Rows.Select(row => row.Speaker));
    });

    [Fact]
    public void 엑셀이_잡고_있으면_글은_남고_출력만_미뤄진다() => HeadlessUi.Run(() =>
    {
        // §5.3 — 잠금의 뜻이 "막는다"가 아니라 "그 파일을 지금 갱신하지 못했다"로 바뀌었다.
        //    원본은 프로젝트라 글은 이미 안전하다.
        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        // 한 번 내서 파일을 만들어 두고, 그것을 엑셀처럼 붙든다.
        view.FindControl<TextBox>("ScriptBox")!.Text = "윌로: 첫 줄";
        Click(view, "ApplyButton");

        using (new FileStream(
                   WorkbookPath("ch01", "ep01"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            view.FindControl<TextBox>("ScriptBox")!.Text = "윌로: 붙들린 동안 쓴 글";
            Click(view, "ApplyButton");
        }

        // 글은 프로젝트에 남았다.
        Assert.Equal(["붙들린 동안 쓴 글"], Texts(session, node));

        // 그리고 못 냈다는 사실이 사람에게 닿는다 — 조용한 실패가 최악이다.
        Assert.Contains("⚠", session.StatusMessage);
    });

    // ── 빈 자리의 사다리 (§6.2 · 챕터 그래프 2026-08-26의 규율을 잇는다) ────
    //
    // 새 프로젝트 → ＋챕터 → ＋에피소드 → <b>＋대본</b>. 앞의 셋은 [챕터 그래프]가 세웠고,
    // 마지막 칸이 여기다. 안내문만 서 있으면 다음 할 일이 글로만 남는다.

    [Fact]
    public void 노드가_없는_에피소드도_목록에_선다() => HeadlessUi.Run(() =>
    {
        // ⛔ 노드 있는 것만 보이면 <b>아직 아무도 안 쓴 에피소드에는 글을 쓸 자리가 없다</b> —
        //    글을 쓰면 노드가 생기는데, 쓰려면 노드가 있어야 하는 매듭이 된다.
        WriteChapter("ch01", "ep01", "ep02");

        (ScriptView view, _) = Show();

        Assert.Equal(["ep01", "ep02"], Rows(view, SceneTreeRowKind.Episode));
    });

    [Fact]
    public void 빈_에피소드에는_가운데에_대본_단추가_선다() => HeadlessUi.Run(() =>
    {
        // 빈 입력칸만 보이면 작가는 그것이 고장인지 빈 대본인지 못 가린다 — 누를 자리가
        // 판 한가운데 함께 선다(챕터 그래프의 [＋ 에피소드]와 같은 모양이다).
        WriteChapter("ch01", "ep01");

        (ScriptView view, _) = Show();

        Assert.True(view.FindControl<Border>("EmptyPanel")!.IsVisible);
        Assert.True(view.FindControl<Button>("EmptyAddScriptButton")!.IsVisible);
        Assert.Contains("아직 빈 대본", view.FindControl<TextBlock>("EmptyText")!.Text!);

        // 세우기 전에는 반영할 것이 없다 — 단추 둘이 동시에 살아 있으면 어느 쪽이
        // 다음 걸음인지 흐려진다.
        Assert.False(view.FindControl<Button>("ApplyButton")!.IsEnabled);
    });

    [Fact]
    public void 대본_단추를_누르면_그때_노드가_선다() => HeadlessUi.Run(() =>
    {
        WriteChapter("ch01", "ep01");

        (ScriptView view, AuthoringSession session) = Show();

        // 누르기 전에는 판에 아무 노드도 없다 — 훑어보기만으로 빈 노드가 쌓이면 안 된다.
        Assert.Empty(session.Project.EnumerateNodes().OfType<DialogueNode>());

        Click(view, "EmptyAddScriptButton");

        DialogueNode created = Assert.Single(
            session.Project.EnumerateNodes().OfType<DialogueNode>());

        Assert.Equal("ep01", created.Name);

        // 사다리가 내려가고 쓸 자리가 열린다 — 노드 생성이 딸려 주는 빈 줄은 은퇴했다.
        Assert.False(view.FindControl<Border>("EmptyPanel")!.IsVisible);
        Assert.True(view.FindControl<Button>("ApplyButton")!.IsEnabled);
        Assert.Empty(session.Project.FindScript(created.ScriptId!)!.ActiveLines);

        view.FindControl<TextBox>("ScriptBox")!.Text = "윌로: 작가가 처음 쓴 줄";
        Click(view, "ApplyButton");

        Assert.Equal(["작가가 처음 쓴 줄"], Texts(session, created));

        // 그리고 워크북도 함께 나갔다 — 툴에 쓴 것이 엑셀을 채운다.
        Assert.Equal(
            ["작가가 처음 쓴 줄"],
            EpisodeWorkbookReader.Read(WorkbookPath("ch01", "ep01")).Rows.Select(row => row.Text));
    });

    [Fact]
    public void 쓰는_중에_노드가_사라져도_글은_살아_남는다() => HeadlessUi.Run(() =>
    {
        // ⚠ 글을 쓰는 동안에는 화면을 다시 그리지 않는다(타이핑이 씹히므로). 그래서 그 사이
        //    다른 탭에서 노드가 지워지면 사다리가 못 올라온다 — [글 반영]에도 길이 있어야
        //    쓰던 글을 잃지 않는다.
        WriteChapter("ch01", "ep01");

        (ScriptView view, AuthoringSession session) = Show();

        view.FindControl<TextBox>("ScriptBox")!.Text = "윌로: 사라진 사이에 쓴 줄";
        Click(view, "ApplyButton");

        DialogueNode rescued = Assert.Single(
            session.Project.EnumerateNodes().OfType<DialogueNode>());

        Assert.Equal(["사라진 사이에 쓴 줄"], Texts(session, rescued));
    });

    [Fact]
    public void 빈_글로는_노드를_만들지_않는다() => HeadlessUi.Run(() =>
    {
        // 잘못 누른 것까지 판에 남기지 않는다.
        //
        // ⚠ <b>공백을 친다</b> (2026-09-17). 아무것도 안 친 상태에서 저장하면 이제
        //    <see cref="ScriptSaveOutcome.Nothing"/>으로 조용히 지나간다 — Ctrl+S가 주된
        //    길이 된 뒤로, 고친 데도 없는데 매번 말하면 그것은 잔소리다. 잡아야 하는 것은
        //    <b>쓴 줄 알았는데 공백뿐인</b> 경우이고, 그쪽은 그대로 말한다.
        WriteChapter("ch01", "ep01");

        (ScriptView view, AuthoringSession session) = Show();

        view.FindControl<TextBox>("ScriptBox")!.Text = "   \n  ";
        Click(view, "ApplyButton");

        Assert.Empty(session.Project.EnumerateNodes().OfType<DialogueNode>());
        Assert.Contains("빈 글은", session.StatusMessage);
    });

    [Fact]
    public void 챕터가_없으면_어디로_가야_하는지_가리킨다() => HeadlessUi.Run(() =>
    {
        // ⚠ 여기에는 [＋ 챕터]를 세우지 않는다 — 챕터의 주인은 기획자의 엑셀이고 그 단추는
        //    [챕터 그래프]에 이미 있다. 같은 단추가 두 자리에 서면 어느 쪽이 진짜인지 묻게 된다.
        (ScriptView view, _) = Show();

        Assert.True(view.FindControl<Border>("EmptyPanel")!.IsVisible);
        Assert.False(view.FindControl<Button>("EmptyAddScriptButton")!.IsVisible);
        Assert.Contains("[챕터 그래프]", view.FindControl<TextBlock>("EmptyText")!.Text!);
    });

    [Fact]
    public void 에피소드가_없는_챕터도_어디로_가야_하는지_가리킨다() => HeadlessUi.Run(() =>
    {
        WriteChapter("ch01");

        (ScriptView view, _) = Show();

        Assert.False(view.FindControl<Button>("EmptyAddScriptButton")!.IsVisible);
        Assert.Contains("에피소드가 없습니다", view.FindControl<TextBlock>("EmptyText")!.Text!);
    });

    // ── 화자는 등록부에서 (§6.2) ────────────────────────────────────────────

    [Fact]
    public void 화자를_고르면_커서가_선_줄에_붙는다() => HeadlessUi.Run(() =>
    {
        // 칸이 아니라 글을 고치는 화면이라 콤보박스가 설 자리가 없다 — 드롭다운은
        // 커서가 선 줄에 이름을 붙여 준다. 손으로 쳐도 되지만, 그러면 오타가 곧 미등록이다.
        Register(("윌로", "willo"), ("라루", "laru"));

        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        var box = view.FindControl<TextBox>("ScriptBox")!;
        box.Text = "윌로: 첫 줄\n아직 화자가 없는 줄";
        box.CaretIndex = box.Text.Length;

        PickSpeaker(view, "라루");

        Assert.Equal("윌로: 첫 줄\n라루: 아직 화자가 없는 줄", box.Text);
    });

    [Fact]
    public void 이미_화자가_있는_줄이면_갈아_끼운다() => HeadlessUi.Run(() =>
    {
        // ⛔ 앞에 붙이기만 하면 "라루: 윌로: …"가 되고, 파서는 그것을 화자 '라루'에
        //    내용 "윌로: …"인 한 줄로 읽는다 — 조용히 망가지는 자리다.
        Register(("윌로", "willo"), ("라루", "laru"));

        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        var box = view.FindControl<TextBox>("ScriptBox")!;
        box.Text = "윌로: 첫 줄";
        box.CaretIndex = 3;

        PickSpeaker(view, "라루");

        Assert.Equal("라루: 첫 줄", box.Text);
    });

    [Fact]
    public void 콜론이_있어도_산문이면_화자로_보지_않는다() => HeadlessUi.Run(() =>
    {
        // 파서의 규칙과 같아야 한다 — 접두에 공백이 있으면 산문이다("그는 말했다: …").
        Register(("라루", "laru"));

        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        var box = view.FindControl<TextBox>("ScriptBox")!;
        box.Text = "그는 말했다: 다시는 오지 않겠다고";
        box.CaretIndex = 0;

        PickSpeaker(view, "라루");

        Assert.Equal("라루: 그는 말했다: 다시는 오지 않겠다고", box.Text);
    });

    [Fact]
    public void 미등록_화자는_오류가_아니라_표시된다() => HeadlessUi.Run(() =>
    {
        // §6.2 — 막지 않는다. 작가가 등록부보다 앞서 쓰는 것은 정상이고 등록은 기획자의
        // 일이다. 그래도 짚어는 준다: 미등록은 초상화가 안 붙는다는 뜻이라서다.
        Register(("윌로", "willo"));

        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        view.FindControl<TextBox>("ScriptBox")!.Text = "윌로: 첫 줄\n문지기: 어서 오시오";
        Click(view, "ApplyButton");

        // 반영은 됐다 — 오류가 아니다.
        Assert.Equal(["첫 줄", "어서 오시오"], Texts(session, node));

        var unknown = view.FindControl<TextBlock>("UnknownSpeakerText")!;

        Assert.True(unknown.IsVisible);
        Assert.Contains("문지기", unknown.Text!);
        Assert.DoesNotContain("윌로", unknown.Text!);

        // 붉은 문제 줄이 아니다 — 색이 뜻을 나른다.
        Assert.False(view.FindControl<TextBlock>("ProblemsText")!.IsVisible);
    });

    [Fact]
    public void 공백_있는_미등록_이름은_화자가_아니라_지문이_된다() => HeadlessUi.Run(() =>
    {
        // ⚠ <b>작가가 밟는 함정이다.</b> 파서는 접두에 공백이 있으면 산문으로 본다
        //    ("그는 말했다: …"를 화자로 삼지 않으려고) — 등록된 이름만 예외다. 그래서
        //    "늙은 상인: 어서 오시오"는 <b>화자 없는 한 줄</b>이 되고, 화자가 없으니
        //    [미등록] 표시도 안 뜬다. 조용하다는 것이 이 자리의 위험이다.
        //
        //    막지 않는 이유는 규칙이 파서의 것이고 저 산문 규칙에도 이유가 있어서다.
        //    작가에게 주는 답은 <b>[화자 ▾]로 고르는 것</b>이다 — 등록된 이름은 공백이
        //    있어도 화자로 읽힌다(아래 두 번째 대목).
        Register(("윌로", "willo"));

        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode node = Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        view.FindControl<TextBox>("ScriptBox")!.Text = "윌로: 첫 줄\n늙은 상인: 어서 오시오";
        Click(view, "ApplyButton");

        Assert.Equal(["첫 줄", "늙은 상인: 어서 오시오"], Texts(session, node));
        Assert.False(view.FindControl<TextBlock>("UnknownSpeakerText")!.IsVisible);

        // 등록만 해 두면 같은 글이 화자로 읽힌다 — 공백은 문제가 아니었다.
        // (실제 창구를 지난다 — 파일만 갈면 열려 있는 세션의 등록부는 안 바뀐다.)
        Assert.True(session.SaveSpeakers([
            new SpeakerSpec { Name = "윌로", CharacterId = "willo" },
            new SpeakerSpec { Name = "늙은 상인", CharacterId = "merchant" }
        ]));

        view.FindControl<TextBox>("ScriptBox")!.Text = "윌로: 첫 줄\n늙은 상인: 어서 오시오";
        Click(view, "ApplyButton");

        Assert.Equal(["첫 줄", "어서 오시오"], Texts(session, node));
    });

    [Fact]
    public void 전부_등록된_화자면_아무_말도_안_한다() => HeadlessUi.Run(() =>
    {
        // 잘된 일은 조용하다 — 늘 서 있는 안내문은 곧 아무도 안 읽는 안내문이다.
        Register(("윌로", "willo"));

        (ScriptView view, AuthoringSession session) = Show();
        Seed(session, "ch01", "ep01", ("윌로", "첫 줄"));

        Assert.False(view.FindControl<TextBlock>("UnknownSpeakerText")!.IsVisible);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 탐색기에 <b>보이는</b> 줄들 — 접힌 것은 안 나온다. 구조를 재는 자리다
    /// (<c>docs/plans/R6-explorer.md</c> §8).
    /// </summary>
    [Fact]
    public void 대본에서_고른_것이_세션의_선택이_된다() => HeadlessUi.Run(() =>
    {
        // R6 S-4 — 두 화면이 제 선택을 따로 들면 같은 에피소드를 두 자리에서 고르게 되고,
        // 어느 쪽이 지금 열린 글인지 사람이 못 가린다. 정본은 세션 하나다.
        WriteChapter("ch01", "ep01", "ep02");

        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode second = Board(session, "ch01", "ep01", "ep02");

        Pick(view, "ep02");

        Assert.Equal(second.Id, session.SelectedNodeId);
    });

    [Fact]
    public void 연출_그래프에서_고르면_대본이_따라온다() => HeadlessUi.Run(() =>
    {
        WriteChapter("ch01", "ep01", "ep02");

        (ScriptView view, AuthoringSession session) = Show();
        DialogueNode second = Board(session, "ch01", "ep01", "ep02");

        session.Select(second.Id);   // 저쪽 판에서 카드를 누른 것과 같은 길이다

        Assert.Equal("ep02", Tree(view).Selection!.EpisodeId);
        Assert.Equal("ep02", view.FindControl<TextBlock>("HeaderText")!.Text);
    });

    [Fact]
    public void 챕터_밖_노드를_고르면_대본은_가만히_있는다() => HeadlessUi.Run(() =>
    {
        // ⚠ 작가의 자유 판에서 고른 노드는 대본 탭에 설 자리가 없다 — 엉뚱한 글로 튀는 것보다
        //   가만히 있는 편이 낫다.
        WriteChapter("ch01", "ep01");

        (ScriptView view, AuthoringSession session) = Show();
        Board(session, "ch01", "ep01");

        DialogueNode loose = session.Editor.AddDialogueNode(
            session.Editor.EnsureChapterBoard("자유판"), name: "곁가지");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterEpisodePick? before = Tree(view).Selection;
        session.Select(loose.Id);

        Assert.Equal(before, Tree(view).Selection);
    });

    private static ChapterSceneTree Tree(ScriptView view) =>
        view.FindControl<ChapterSceneTree>("EpisodeTree")!;

    /// <summary>그 챕터의 판에 에피소드 노드들을 세운다 — 마지막 것을 돌려준다.</summary>
    private static DialogueNode Board(
        AuthoringSession session, string chapterId, params string[] episodeIds)
    {
        string fileId = session.Editor.EnsureChapterBoard(chapterId);

        DialogueNode? last = null;

        foreach (string episodeId in episodeIds)
        {
            last = session.Editor.AddDialogueNode(fileId, name: episodeId);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return last!;
    }

    /// <summary>트리에서 그 에피소드를 누른다 — 사람이 고르는 길 그대로다.</summary>
    private static void Pick(ScriptView view, string episodeId)
    {
        ChapterSceneTree tree = Tree(view);

        int index = tree.Rows
            .Select((row, at) => (row, at))
            .First(item => string.Equals(item.row.EpisodeId, episodeId, StringComparison.Ordinal)).at;

        tree.GetVisualDescendants().OfType<Button>().ElementAt(index)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static IReadOnlyList<string> Rows(ScriptView view, SceneTreeRowKind kind) =>
        view.FindControl<ChapterSceneTree>("EpisodeTree")!.Rows
            .Where(row => row.Kind == kind)
            .Select(row => row.EpisodeId ?? row.SceneId ?? row.ChapterId)
            .ToList();

    /// <summary>화자 등록부 — 원천은 <c>game.definition.json</c> 하나다.</summary>
    private void Register(params (string Name, string CharacterId)[] speakers) =>
        GameDefinitionStore.SaveSpeakers(
            ManifestPath,
            speakers.Select(item => new SpeakerSpec
            {
                Name = item.Name,
                CharacterId = item.CharacterId
            }).ToList());

    /// <summary>[화자 ▾]를 열고 그 이름을 고른다 — 목록 안의 단추가 실제 창구다.</summary>
    private static void PickSpeaker(ScriptView view, string name)
    {
        Click(view, "SpeakerButton");

        view.SpeakerMenuItems
            .Single(button => string.Equals(button.Content as string, name, StringComparison.Ordinal))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>에피소드 몇 개짜리 챕터 워크북 — 기획자가 만들어 둔 판의 최소 모양.</summary>
    private void WriteChapter(string chapterId, params string[] episodeIds)
    {
        // ⛔ 2026-09-16까지 이 헬퍼는 <b>워크북 파일</b>을 만들었다 (R-F 전). [대본] 탭이
        //    `chapters/*.xlsx`를 훑어 목록을 세웠기 때문이다. 이제 챕터의 주인이 프로젝트라
        //    픽스처도 그쪽에 세운다.
        if (_session is { } open)
        {
            Build(open.Editor);
            return;
        }

        // 아직 세션이 없다 — 저장물에 직접 심어 두면 Show()가 열 때 함께 들어온다.
        StoryProject project = ProjectStore.Load(ManifestPath).Project;
        Build(new ProjectEditor(project));
        ProjectStore.Save(ManifestPath, project);

        void Build(ProjectEditor editor)
        {
            ChapterDocument chapter = editor.EnsureChapter(chapterId);

            if (chapter.Stats.Count == 0)
            {
                chapter.Stats.Add(new ChapterStat("trust", "신뢰", 0, 0, 100, SourceRow: 0));
            }

            string previous = string.Empty;

            // ⚠ <b>카드 없는 에피소드를 짓는다</b> (2026-09-18). 이 헬퍼가 흉내내는 것은
            //    <i>기획자가 짜 온 챕터</i>이고, 거기에는 아직 작가의 대본이 없다 — 실제로
            //    그 상태가 오는 길은 챕터 워크북 임포트이고, 임포트는 문서에 바로 넣는다.
            //    `AddEpisode`를 쓰면 카드까지 함께 서서 「아직 안 쓴 에피소드」를 못 만든다.
            foreach (string id in episodeIds)
            {
                editor.FindChapter(chapterId)!.Episodes.Add(new ChapterEpisode(
                    id, id, Index: string.Empty, DialogueEntry: id,
                    0, 0, Memo: null, SourceRow: 0));

                if (previous.Length > 0)
                {
                    editor.AddEdge(chapterId, previous, id, optionLabel: "다음");
                }

                previous = id;
            }
        }
    }

    private string WorkbookPath(string chapterId, string episodeId) =>
        EpisodeLibrary.PathFor(EpisodeLibrary.FolderFor(ManifestPath, chapterId)!, episodeId);

    /// <summary>열려 있는 세션 — <see cref="WriteChapter"/>가 저장물과 세션 중 어디에 심을지 고른다.</summary>
    private AuthoringSession? _session;

    private (ScriptView View, AuthoringSession Session) Show()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);
        _session = session;

        var view = new ScriptView();
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        view.Attach(session);

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (view, session);
    }

    /// <summary>판 하나와 그 위의 대사노드 하나를 세우고 줄을 채운다.</summary>
    private DialogueNode Seed(
        AuthoringSession session, string chapterId, string episodeId,
        params (string Speaker, string Text)[] lines)
    {
        // ⚠ 챕터가 있어야 한다 — 에피소드 목록의 출처가 그쪽이다(기획자의 것).
        WriteChapter(chapterId, episodeId);

        // ⚠ <b>카드는 여기서 세운다.</b> `WriteChapter`는 <i>기획자가 짜 온 챕터</i>를
        //    흉내내므로 카드가 없다(임포트의 모양) — 작가가 대본을 다는 것이 이 줄이다.
        string fileId = session.Editor.EnsureChapterBoard(chapterId);
        DialogueNode node = session.Editor.AddDialogueNode(fileId, name: episodeId);
        string scriptId = session.Editor.EnsureDialogueScript(node.Id).Id;

        // 노드 생성이 딸려 주는 첫 빈 줄을 첫 대사로 쓴다.
        string first = session.Project.FindScript(scriptId)!.ActiveLines.First().Id;
        session.Editor.SetScriptLineText(scriptId, first, lines[0].Speaker, lines[0].Text);

        foreach ((string speaker, string text) in lines.Skip(1))
        {
            string id = session.Editor.InsertScriptLine(scriptId).Id;
            session.Editor.SetScriptLineText(scriptId, id, speaker, text);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return node;
    }

    private static void Click(ScriptView view, string name)
    {
        view.FindControl<Button>(name)!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static IReadOnlyList<string> LineIds(AuthoringSession session, DialogueNode node) =>
        session.Project.FindScript(node.ScriptId!)!.ActiveLines.Select(line => line.Id).ToList();

    private static IReadOnlyList<string> Texts(AuthoringSession session, DialogueNode node)
    {
        ScriptDocument script = session.Project.FindScript(node.ScriptId!)!;
        ScriptLocale primary = script.Locales.Single(locale => locale.Locale == script.PrimaryLocale);

        return script.ActiveLines.Select(line => primary.Find(line.Id).Text).ToList();
    }
}
