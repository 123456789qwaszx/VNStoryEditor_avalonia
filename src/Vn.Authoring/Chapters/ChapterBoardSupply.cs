using ClosedXML.Excel;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Chapters;
/// <summary>
/// <b>챕터가 판에 공급하는 것</b> — 조건 배관과 가드레일.
///
/// ⚠ 이 클래스는 <c>EpisodeSyncService</c>였고, 그 이름의 본체이던 <c>Sync</c>(워크북 →
/// 대사노드 역방향 반영)는 2026-09-16에 걷혔다 (R-D, 지시서 §2). 남은 것은 <b>동기화가
/// 아니었던 것들</b>이다: 챕터 `조건` 시트를 설정노드로 공급하는 배관과, 판을 훑어
/// 위험을 말하는 경고 둘. 워크북에서 들여오는 일은 이제
/// <see cref="Import.EpisodeWorkbookImporter"/>가 하고, 그것은 <b>한 번</b>만 돈다.
///
/// 이름이 바뀐 이유가 그것이다 — 여기 남은 일에는 "동기화"가 하나도 없다.
/// </summary>
public static class ChapterBoardSupply
{
    /// <summary>
    /// 챕터 조건 공급 설정노드의 이름 규약 — 이 이름이 곧 신원이다. 동기화(생성·dedupe)와
    /// 그래프 프로젝션(작가 화면에서 숨김)이 같은 규칙 하나를 쓴다.
    /// </summary>
    public static string ConditionSupplyNodeName(string chapterId) => $"챕터 {chapterId} 조건";

    /// <summary>
    /// 이 노드가 <b>A계층(기획자) 조건을 나르는 배관</b>인가. 챕터 = 판 1:1이므로 판 이름이
    /// 곧 챕터 Id다. <b>계층을 가르는 규칙은 이 하나뿐이다</b>(사본 금지) — 그래프 프로젝션의
    /// 카드 숨김도, 작가 조건 목록의 배제도 같은 것을 부른다.
    /// </summary>
    public static bool IsConditionSupplyNode(StoryNode node, StoryFile file)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(file);

        // 판 이름이 챕터 Id와 어긋난 프로젝트(구판·수기 개명)에서도 배관은 배관이다 —
        // 이름 규약만으로도 알아본다. 이 판정이 이미터의 네임스페이스에도 쓰이므로
        // (스탯에는 접두를 붙이면 안 된다) 놓치면 게임이 깨진다.
        return node is SetNode &&
               (string.Equals(node.Name, ConditionSupplyNodeName(file.Name), StringComparison.Ordinal) ||
                IsConditionSupplyNodeName(node.Name));
    }

    /// <summary>이름만으로 A계층 공급 노드인지 — <c>챕터 … 조건</c>.</summary>
    public static bool IsConditionSupplyNodeName(string? name) =>
        name is not null &&
        name.StartsWith("챕터 ", StringComparison.Ordinal) &&
        name.EndsWith(" 조건", StringComparison.Ordinal);

    /// <summary>프로젝트 전체에서 A계층 공급 노드의 Id들. 작가 화면이 이 집합을 걸러 낸다.</summary>
    public static HashSet<string> ConditionSupplyNodeIds(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (StoryFile file in project.Files)
        {
            foreach (StoryNode node in file.Nodes)
            {
                if (IsConditionSupplyNode(node, file))
                {
                    ids.Add(node.Id);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// 라벨이 바뀐 조건은 <b>이름만 갈아 끼운다</b> (2026-08-24 소유자 — "조건 이름을 편집한
    /// 경우도 연결이 이어지도록").
    ///
    /// 줄에 매달린 전환은 <see cref="Model.LineConditionTransition.ConditionId"/>로 잇는다.
    /// 그래서 이름이 바뀌었다고 조건을 <b>새로 만들면</b> Id가 달라져 이미 매달린 갈래가 전부
    /// 고아가 된다 — 같은 식이면 같은 조건이므로, 있는 것의 이름만 고쳐 Id를 지킨다.
    /// 반대로 이름을 그냥 두면 판에는 <b>사라진 옛 라벨</b>이 계속 보인다.
    ///
    /// ⚠ 고치는 것은 <b>공급 노드가 소유한 조건</b>뿐이다. 같은 식을 전역 정의나 작가의
    /// 설정노드가 이미 주고 있어도 그쪽 이름은 남의 것이라 건드리지 않는다.
    /// </summary>
    /// <returns>공급 노드가 이미 그 식을 갖고 있어 새로 만들 필요가 없으면 true.</returns>
    private static bool RenameSuppliedCondition(
        ProjectEditor editor,
        SetNode supply,
        string label,
        string yarn)
    {
        ConditionDefinition? existing = supply.Conditions.FirstOrDefault(condition =>
            string.Equals(condition.Expression.Trim(), yarn, StringComparison.Ordinal));

        if (existing is null)
        {
            return false;
        }

        if (!string.Equals(existing.Name, label, StringComparison.Ordinal))
        {
            editor.UpdateCondition(existing.Id, label, existing.Expression);
        }

        return true;
    }

    /// <summary>
    /// 챕터의 조건 <b>전부</b>를 그 판의 모든 대사 노드가 쓸 수 있게 공급한다 (2단계 4번).
    /// 작가의 자유 노드가 조건 드롭다운에서 챕터 라벨(A 계층)을 바로 고르게 하는 자리다 —
    /// 설정노드를 손으로 찾아 잇는 절차가 없어야 "개발자를 안 부르고" 조건을 건다.
    /// 링크·조건 모두 멱등이다(있으면 다시 만들지 않는다).
    /// </summary>
    public static void SupplyChapterConditionsToBoard(
        ProjectEditor editor,
        GameDefinition definition,
        string fileId,
        ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(chapter);

        List<(string Label, string Yarn)> translatable = chapter.Conditions
            .Select(condition => (condition.Label, Translated: ConditionYarnTranslator.Translate(condition)))
            .Where(pair => pair.Translated.IsTranslatable)
            .Select(pair => (pair.Label, Yarn: pair.Translated.Yarn!))
            .DistinctBy(pair => pair.Yarn, StringComparer.Ordinal)
            .ToList();

        if (translatable.Count == 0)
        {
            return;
        }

        string supplyName = ConditionSupplyNodeName(chapter.ChapterId);

        SetNode? supply = editor.Project.EnumerateNodes().OfType<SetNode>()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, supplyName, StringComparison.Ordinal));

        supply ??= editor.AddSetNode(fileId, name: supplyName);

        foreach ((string label, string yarn) in translatable)
        {
            // 있으면 이름만 따라가고(Id 보존 = 매달린 갈래 보존), 없으면 만든다.
            if (!RenameSuppliedCondition(editor, supply, label, yarn))
            {
                editor.AddCondition(supply.Id, label, yarn);
            }
        }

        // 대사노드마다 링크를 잇던 고리는 폐지됐다 (2026-08-17) — 공급 범위가 판(챕터)
        // 전체다. 노드를 새로 만들 때마다 배관을 다시 잇지 않아도 된다.
    }

    /// <summary>
    /// 가드레일 (2단계 4번) — 작가의 자유 노드가 Tier 2 스탯(A 계층)에 <c>&lt;&lt;set&gt;&gt;</c>을
    /// 걸면 경고한다. 대사 중의 스탯 직접 조작은 설계에서 통째로 뺐다(2026-08-14 — J열 폐지):
    /// 세이브/로드 복귀·도달성 증명이 못 보는 값 변화가 대본에 숨는다.
    /// 막지는 않는다(재생은 되니까), 크게 말한다.
    /// </summary>
    public static IReadOnlyList<ChapterDiagnostic> WarnFreeNodeStatWrites(
        ProjectEditor editor,
        string fileId,
        ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(chapter);

        HashSet<string> statKeys = chapter.Stats
            .Select(stat => stat.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (statKeys.Count == 0)
        {
            return Array.Empty<ChapterDiagnostic>();
        }

        StoryFile? file = editor.Project.Files.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, fileId, StringComparison.Ordinal));

        var warnings = new List<ChapterDiagnostic>();

        foreach (DialogueNode dialogue in (file?.Nodes ?? []).OfType<DialogueNode>())
        {
            if (dialogue.ExcelEpisodeId is not null)
            {
                continue; // 엑셀노드의 본문은 동기화 산출물 — set이 애초에 못 붙는다
            }

            foreach (DialogueLineExtension extension in dialogue.LineExtensions)
            {
                foreach (SetOperation operation in extension.SetOperations)
                {
                    string variable = operation.Variable.TrimStart('$');

                    if (statKeys.Contains(variable))
                    {
                        warnings.Add(new ChapterDiagnostic(
                            ChapterDiagnosticSeverity.Warning,
                            ChapterDiagnosticCode.StatKeyUnknown,
                            chapter.SourcePath, null, null, null,
                            $"자유 노드 '{dialogue.Name}'이 스탯 '{variable}'을 set으로 바꿉니다 — " +
                            "대사 중의 스탯 직접 조작은 설계에서 뺐습니다(세이브/로드·도달성 증명이 " +
                            "못 봅니다). 로컬 변수를 쓰세요. 수치 조정 방식은 별도로 정해집니다."));
                    }
                }
            }
        }

        // 설정노드의 <b>배정</b>도 같은 자리다 (2026-08-17) — Set_ 노드 본문이 되어 실제로
        // `<<set>>`이 나간다. 대사 줄만 훑던 검사가 이 길을 놓치고 있었다: 작가가 변수 칸에
        // 손으로 `trust`를 적으면(후보에서는 뺐지만 자유 입력은 막지 않는다) 조용히 새어 나갔다.
        foreach (SetNode setNode in (file?.Nodes ?? []).OfType<SetNode>())
        {
            if (file is not null && IsConditionSupplyNode(setNode, file))
            {
                continue; // A계층 공급 노드는 배관이다 — 여기 조건만 있고 배정은 없다
            }

            foreach (VariableAssignment assignment in setNode.Assignments)
            {
                string variable = assignment.Variable.TrimStart('$');

                if (statKeys.Contains(variable))
                {
                    warnings.Add(new ChapterDiagnostic(
                        ChapterDiagnosticSeverity.Warning,
                        ChapterDiagnosticCode.StatKeyUnknown,
                        chapter.SourcePath, null, null, null,
                        $"설정노드 '{setNode.Name}'이 스탯 '{variable}'을 배정합니다 — 이 배정은 " +
                        "Set_ 노드 본문으로 나가 실제로 스탯을 바꿉니다. 스탯이 변하는 자리는 " +
                        "챕터 `간선` 시트의 `스탯변화` 하나뿐입니다(A계층). 작가의 변수는 " +
                        "따로 두세요."));
                }
            }
        }

        return warnings;
    }

    /// <summary>
    /// 가드레일 — 판 위 노드의 출구(기본·갈래)가 엑셀노드를 가리키면 경고 (소유자 결정
    /// 2026-08-14). 에피소드 사이의 흐름은 챕터 간선이 소유하므로, 이 점프는 챕터 장부
    /// (표시/해금 검사·에피소드 끝 스탯 환산·cleared 기록)를 전부 지나친다. 게다가 Yarn
    /// 점프는 노드 처음부터 다시 재생하므로 같은 에피소드로의 "복귀"도 함정이다.
    /// 편집기는 후보에서 빼지만, 이미 있는 연결(옛 프로젝트)은 막지 않고 크게 말한다.
    /// </summary>
    public static IReadOnlyList<ChapterDiagnostic> WarnExitsIntoExcelNodes(
        ProjectEditor editor,
        string fileId,
        ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(chapter);

        Dictionary<string, DialogueNode> excelNodes = editor.Project.EnumerateNodes()
            .OfType<DialogueNode>()
            .Where(node => node.ExcelEpisodeId is not null)
            .ToDictionary(node => node.Id, node => node, StringComparer.Ordinal);

        if (excelNodes.Count == 0)
        {
            return Array.Empty<ChapterDiagnostic>();
        }

        StoryFile? file = editor.Project.Files.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, fileId, StringComparison.Ordinal));

        var warnings = new List<ChapterDiagnostic>();

        foreach (DialogueNode dialogue in (file?.Nodes ?? []).OfType<DialogueNode>())
        {
            void Check(string? targetId, string exitKind)
            {
                if (targetId is not null && excelNodes.TryGetValue(targetId, out DialogueNode? target))
                {
                    warnings.Add(new ChapterDiagnostic(
                        ChapterDiagnosticSeverity.Warning,
                        ChapterDiagnosticCode.ExitIntoExcelNode,
                        chapter.SourcePath, null, null, null,
                        $"노드 '{dialogue.Name}'의 {exitKind}가 엑셀노드 '{target.Name}'을 가리킵니다 — " +
                        "에피소드 사이의 흐름은 챕터 간선(기획자)이 정합니다. 이 점프는 표시/해금·" +
                        "cleared 기록을 지나치고, 도착 노드를 처음부터 다시 재생합니다. 출구를 " +
                        "자유 노드로 바꾸거나 비워 주세요(비우면 에피소드 종료)."));
                }
            }

            Check(dialogue.EffectiveDefaultExit, "기본 출구");

            foreach ((_, string targetId) in dialogue.BranchExits)
            {
                Check(targetId, "갈래 출구");
            }

            foreach ((_, string targetId) in dialogue.ChoiceExits)
            {
                Check(targetId, "선택지 출구");
            }
        }

        return warnings;
    }
}
