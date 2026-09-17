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
    /// (`ChapterBoardSupply.TidyBlockRows` → `EpisodeWorkbookWriter.ClearBlockRowIndexes`).
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
    OptionLabelBlank,

    /// <summary>
    /// ⛔ <b>대본의 조건 블록은 폐지됐다</b> (2026-09-16 소유자 — R-C,
    /// <c>docs/work-orders/tool-owns-workbooks-orders.md</c> §3).
    ///
    /// <b>이미 죽어 있던 기능이다.</b> 대본 워크북의 <c>조건라벨</c>은 챕터 `조건` 시트에서만
    /// 오고 그 시트는 전부 [2] 진행 스탯인데, 런타임이 <b>대사에서 스탯 분기를 금지</b>했다
    /// (`ked-presentation-runtime/docs/scene-boundary-plan.md` §4 G0). R4가 그 금지를
    /// <b>내보내기 관문에만</b> 반영해(<c>CoreRefusedChapter</c>와 같은 결) — 저작 표면은
    /// 여전히 드롭다운으로 권했다. 그래서 엑셀에서 만들 수 있는 조건 블록은 <b>전부</b>
    /// 내보내기에서 막혔다: 성공 사례가 테스트에 0건이었다.
    ///
    /// ⚠ 막는 자리와 권하는 자리가 다른 말을 하던 것을 여기서 합친다 — <b>읽는 시점에</b>
    /// 짚어야 며칠 쓰고 나서 "이거 안 나가는데?"를 만나지 않는다.
    /// 분기가 필요하면 챕터 간선의 표시조건·해금조건으로 올린다.
    /// </summary>
    EpisodeConditionBlockRetired,

    AutoEdgeHasSiblings,
    AutoEdgeHasConditions,
    AutoEdgeHasStatChanges,
    AutoEdgeCrossesScene,
    AutoEdgeHasChoiceLabel,

    /// <summary>
    /// 한 장면에 밖에서 들어오는 자리가 둘 이상이다 (V2 · 2026-09-16).
    ///
    /// 그 자리가 롤백이 되돌아갈 곳이고 이어하기가 재개할 곳이라, 둘이면 어느 쪽으로
    /// 되돌아갈지가 정해지지 않는다. 코어의 <c>VerifySceneEntries</c>와 같은 규칙이고
    /// 내보내기 관문도 같은 이유로 거부한다 — 여기서는 <b>더 일찍</b> 말할 뿐이다.
    /// </summary>
    SceneHasManyEntries,

    /// <summary>
    /// 파일을 <b>열지</b> 못했다 (R-F · 2026-09-16). 데이터의 흠이 아니라 접근 실패다 —
    /// 엑셀이 붙들고 있거나, 파일이 깨졌거나, 권한이 없다.
    ///
    /// ⚠ 그래도 <b>오류</b>다. 목록 화면에서는 한 파일을 못 읽어도 나머지를 보여 주는 것이
    /// 옳지만(<see cref="ChapterLibrary"/>), 임포트는 부분 성공하지 않는다(§5.2) — 무엇이
    /// 안 들어왔는지 모르는 채로 절반만 들이면 어느 챕터가 원본인지 사람이 알 수 없다.
    /// </summary>
    ChapterFileUnreadable
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

/// <summary>
/// 조건식 해석 문제를 <b>진단 코드</b>로 옮기는 규칙 — <b>한 벌이다</b>.
///
/// ⚠ 워크북 리더와 모델 검사가 <b>둘 다</b> 쓴다. 2026-09-17까지는 리더에만 있었고,
/// 그래서 R-F로 모델이 정본이 된 뒤 <b>툴에서 만든 조건의 흠은 아무도 안 봤다</b> —
/// 같은 날 스탯 검사가 겪은 일(*"안 옮기면 조용히 사라지는 검사가 된다"*)이 조건에서
/// 한 번 더 일어나 있었다.
/// </summary>
public static class ChapterDiagnostics
{
    public static ChapterDiagnosticCode CodeFor(ConditionProblemKind kind) => kind switch
    {
        ConditionProblemKind.UnknownStatKey => ChapterDiagnosticCode.StatKeyUnknown,
        ConditionProblemKind.ValueNotInteger => ChapterDiagnosticCode.StatValueNotInteger,
        ConditionProblemKind.Empty => ChapterDiagnosticCode.ConditionExpressionBlank,
        _ => ChapterDiagnosticCode.ConditionExpressionMalformed
    };
}
