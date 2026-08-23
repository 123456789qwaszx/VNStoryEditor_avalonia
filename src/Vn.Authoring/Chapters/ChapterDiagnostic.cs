namespace Vn.Authoring.Chapters;

public enum ChapterDiagnosticSeverity
{
    /// <summary>읽지 않고 지나친 것을 알리는 기록. 데이터는 유효하다.</summary>
    Info,

    /// <summary>읽기는 했지만 사람이 봐야 한다. 모델은 만들어진다.</summary>
    Warning,

    /// <summary>규격 위반. 모델은 만들되 <see cref="ChapterGraphModel.HasErrors"/>가 선다.</summary>
    Error
}

public enum ChapterDiagnosticCode
{
    SheetMissing,
    SheetIgnored,
    ColumnHeaderUnexpected,
    EpisodeIdBlank,
    EpisodeIdDuplicated,
    DialogueEntryBlank,
    PositionNotNumeric,
    ConditionLabelUndefined,
    ConditionExpressionBlank,
    ConditionExpressionMalformed,
    StatKeyUnknown,
    StatValueNotInteger,
    StatRangeInvalid,
    StatCountOutOfRange,
    StatMissingFromGameDefinition,
    EdgeEndpointUnknown,
    EdgeEndpointBlank,
    BooleanNotRecognized,
    FormulaWithoutCachedValue,
    EpisodeUnreachable,
    OptionEdgeMismatch,
    ExitIntoExcelNode,
    FixtureStatColumnUnknown,
    FixtureChoiceMalformed,
    EndingKeyBlank,

    /// <summary>v11 — `종류`와 `선택지` 문구가 서로 어긋난다.</summary>
    EdgeKindMismatch,

    /// <summary>v11 — `종류` 칸에 모르는 낱말이 적혀 있다.</summary>
    EdgeKindUnknown,

    /// <summary>v11 — 같은 도착으로 들어오는 간선들의 엔딩키가 다르다.</summary>
    EndingKeyConflict,

    /// <summary>v11 — 간선에 매달린 연출 노드가 아직 비어 있다(경고).</summary>
    EdgePresentationEmpty,

    /// <summary>
    /// `대사엔트리`가 가리키는 대사노드가 <b>그 챕터의 판에 없다</b> (2026-08-23).
    /// 이대로 내보내면 진행 JSON이 존재하지 않는 yarn 노드를 부른다.
    /// </summary>
    DialogueEntryNodeMissing,

    /// <summary>
    /// 진행 코어가 이 챕터를 <b>싣지 못한다</b> (2026-08-23). 검증은 통과했지만 산출물을
    /// 실제 소비자에게 먹여 보니 거부했다는 뜻이고, 그대로 내보내면 게임에서 그 챕터가
    /// 시작되지 않는다.
    /// </summary>
    CoreRefusedChapter
}

/// <summary>
/// 워크북에서 발견한 것 하나. <b>파일·시트·행·열까지 반드시 짚는다</b> — 기획자가
/// 엑셀만 열어서 고칠 수 있어야 하므로(마스터 플랜 §4), "어딘가 잘못됐다"는 보고는 실패다.
/// </summary>
/// <param name="Row">1부터 시작하는 엑셀 행 번호. 시트 전체에 대한 지적이면 null.</param>
/// <param name="Column">"A"·"K" 같은 엑셀 열 이름. 행 전체에 대한 지적이면 null.</param>
public sealed record ChapterDiagnostic(
    ChapterDiagnosticSeverity Severity,
    ChapterDiagnosticCode Code,
    string FilePath,
    string? Sheet,
    int? Row,
    string? Column,
    string Message)
{
    /// <summary>"파일 · 시트 · 4행 · I열 — 메시지" 한 줄. 상태줄·목록이 그대로 쓴다.</summary>
    public string Describe()
    {
        var parts = new List<string> { System.IO.Path.GetFileName(FilePath) };

        if (!string.IsNullOrEmpty(Sheet))
        {
            parts.Add(Sheet);
        }

        if (Row is int row)
        {
            parts.Add($"{row}행");
        }

        if (!string.IsNullOrEmpty(Column))
        {
            parts.Add($"{Column}열");
        }

        return $"{string.Join(" · ", parts)} — {Message}";
    }

    public override string ToString() => Describe();
}
