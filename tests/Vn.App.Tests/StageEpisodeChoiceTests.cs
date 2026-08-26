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

        preview.SupplyChapters([Chapter("ch05", edges:
            [
                Edge("EP01", "EP02", "믿는다", row: 2),
                Edge("EP01", "EP02", "떠난다", row: 3),
            ])]);
        preview.Show(LastLineRequest());

        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));
        Assert.Equal(
            ["믿는다", "떠난다"],
            preview.CurrentRequest!.ChoiceOptions!.Select(option => option.Text).ToList());
    });

    [Fact]
    public void 조건_시트에_없는_라벨은_깨진_관문으로_막고_사유를_병기한다() => HeadlessUi.Run(() =>
    {
        // 깨진 조건을 통과시켜 도달을 부풀리지 않는다(증명기와 한 벌) — 다만 저작 도구라
        // 지우는 대신 흐리게 남기고 무엇이 깨졌는지 문구가 말한다.
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ep01 = AddEpisodeNode(session, fileId, "EP01");

        preview.SupplyChapters([Chapter("ch05",
            edges: [Edge("EP01", "EP02", "몰래 간다", row: 2, visibleCondition: "신뢰높음")])]);
        preview.Show(LastLineRequest());

        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));

        StageChoiceOption option = preview.CurrentRequest!.ChoiceOptions!.Single();
        Assert.Equal("몰래 간다 〔조건 해석 불가 · 신뢰높음〕", option.Text);
        Assert.True(option.IsDisabled);
        Assert.Null(option.Choose);
    });

    [Fact]
    public void 표시조건_미달은_흐리게_남고_해금조건_미달은_잠금_안내문을_말한다() => HeadlessUi.Run(() =>
    {
        // 게임과 같은 판정(표시 미달 = 목록에서 빠질 길 · 해금 미달 = 잠긴 채 보일 길)을
        // 지금 스탯으로 실제로 한다 — trust 초기값 0이라 둘 다 미달이다.
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ep01 = AddEpisodeNode(session, fileId, "EP01");

        preview.SupplyChapters([Chapter("ch05",
            edges:
            [
                Edge("EP01", "EP02", "숨은 길", row: 2, visibleCondition: "신뢰2"),
                Edge("EP01", "EP02", "잠긴 길", row: 3, unlockCondition: "신뢰2", lockedMessage: "신뢰가 부족하다"),
            ],
            stats: [Trust()],
            conditions: [TrustAtLeast2()])]);
        preview.Show(LastLineRequest());

        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));

        IReadOnlyList<StageChoiceOption> options = preview.CurrentRequest!.ChoiceOptions!;
        Assert.Equal("숨은 길 〔표시조건 미달 · 신뢰2〕", options[0].Text);
        Assert.True(options[0].IsDisabled);
        Assert.Equal("🔒 잠긴 길 — 신뢰가 부족하다", options[1].Text);
        Assert.True(options[1].IsDisabled);
        Assert.All(options, option => Assert.Null(option.Choose));
    });

    [Fact]
    public void 간선_선택이_스탯을_커밋해_다음_에피소드의_관문이_열린다() => HeadlessUi.Run(() =>
    {
        // 전이 규칙 v9① 그대로: 간선을 타는 순간 1회 커밋, 판정은 커밋 전 값.
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ep01 = AddEpisodeNode(session, fileId, "EP01");
        DialogueNode ep02 = AddEpisodeNode(session, fileId, "EP02");
        AddEpisodeNode(session, fileId, "EP03");

        preview.SupplyChapters([Chapter("ch05",
            edges:
            [
                Edge("EP01", "EP02", "믿는다", row: 2, statChanges: [new StatDelta("trust", 2)]),
                Edge("EP02", "EP03", "속을 털어놓는다", row: 3, unlockCondition: "신뢰2"),
            ],
            stats: [Trust()],
            conditions: [TrustAtLeast2()])]);
        preview.Show(LastLineRequest());

        // EP01 끝 — 커밋 전이라 EP02의 관문은 아직 잠겨 있을 값이다.
        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));
        preview.CurrentRequest!.ChoiceOptions!.Single().Choose!(); // trust 0 → 2 커밋

        // HUD가 챕터 단위 누적을 든다 — 요청은 편집기가 짓지만 스탯은 프리뷰가 얹는다.
        preview.Show(LastLineRequest());
        Assert.Equal(2, preview.CurrentRequest!.ChapterStats!.Single().Value);

        // EP02 끝 — 커밋된 값으로 관문이 열린다.
        Assert.True(preview.TryPresentEpisodeChoices(ep02.Id));
        StageChoiceOption unlocked = preview.CurrentRequest!.ChoiceOptions!.Single();
        Assert.Equal("속을 털어놓는다", unlocked.Text);
        Assert.False(unlocked.IsDisabled);
        Assert.NotNull(unlocked.Choose);
    });

    [Fact]
    public void 새_전체_재생은_스탯을_처음값으로_되돌린다() => HeadlessUi.Run(() =>
    {
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ep01 = AddEpisodeNode(session, fileId, "EP01");
        AddEpisodeNode(session, fileId, "EP02");

        preview.SupplyChapters([Chapter("ch05",
            edges:
            [
                Edge("EP01", "EP02", "믿는다", row: 2, statChanges: [new StatDelta("trust", 2)]),
                Edge("EP01", "EP02", "잠긴 길", row: 3, unlockCondition: "신뢰2"),
            ],
            stats: [Trust()],
            conditions: [TrustAtLeast2()])]);
        preview.Show(LastLineRequest());

        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));
        preview.CurrentRequest!.ChoiceOptions![0].Choose!(); // trust → 2

        // 새 판 — detour 이력과 같은 신호(ResetDetours → RunReset)가 스탯도 되돌린다.
        preview.Playback.ResetDetours();

        preview.Show(LastLineRequest());
        Assert.True(preview.TryPresentEpisodeChoices(ep01.Id));
        Assert.True(preview.CurrentRequest!.ChoiceOptions![1].IsDisabled); // 다시 잠겼다
        Assert.Equal(0, preview.CurrentRequest!.ChapterStats!.Single().Value);
    });

    [Fact]
    public void 나가는_간선이_없으면_제시하지_않는다() => HeadlessUi.Run(() =>
    {
        // 챕터 종료 — 지금처럼 멈추는 것이 맞다. 커스텀 씬의 끝도 에피소드 끝이 아니다.
        var (preview, session, fileId) = ShowPreview();
        DialogueNode ending = AddEpisodeNode(session, fileId, "EP99");
        DialogueNode custom = session.Editor.AddDialogueNode(fileId, name: "커스텀");

        preview.SupplyChapters([Chapter("ch05", edges: [Edge("EP01", "EP02", "믿는다", row: 2)])]);
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

        preview.SupplyChapters([Chapter("ch05", edges: [Edge("EP01", "EP02", "믿는다", row: 2)])]);
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

        preview.SupplyChapters([Chapter("ch05", edges: [Edge("EP01", "EP02", "믿는다", row: 2)])]);
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

        preview.SupplyChapters([Chapter("ch05", edges: [Edge("EP01", "EP02", "믿는다", row: 2)])]);
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

    private static ChapterEntry Chapter(
        string chapterId,
        ChapterEdge[]? edges = null,
        ChapterStat[]? stats = null,
        ChapterCondition[]? conditions = null) => new(
        chapterId,
        $"chapters/{chapterId}.xlsx",
        new ChapterGraphModel(
            chapterId,
            $"chapters/{chapterId}.xlsx",
            [],
            edges ?? [],
            conditions ?? [],
            stats ?? [],
            [],
            []),
        OpenFailure: null);

    private static ChapterEdge Edge(
        string from,
        string to,
        string label,
        int row,
        string? visibleCondition = null,
        string? unlockCondition = null,
        string? lockedMessage = null,
        StatDelta[]? statChanges = null) => new(
            from, to, label, unlockCondition, LockedMessage: lockedMessage, SourceRow: row)
        {
            VisibleConditionLabel = visibleCondition,
            StatChanges = statChanges ?? [],
        };

    private static ChapterStat Trust() => new("trust", "신뢰", 0, 0, 5, SourceRow: 2);

    private static ChapterCondition TrustAtLeast2() => new(
        "신뢰2",
        "trust >= 2",
        Description: null,
        [new ConditionTerm(ConditionTermKind.StatComparison, "trust", ConditionComparison.AtLeast, 2)],
        IsValid: true,
        SourceRow: 2);
}
