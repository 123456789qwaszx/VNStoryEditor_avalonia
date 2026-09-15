using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// R-B — 챕터 워크북 이미터 (<c>docs/work-orders/tool-owns-workbooks-orders.md</c>).
///
/// <b>견본을 읽어 다시 내고 또 읽는다.</b> 손으로 지은 모델로 거는 것보다 이쪽이 세다 —
/// 규격의 실물(<c>docs/chapter-graph-sample.xlsx</c>)이 입력이고, 단언은 <b>첫 읽기와 둘째
/// 읽기를 견주는 것</b>이라 견본이 자라도 테스트가 낡지 않는다.
///
/// 이 왕복이 R-B의 합격 기준이자 뒤집기 전 구간의 안전망이다(지시서 §9).
/// </summary>
public sealed class ChapterWorkbookEmitterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-emitter-tests", Guid.NewGuid().ToString("N"));

    public ChapterWorkbookEmitterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    /// <summary>견본 → 이미터 → 리더. 돌아온 모델이 둘째 항이다.</summary>
    private (ChapterGraphModel First, ChapterGraphModel Again) RoundTrip()
    {
        ChapterGraphModel first = ChapterWorkbookReader.Read(SamplePath);

        string path = Path.Combine(_directory, "ch01.xlsx");
        ChapterWriteResult result = ChapterWorkbookEmitter.Emit(path, first);

        Assert.True(result.Written, result.Failure);

        return (first, ChapterWorkbookReader.Read(path));
    }

    [Fact]
    public void 낸_워크북을_리더가_오류_없이_읽는다()
    {
        (_, ChapterGraphModel again) = RoundTrip();

        List<ChapterDiagnostic> errors = again.Diagnostics
            .Where(item => item.Severity == ChapterDiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0, string.Join(" / ", errors.Select(item => item.Message)));
    }

    [Fact]
    public void 다섯_시트의_행_수가_보존된다()
    {
        (ChapterGraphModel first, ChapterGraphModel again) = RoundTrip();

        // 견본이 비어 있으면 이 테스트는 아무것도 지키지 못한다 — 먼저 그것부터 건다.
        Assert.NotEmpty(first.Episodes);
        Assert.NotEmpty(first.Edges);

        Assert.Equal(first.Episodes.Count, again.Episodes.Count);
        Assert.Equal(first.Edges.Count, again.Edges.Count);
        Assert.Equal(first.Conditions.Count, again.Conditions.Count);
        Assert.Equal(first.Stats.Count, again.Stats.Count);
        Assert.Equal(first.ChoiceOptions.Count, again.ChoiceOptions.Count);
    }

    [Fact]
    public void 에피소드의_신원과_장면과_이벤트키가_그대로_돌아온다()
    {
        (ChapterGraphModel first, ChapterGraphModel again) = RoundTrip();

        Assert.Equal(
            first.Episodes.Select(episode =>
                (episode.EpisodeId, episode.DialogueEntry, episode.Title,
                 episode.SceneId, episode.EventKey)),
            again.Episodes.Select(episode =>
                (episode.EpisodeId, episode.DialogueEntry, episode.Title,
                 episode.SceneId, episode.EventKey)));
    }

    [Fact]
    public void 간선의_순서와_문구와_관문이_그대로_돌아온다()
    {
        (ChapterGraphModel first, ChapterGraphModel again) = RoundTrip();

        // ⚠ 행 순서가 곧 화면 순서이자 서버 이력의 OptionIndex다 — 왕복이 순서를 바꾸면
        //    출시된 선택지의 뜻이 달라진다. 그래서 정렬하지 않고 순서 그대로 견준다.
        Assert.Equal(
            first.Edges.Select(edge =>
                (edge.FromEpisodeId, edge.ToEpisodeId, edge.OptionLabel,
                 edge.VisibleConditionLabel, edge.ConditionLabel, edge.Auto)),
            again.Edges.Select(edge =>
                (edge.FromEpisodeId, edge.ToEpisodeId, edge.OptionLabel,
                 edge.VisibleConditionLabel, edge.ConditionLabel, edge.Auto)));
    }

    [Fact]
    public void 스탯변화가_왕복에서_상하지_않는다()
    {
        (ChapterGraphModel first, ChapterGraphModel again) = RoundTrip();

        // 원문을 보관하지 않고 다시 조립하는 유일한 칸이라 따로 건다 —
        // 깃발(Set)은 부호 없이, 정수(Add)는 부호를 달고 나가야 한다.
        Assert.Equal(
            first.Edges.Select(edge => edge.StatChanges.Select(
                delta => (delta.Key, delta.Amount, delta.Kind))),
            again.Edges.Select(edge => edge.StatChanges.Select(
                delta => (delta.Key, delta.Amount, delta.Kind))));
    }

    [Fact]
    public void 스탯의_타입과_경계가_그대로_돌아온다()
    {
        (ChapterGraphModel first, ChapterGraphModel again) = RoundTrip();

        Assert.Equal(
            first.Stats.Select(stat =>
                (stat.Key, stat.DisplayName, stat.Initial, stat.Minimum, stat.Maximum, stat.Type)),
            again.Stats.Select(stat =>
                (stat.Key, stat.DisplayName, stat.Initial, stat.Minimum, stat.Maximum, stat.Type)));
    }

    [Fact]
    public void 임시_파일을_남기지_않는다()
    {
        RoundTrip();

        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
        Assert.Single(Directory.EnumerateFiles(_directory, "*.xlsx"));
    }
}
