namespace Vn.Authoring.Chapters;

/// <summary>
/// <b>프로젝트가 든 챕터 하나</b> (R-F · 2026-09-16,
/// <c>docs/work-orders/tool-owns-workbooks-orders.md</c> §1.1).
///
/// ⛔ <b>여기가 뒤집기의 나머지 절반이다.</b> R-E까지는 <i>대사 본문</i>의 주인이 옮겨
/// 왔고, 에피소드 구조·간선·선택지·조건·스탯은 아직 <c>chapters/{Id}.xlsx</c>가 쥐고
/// 있었다. 이 타입이 서면서 그 값들이 프로젝트 안으로 들어온다 — 워크북은
/// <c>exported/</c>와 같은 편, 즉 <b>산출물</b>이 된다.
///
/// <b><see cref="ChapterGraphModel"/>과 무엇이 다른가</b> — 그쪽은 <i>워크북 하나를 읽은
/// 결과</i>이고 이쪽은 <i>저작한 값</i>이다. 그래서 읽기의 부산물 셋이 여기 없다:
/// <list type="bullet">
/// <item><c>SourcePath</c> — 어느 파일에서 읽었나. 이제 낼 자리이지 읽은 자리가 아니다.</item>
/// <item><c>Diagnostics</c> — 그 파일에 대한 리더의 불평. 저작한 값에 붙일 것이 아니다.</item>
/// <item><c>Speakers</c>·<c>HasSpeakerSheet</c> — 폐지된 `화자` 시트를 정의 파일로 흡수하는
///   <b>일회성 이행</b>에서만 쓴다(2026-08-23). 임포터의 것이지 프로젝트의 것이 아니다.</item>
/// </list>
///
/// ⚠ <b>파생값은 담지 않는다.</b> <c>ChapterCondition.Parsed</c>·<c>IsValid</c>는 식 원문에서
/// 나오므로 <see cref="ToGraphModel"/>이 다시 푼다 — 담아 두면 식을 고쳤을 때 둘이 갈린다.
/// 원문이 정본이라는 규율(§0.5 무해석성)이 저장물에서도 그대로다.
///
/// ⚠ <b><c>SourceRow</c>는 남겨 두되 뜻이 옅어진다.</b> 임포트해 온 값은 원래 행 번호를
/// 그대로 들고, 툴에서 새로 만든 것은 0이다. 진단이 사람에게 자리를 짚어 주던 근거인데,
/// 낼 파일의 행 번호는 이미터가 정하므로 R-F 뒤에는 <b>임포트 이력</b>에 가깝다.
/// </summary>
public sealed class ChapterDocument
{
    public required string ChapterId { get; set; }

    public List<ChapterEpisode> Episodes { get; init; } = [];

    public List<ChapterEdge> Edges { get; init; } = [];

    public List<ChapterCondition> Conditions { get; init; } = [];

    public List<ChapterStat> Stats { get; init; } = [];

    /// <summary>챕터가 함께 쓰는 문구 사전 — 배선이 아니라 어휘집이다 (v9).</summary>
    public List<ChapterChoiceOption> ChoiceOptions { get; init; } = [];

    /// <summary>재생루트를 눈으로 보기 위한 테스트 데이터. 내보내기에 안 섞인다 (§3.1).</summary>
    public List<ChapterFixture> Fixtures { get; init; } = [];

    /// <summary>
    /// 읽는 쪽이 늘 쓰던 모양으로 낸다 — 내보내기·도달성·프리뷰·그래프 뷰가 전부
    /// <see cref="ChapterGraphModel"/>을 받으므로, 주인이 바뀌어도 그쪽은 그대로다.
    /// </summary>
    /// <param name="sourcePath">이 챕터를 <b>낼</b> 워크북 경로. 이제 읽은 자리가 아니다.</param>
    public ChapterGraphModel ToGraphModel(string sourcePath) => new(
        ChapterId,
        sourcePath,
        Episodes,
        Edges,
        Conditions.Select(Reparse).ToList(),
        Stats,
        Fixtures,
        // 저작한 값에는 리더의 불평이 없다 — 검증은 저작 검증이 따로 한다.
        diagnostics: [],
        speakers: [],
        hasSpeakerSheet: false,
        ChoiceOptions);

    /// <summary>
    /// 워크북에서 읽어 온 모델을 저작 값으로 받아들인다 — <b>임포트의 마지막 한 걸음</b>.
    ///
    /// ⚠ 진단은 버린다. 들여올지 말지는 임포터가 <b>이미</b> 정했고(§5.2 — 하나라도 깨졌으면
    /// 아무것도 안 들여온다), 통과한 뒤에 남은 불평을 저작 값에 붙이면 프로젝트가 옛 파일의
    /// 흠을 영원히 들고 다닌다.
    /// </summary>
    public static ChapterDocument From(ChapterGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new ChapterDocument
        {
            ChapterId = model.ChapterId,
            Episodes = [.. model.Episodes],
            Edges = [.. model.Edges],
            Conditions = [.. model.Conditions],
            Stats = [.. model.Stats],
            ChoiceOptions = [.. model.ChoiceOptions],
            Fixtures = [.. model.Fixtures]
        };
    }

    /// <summary>식 원문에서 해석 결과를 다시 만든다 — 담아 둔 것을 믿지 않는다.</summary>
    private ChapterCondition Reparse(ChapterCondition condition)
    {
        if (condition.Expression.Length == 0)
        {
            return condition with { Parsed = [], IsValid = false };
        }

        ConditionParseResult parsed = ConditionExpressionParser.Parse(
            condition.Expression,
            Stats.Select(stat => stat.Key).ToHashSet(StringComparer.Ordinal));

        return condition with { Parsed = parsed.Terms, IsValid = parsed.IsValid };
    }
}
