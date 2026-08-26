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

    /// <summary>
    /// 간선에 매단 자유 씬에 재생할 줄이 하나도 없다 (2026-08-25). 줄 없는 노드는
    /// YarnProject에 실리지 않아 게임이 그 씬을 찾지 못한다.
    /// </summary>
    ViaSceneEmpty,

    /// <summary>
    /// 에피소드의 대사 노드에 재생할 줄이 하나도 없다 (2026-08-25). 같은 이유로 치명적이다 —
    /// 그 노드가 YarnProject에서 빠져 재생이 시작되지 않는다.
    /// </summary>
    EpisodeSceneEmpty,

    DialogueEntryBlank,
    PositionNotNumeric,
    ConditionLabelUndefined,
    ConditionExpressionBlank,
    ConditionExpressionMalformed,
    StatKeyUnknown,
    StatValueNotInteger,
    StatRangeInvalid,

    /// <summary>
    /// ⛔ <b>안 쓴다 (2026-08-26 폐지)</b> — 스탯이 2~5개 밖일 때 뜨던 경고. 챕터를 갓 만들면
    /// 0개라 언제나 떴고, 언제나 뜨는 경고는 옆에 선 진짜 오류까지 안 읽히게 만들었다.
    /// <b>이름은 남긴다</b>: 이 enum은 사람이 읽는 진단 코드라 값을 지우면 옛 보고·기록의
    /// 코드 이름이 다른 뜻으로 밀린다.
    /// </summary>
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

    /// <summary>
    /// v14 — 블록 행(IF·ELSEIF·ENDIF)에 대사 순번이 남아 있다. <b>오류가 아니라 할 일이다</b>:
    /// 템플릿이 미리 깔아 둔 번호라 사람의 잘못이 아니고, 동기화가 그 칸을 비운다
    /// (`EpisodeSyncService.TidyBlockRows` → `EpisodeWorkbookWriter.ClearBlockRowIndexes`).
    /// ⚠ 이 코드가 <b>치울 것을 찾는 열쇠</b>다 — 글자로 찾지 않는다.
    /// </summary>
    BlockRowIndexStray,

    /// <summary>v11 — `종류`와 `선택지` 문구가 서로 어긋난다.</summary>
    EdgeKindMismatch,

    /// <summary>v11 — `종류` 칸에 모르는 낱말이 적혀 있다.</summary>
    EdgeKindUnknown,

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
    CoreRefusedChapter,

    /// <summary>
    /// 간선에 `선택지` 문구가 없다 (2026-08-24 규격). **모든 길은 선택지다** —
    /// 문구 없이 넘어가는 "보이지 않는 기본"은 폐지됐다.
    /// </summary>
    OptionLabelBlank
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
