using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.Authoring.Model;

/// <summary>
/// 저작 도구가 다루는 공식 원본.
///
/// 세 종류의 데이터가 한 지붕 아래 있고, 서로 겹치지 않는다.
/// <code>
/// Scripts       작가의 대본에서 나온 줄 정체성과 locale별 화자·대사   ← 대사 본문의 유일한 원본
/// Files/Nodes   그 줄들에 얹는 대사 논리와 연출, 그리고 실행 흐름
/// Results       얼어붙은 발행 결과와 그 조합                        ← 불변, 추가만 가능
/// </code>
///
/// 프로젝트는 여러 <see cref="StoryFile"/>을 가지고, 각 파일이 노드를 소유한다.
/// 프로젝트 전체 노드 순서가 필요할 때는 <see cref="EnumerateNodes"/>를 사용한다.
/// 그 순서는 파일 순서 뒤에 각 파일 안의 노드 순서를 이어 붙인 것이다.
/// </summary>
public sealed class StoryProject
{
    /// <summary>
    /// 대본 산출물·발행 결과·RuntimeComposition을 도입한 형식 버전.
    ///
    /// 버전 2 이하는 화자·대사를 DialogueNode가 직접 소유했고 Presentation이 편집 중인
    /// 노드를 실시간으로 읽었다. 그 데이터를 새 의미로 자동 해석하면 어느 줄이 어느 LineId인지
    /// 도구가 임의로 정하게 된다. 그래서 <b>읽지 않고 명시적으로 거부한다.</b>
    /// </summary>
    public const int CurrentFormatVersion = 3;

    /// <summary>이 버전 이하는 읽지 않는다.</summary>
    public const int LastUnsupportedFormatVersion = 2;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string Title { get; set; } = "새 프로젝트";

    /// <summary>작가의 대본에서 나온 산출물. 화자·대사의 유일한 수정 가능한 원본이다.</summary>
    public List<ScriptDocument> Scripts { get; init; } = new();

    public List<StoryFile> Files { get; init; } = new();

    /// <summary>
    /// 실행 출구가 아닌 조건 공급 관계.
    ///
    /// ⚠ <b>범위 결정에는 더 이상 쓰이지 않는다</b> (2026-08-17) — 작가의 조건·변수는 판
    /// (챕터) 단위 전역이라 링크 없이 미친다. 구판 프로젝트의 데이터를 지우지 않으려고
    /// 남겨 둘 뿐이고, 그래프도 이 선을 그리지 않는다.
    /// </summary>
    public List<NodeLink> Links { get; init; } = new();

    /// <summary>
    /// <b>작가가 직접 더한 화자</b> (2026-08-17 소유자) — `game.definition.json`이 아니라
    /// 여기 산다. 정의 파일은 <b>기획자 전용</b>이고(스탯·전역 조건·초상화 매핑·연출 카탈로그),
    /// 작가가 임시로 쓰는 이름까지 거기 섞이면 두 사람의 자료가 한 파일에서 엉킨다.
    ///
    /// 드롭다운의 재료일 뿐이라 없어도 대본은 돈다 — 화자 칸은 자유 입력이다.
    /// </summary>
    public List<WriterSpeaker> WriterSpeakers { get; init; } = new();

    /// <summary>
    /// 작가의 커스텀 이징 곡선 (W67 후속). 커맨드 다섯째 인자 <c>@이름</c>의 유일한 원천 —
    /// 런타임용 <c>curves.json</c>은 내보내기가 여기서 만든다.
    /// </summary>
    public List<EaseCurve> EaseCurves { get; init; } = new();

    /// <summary>발행된 불변 결과. 추가만 되고 내용이 바뀌지 않는다.</summary>
    public ResultRepository Results { get; init; } = new();

    /// <summary>대사 결과와 연출 결과를 짝지어 둔 정식 출력 입력.</summary>
    public List<RuntimeComposition> Compositions { get; init; } = new();

    /// <summary>이야기가 시작되는 노드. null이면 아직 정하지 않은 것이다.</summary>
    public string? StartNodeId { get; set; }

    /// <summary>프리뷰 에셋 폴더 설정. 미설정이어도 저작은 계속된다.</summary>
    public AssetRootSettings AssetRoots { get; init; } = new();

    /// <summary>
    /// 최근 사용한 연출 커맨드 정의 Id, 최신이 앞. 갤러리의 "최근" 섹션이 읽는다.
    /// 프로젝트에 저장한다 — 이 프로젝트에서 자주 쓰는 어휘는 프로젝트의 것이다.
    /// </summary>
    public List<string> RecentCommandIds { get; init; } = new();

    /// <summary>갤러리 "최근" 섹션이 다 보여 줄 수 있는 만큼만 남긴다.</summary>
    public const int MaxRecentCommands = 8;

    /// <summary>내보내기 양식 선택 (X13). 기본 전부 켬.</summary>
    public ExportFormatSelection ExportFormats { get; init; } = new();

    /// <summary>
    /// 라이브 CompositionNode 출력 폴더 (X12c, D-1). 프로젝트 기준 상대 경로,
    /// null이면 라이브 출력 없음 — 편의 기능이 저작을 막지 않는다.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// 프로젝트 전체 노드를 결정적인 순서로 펼친다.
    /// 파일 순서 → 각 파일의 Nodes 순서다.
    /// </summary>
    public IEnumerable<StoryNode> EnumerateNodes()
    {
        return Files.SelectMany(file => file.Nodes);
    }

    public StoryFile? FindFile(string? fileId)
    {
        return fileId is null
            ? null
            : Files.FirstOrDefault(file => string.Equals(file.Id, fileId, StringComparison.Ordinal));
    }

    public StoryFile? FindFileContainingNode(string? nodeId)
    {
        return nodeId is null
            ? null
            : Files.FirstOrDefault(file => file.Nodes.Any(
                node => string.Equals(node.Id, nodeId, StringComparison.Ordinal)));
    }

    public StoryNode? FindNode(string? nodeId)
    {
        return nodeId is null
            ? null
            : EnumerateNodes().FirstOrDefault(
                node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));
    }

    public DialogueNode? FindDialogue(string? nodeId) => FindNode(nodeId) as DialogueNode;

    public PresentationNode? FindPresentation(string? nodeId) => FindNode(nodeId) as PresentationNode;

    public ScriptDocument? FindScript(string? scriptId)
    {
        return scriptId is null
            ? null
            : Scripts.FirstOrDefault(
                script => string.Equals(script.Id, scriptId, StringComparison.Ordinal));
    }

    /// <summary>이 대사 노드가 읽는 대본. 노드가 없거나 대본을 고르지 않았으면 null이다.</summary>
    public ScriptDocument? ScriptOf(string? dialogueNodeId) =>
        FindScript(FindDialogue(dialogueNodeId)?.ScriptId);

    public RuntimeComposition? FindComposition(string? compositionId)
    {
        return compositionId is null
            ? null
            : Compositions.FirstOrDefault(
                item => string.Equals(item.Id, compositionId, StringComparison.Ordinal));
    }

    public NodeLink? FindLink(string? linkId)
    {
        return linkId is null
            ? null
            : Links.FirstOrDefault(link => string.Equals(link.Id, linkId, StringComparison.Ordinal));
    }

    public IEnumerable<NodeLink> EnumerateLinks(NodeLinkKind kind)
    {
        return Links.Where(link => link.Kind == kind);
    }

    /// <summary>
    /// 프로젝트 안의 모든 SetNode 조건. 파일 순서, SetNode 순서, 조건 순서를 지킨다.
    /// 편집·진단용 전체 조회이며 DialogueNode의 드롭다운 범위에는 사용하지 않는다.
    /// Dialogue별 범위는 AvailableConditionResolver가 Settings link를 기준으로 계산한다.
    /// </summary>
    public IEnumerable<ConditionDefinition> EnumerateConditions()
    {
        return EnumerateNodes().OfType<SetNode>().SelectMany(node => node.Conditions);
    }

    public ConditionDefinition? FindCondition(string? conditionId)
    {
        return conditionId is null
            ? null
            : EnumerateConditions()
                .FirstOrDefault(item => string.Equals(item.Id, conditionId, StringComparison.Ordinal));
    }

    public StoryProject Clone()
    {
        return new StoryProject
        {
            FormatVersion = FormatVersion,
            Title = Title,
            StartNodeId = StartNodeId,
            AssetRoots = AssetRoots.Clone(),
            RecentCommandIds = new List<string>(RecentCommandIds),
            ExportFormats = ExportFormats.Clone(),
            OutputPath = OutputPath,
            Scripts = Scripts.Select(script => script.Clone()).ToList(),
            Files = Files.Select(file => file.Clone()).ToList(),
            Links = Links.Select(link => link.Clone()).ToList(),
            WriterSpeakers = WriterSpeakers.Select(speaker => speaker.Clone()).ToList(),
            EaseCurves = EaseCurves.Select(curve => curve.Clone()).ToList(),
            Results = Results.Clone(),
            Compositions = Compositions.Select(item => item.Clone()).ToList()
        };
    }
}
