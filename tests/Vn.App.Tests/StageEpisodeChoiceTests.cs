using Avalonia.Controls;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.App.Tests;

/// <summary>
/// 에피소드 끝의 챕터 간선 선택지 (2026-08-27 소유자 보고: "에피소드가 끝날 때 선택지가
/// 제시되지 않고 있어"). 전이 규칙 v9①의 프리뷰 절반이다: 대본 끝(나갈 곳 없음)에서
/// `간선` 시트의 선택지가 행 순서대로 서고, 고르면 도착 에피소드의 씬으로 넘어간다 —
/// 씬 선택기와 같은 길(SceneChosen) 하나를 지난다.
/// </summary>
public sealed class StageEpisodeChoiceTests
{
    [Fact]
    public void 에피소드_끝에서_간선_선택지가_행_순서대로_선다() => HeadlessUi.Run(() =>
    {
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ep01 = AddEpisodeNode(session, fileId, "EP01");
        AddEpisodeNode(session, fileId, "EP02");

        preview.SupplyChapters([Chapter("ch05",
            Edge("EP01", "EP02", "믿는다", row: 2),
            Edge("EP01", "EP02", "떠난다", row: 3))]);
        preview.Show(LastLineRequest());

        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));
        Assert.Equal(
            ["믿는다", "떠난다"],
            preview.CurrentRequest!.ChoiceOptions!.Select(option => option.Text).ToList());
    });

    [Fact]
    public void 관문_있는_간선은_숨기지_않고_조건을_병기한다() => HeadlessUi.Run(() =>
    {
        // 프리뷰는 챕터 스탯을 시뮬레이션하지 않는다 — 숨기면 거짓말이고, 근사임을 문구가 말한다.
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ep01 = AddEpisodeNode(session, fileId, "EP01");

        preview.SupplyChapters([Chapter("ch05",
            Edge("EP01", "EP02", "몰래 간다", row: 2, visibleCondition: "신뢰높음", unlockCondition: "열쇠있음"))]);
        preview.Show(LastLineRequest());

        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));
        Assert.Equal(
            "몰래 간다 〔표시: 신뢰높음 · 해금: 열쇠있음〕",
            preview.CurrentRequest!.ChoiceOptions!.Single().Text);
    });

    [Fact]
    public void 나가는_간선이_없으면_제시하지_않는다() => HeadlessUi.Run(() =>
    {
        // 챕터 종료 — 지금처럼 멈추는 것이 맞다. 커스텀 씬의 끝도 에피소드 끝이 아니다.
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ending = AddEpisodeNode(session, fileId, "EP99");
        DialogueNode custom = session.Editor.AddDialogueNode(fileId, name: "커스텀");

        preview.SupplyChapters([Chapter("ch05", Edge("EP01", "EP02", "믿는다", row: 2))]);
        preview.Show(LastLineRequest());

        Assert.False(preview.TryPresentEpisodeChoices(ending.Id));
        Assert.False(preview.TryPresentEpisodeChoices(custom.Id));
    });

    [Fact]
    public void 선택지를_고르면_도착_에피소드의_씬으로_넘어간다() => HeadlessUi.Run(() =>
    {
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ep01 = AddEpisodeNode(session, fileId, "EP01");
        DialogueNode ep02 = AddEpisodeNode(session, fileId, "EP02");

        preview.SupplyChapters([Chapter("ch05", Edge("EP01", "EP02", "믿는다", row: 2))]);
        preview.Show(LastLineRequest());
        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));

        string? chosen = null;
        preview.SceneChosen += id => chosen = id;
        preview.CurrentRequest!.ChoiceOptions!.Single().Choose!();

        Assert.Equal(ep02.Id, chosen); // 씬 선택기와 같은 길 — 연출 채널은 셸이 세운다
    });

    [Fact]
    public void 자유_씬이_매달린_간선은_씬을_먼저_재생하고_도착_에피소드가_뒤따른다() => HeadlessUi.Run(() =>
    {
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ep01 = AddEpisodeNode(session, fileId, "EP01");
        DialogueNode ep02 = AddEpisodeNode(session, fileId, "EP02");
        DialogueNode via = session.Editor.AddDialogueNode(fileId, name: "샛길씬");
        ep01.ChoiceExits["믿는다"] = via.Id; // 작가가 판에서 매단 자유 씬 (ViaNode)

        preview.SupplyChapters([Chapter("ch05", Edge("EP01", "EP02", "믿는다", row: 2))]);
        preview.Show(LastLineRequest());
        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));

        string? chosen = null;
        preview.SceneChosen += id => chosen = id;
        preview.CurrentRequest!.ChoiceOptions!.Single().Choose!();

        Assert.Equal(via.Id, chosen); // 씬이 먼저 —
        Assert.Equal(ep02.Id, preview.Playback.TakePendingEpisodeTarget()); // 다음 자리는 도착 에피소드
    });

    [Fact]
    public void 도착_에피소드가_동기화_전이면_안내하고_남는다() => HeadlessUi.Run(() =>
    {
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ep01 = AddEpisodeNode(session, fileId, "EP01"); // EP02 노드는 아직 없다

        preview.SupplyChapters([Chapter("ch05", Edge("EP01", "EP02", "믿는다", row: 2))]);
        preview.Show(LastLineRequest());
        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));

        string? chosen = null;
        preview.SceneChosen += id => chosen = id;
        preview.CurrentRequest!.ChoiceOptions!.Single().Choose!();

        Assert.Null(chosen); // 넘어갈 곳이 없다 — 상태줄 안내가 길을 말한다
    });

    // ── 조립 ──────────────────────────────────────────────────────────────

    private static (MiniStagePreview Preview, AuthoringSession Session, string FileId) ShowPreview()
    {
        var session = new AuthoringSession();
        var preview = new MiniStagePreview();
        var window = new Window { Width = 1200, Height = 800, Content = preview };
        window.Show();
        preview.Attach(session);

        string fileId = session.EnsureChapterBoard("ch05");
        session.SelectFile(fileId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (preview, session, fileId);
    }

    private static DialogueNode AddEpisodeNode(AuthoringSession session, string fileId, string episodeId)
    {
        DialogueNode node = session.Editor.AddDialogueNode(fileId, name: episodeId);
        node.ExcelEpisodeId = episodeId;
        return node;
    }

    private static MiniStagePreviewRequest LastLineRequest() => new(
        "테스트",
        MiniStageState.Empty,
        HasPresentation: false,
        SelectedLineId: "L1",
        SpeakerName: null,
        LineText: "마지막 줄",
        LineIndex: 0,
        LineCount: 1);

    private static ChapterEntry Chapter(string chapterId, params ChapterEdge[] edges) => new(
        chapterId,
        $"chapters/{chapterId}.xlsx",
        new ChapterGraphModel(chapterId, $"chapters/{chapterId}.xlsx", [], edges, [], [], [], []),
        OpenFailure: null);

    private static ChapterEdge Edge(
        string from,
        string to,
        string label,
        int row,
        string? visibleCondition = null,
        string? unlockCondition = null) => new(
            from, to, label, unlockCondition, LockedMessage: null, SourceRow: row)
        {
            VisibleConditionLabel = visibleCondition,
        };
}
