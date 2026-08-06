using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Flow;

/// <summary>
/// 갈래 선택 상태 (W35) — "이 프리뷰는 어느 갈래를 보고 있는가".
///
/// 키는 블록의 시작 라인 LineId다. LineId는 대본과 발행 결과 양쪽에서 영구 불변이라(A-2)
/// 작업 대본 프리뷰와 발행 기준 프리뷰가 같은 선택을 공유한다.
/// 뷰 상태다 — 저장하지 않는다(원칙 E). 선택이 없는 블록은 기존의 문서 순서 근사로
/// 접히고, 그 사실이 뱃지로 남는다.
/// </summary>
public sealed class StageBranchSelection
{
    private readonly Dictionary<string, string> _choiceByBlock = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _conditionByBlock = new(StringComparer.Ordinal);

    /// <summary>조건 블록에서 "모든 갈래가 거짓"(전부 건너뜀)을 뜻하는 선택 값.</summary>
    public const int SkipAllBranches = -1;

    public void SelectChoice(string blockLineId, string optionLineId)
        => _choiceByBlock[blockLineId] = optionLineId;

    public bool TryGetChoice(string blockLineId, out string optionLineId)
        => _choiceByBlock.TryGetValue(blockLineId, out optionLineId!);

    public void SelectCondition(string blockLineId, int branchIndex)
        => _conditionByBlock[blockLineId] = branchIndex;

    public bool TryGetCondition(string blockLineId, out int branchIndex)
        => _conditionByBlock.TryGetValue(blockLineId, out branchIndex);

    /// <summary>칩 클릭 순환: 갈래 0 → 1 → … → (조건만) 건너뜀 → 미선택(근사) → 다시 0.</summary>
    public void Cycle(BranchFlow.Block block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (block.IsChoice)
        {
            int current = TryGetChoice(block.BlockLineId, out string selected)
                ? block.Branches.ToList().FindIndex(branch =>
                    string.Equals(branch.LineId, selected, StringComparison.Ordinal))
                : -1;

            int next = current + 1;

            if (next >= block.Branches.Count)
            {
                _choiceByBlock.Remove(block.BlockLineId); // 미선택 = 근사
            }
            else
            {
                _choiceByBlock[block.BlockLineId] = block.Branches[next].LineId;
            }

            return;
        }

        int currentIndex = TryGetCondition(block.BlockLineId, out int index)
            ? index
            : int.MinValue; // 미선택

        int nextIndex = currentIndex switch
        {
            int.MinValue => 0,
            SkipAllBranches => int.MinValue,
            _ => currentIndex + 1 >= block.Branches.Count ? SkipAllBranches : currentIndex + 1,
        };

        if (nextIndex == int.MinValue)
        {
            _conditionByBlock.Remove(block.BlockLineId);
        }
        else
        {
            _conditionByBlock[block.BlockLineId] = nextIndex;
        }
    }

    public void Clear()
    {
        _choiceByBlock.Clear();
        _conditionByBlock.Clear();
    }

    /// <summary>수동 선택의 사본 — 시뮬(W36-b)이 자동 판정을 얹을 때 원본을 훼손하지 않는다.</summary>
    public StageBranchSelection Clone()
    {
        var clone = new StageBranchSelection();

        foreach ((string blockId, string optionLineId) in _choiceByBlock)
        {
            clone._choiceByBlock[blockId] = optionLineId;
        }

        foreach ((string blockId, int branchIndex) in _conditionByBlock)
        {
            clone._conditionByBlock[blockId] = branchIndex;
        }

        return clone;
    }
}

/// <summary>
/// 갈래 인식 흐름 분석 (W35) — 리듀서 밖의 "어떤 라인을 먹일지 고르는 층"(설계 초안 §6.4).
///
/// 체인은 평면이다(중첩·MixedChain은 유효하지 않은 구성 — 플로우 해석기가 따로 알린다):
/// BeginIf/BeginElseIf…EndIf, BeginChoice/BeginNextOption…EndChoice. Begin 라인부터 그
/// 갈래이고 End 라인부터 일반 흐름이다.
///
/// 선택이 없는 블록은 기존 근사(전부 적용) + Unresolved 표시. <b>커서가 갈래 안에 있으면
/// 그 갈래가 선택을 덮는다</b> — 작가가 보고 있는 라인이 화면에서 사라지면 안 된다.
/// 작업 대본(DialogueLine)과 발행 결과(DialogueResultLine)가 델리게이트로 같은 구현
/// 하나를 지난다(규칙 사본 금지 — ChoiceOptionBundle과 같은 모양).
/// </summary>
public static class BranchFlow
{
    /// <summary>갈래 하나 — 시작 라인과 표시 라벨(선택지=버튼 텍스트, 조건=이름/식).</summary>
    public sealed record Branch(string LineId, string Label);

    /// <summary>블록 하나. <see cref="SelectedBranch"/>는 커서 덮어쓰기까지 반영된 유효 선택이다
    /// (null=미선택(근사), <see cref="StageBranchSelection.SkipAllBranches"/>=전부 건너뜀).</summary>
    public sealed record Block(
        string BlockLineId,
        bool IsChoice,
        IReadOnlyList<Branch> Branches,
        int? SelectedBranch);

    /// <summary>분석된 라인 하나 — 선택 갈래 기준으로 접히는가, 미선택 근사인가.</summary>
    public sealed record AnalyzedLine<TLine>(TLine Source, bool Taken, bool Unresolved);

    public sealed record Analysis<TLine>(
        IReadOnlyList<AnalyzedLine<TLine>> Lines,
        IReadOnlyList<Block> Blocks);

    public static Analysis<TLine> Analyze<TLine>(
        IReadOnlyList<TLine> lines,
        Func<TLine, ConditionTransitionKind?> kindOf,
        Func<TLine, string> lineIdOf,
        Func<TLine, string> labelOf,
        StageBranchSelection selection,
        string? cursorLineId)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(kindOf);
        ArgumentNullException.ThrowIfNull(lineIdOf);
        ArgumentNullException.ThrowIfNull(labelOf);
        ArgumentNullException.ThrowIfNull(selection);

        // 1차: 블록 구조와 커서 위치를 파악한다.
        (List<(string BlockId, bool IsChoice, List<Branch> Branches)> blockStarts,
            (int BlockIndex, int BranchIndex)?[] blockOfLine) = BuildStructure(lines, kindOf, lineIdOf, labelOf);

        (int BlockIndex, int BranchIndex)? cursorBranch = null;

        for (int index = 0; index < lines.Count; index++)
        {
            if (blockOfLine[index] is { } at && cursorLineId is not null &&
                string.Equals(lineIdOf(lines[index]), cursorLineId, StringComparison.Ordinal))
            {
                cursorBranch = at;
            }
        }

        // 2차: 블록별 유효 선택(커서 덮어쓰기 포함)을 정하고 라인에 적용한다.
        var effective = new int?[blockStarts.Count];

        for (int blockIndex = 0; blockIndex < blockStarts.Count; blockIndex++)
        {
            (string blockId, bool isChoice, List<Branch> branches) = blockStarts[blockIndex];

            if (cursorBranch is { } cursor && cursor.BlockIndex == blockIndex)
            {
                effective[blockIndex] = cursor.BranchIndex; // 보고 있는 라인의 갈래가 이긴다
            }
            else if (isChoice)
            {
                effective[blockIndex] =
                    selection.TryGetChoice(blockId, out string optionLineId) &&
                    branches.FindIndex(branch =>
                        string.Equals(branch.LineId, optionLineId, StringComparison.Ordinal)) is var found and >= 0
                        ? found
                        : null;
            }
            else
            {
                effective[blockIndex] = selection.TryGetCondition(blockId, out int branchIndex)
                    ? branchIndex
                    : null;
            }
        }

        var analyzed = new AnalyzedLine<TLine>[lines.Count];

        for (int index = 0; index < lines.Count; index++)
        {
            if (blockOfLine[index] is not { } at)
            {
                analyzed[index] = new AnalyzedLine<TLine>(lines[index], Taken: true, Unresolved: false);
                continue;
            }

            int? selected = effective[at.BlockIndex];

            analyzed[index] = selected is null
                ? new AnalyzedLine<TLine>(lines[index], Taken: true, Unresolved: true)
                : new AnalyzedLine<TLine>(lines[index], Taken: at.BranchIndex == selected, Unresolved: false);
        }

        Block[] blocks = blockStarts
            .Select((start, blockIndex) => new Block(
                start.BlockId, start.IsChoice, start.Branches, effective[blockIndex]))
            .ToArray();

        return new Analysis<TLine>(analyzed, blocks);
    }

    /// <summary>
    /// 평면 체인의 블록 구조 — Analyze와 조건 값 시뮬(<see cref="ConditionSimulation"/>)이
    /// 같은 문법 해석 하나를 쓴다(사본 금지).
    /// </summary>
    internal static (
        List<(string BlockId, bool IsChoice, List<Branch> Branches)> BlockStarts,
        (int BlockIndex, int BranchIndex)?[] BlockOfLine)
        BuildStructure<TLine>(
            IReadOnlyList<TLine> lines,
            Func<TLine, ConditionTransitionKind?> kindOf,
            Func<TLine, string> lineIdOf,
            Func<TLine, string> labelOf)
    {
        var blockStarts = new List<(string BlockId, bool IsChoice, List<Branch> Branches)>();
        var blockOfLine = new (int BlockIndex, int BranchIndex)?[lines.Count];

        int currentBlock = -1;
        int currentBranch = -1;

        for (int index = 0; index < lines.Count; index++)
        {
            ConditionTransitionKind? kind = kindOf(lines[index]);

            switch (kind)
            {
                case ConditionTransitionKind.EndIf or ConditionTransitionKind.EndChoice:
                    currentBlock = -1; // End 라인부터 일반 흐름
                    break;

                case ConditionTransitionKind.BeginIf or ConditionTransitionKind.BeginChoice:
                    blockStarts.Add((
                        lineIdOf(lines[index]),
                        kind is ConditionTransitionKind.BeginChoice,
                        new List<Branch> { new(lineIdOf(lines[index]), labelOf(lines[index])) }));
                    currentBlock = blockStarts.Count - 1;
                    currentBranch = 0;
                    break;

                case ConditionTransitionKind.BeginElseIf or ConditionTransitionKind.BeginNextOption:
                    if (currentBlock < 0)
                    {
                        // 깨진 구조(시작 없는 갈래) — 새 블록으로 취급해 조용히 삼키지 않는다.
                        blockStarts.Add((
                            lineIdOf(lines[index]),
                            kind is ConditionTransitionKind.BeginNextOption,
                            new List<Branch>()));
                        currentBlock = blockStarts.Count - 1;
                        currentBranch = -1;
                    }

                    blockStarts[currentBlock].Branches.Add(new Branch(lineIdOf(lines[index]), labelOf(lines[index])));
                    currentBranch = blockStarts[currentBlock].Branches.Count - 1;
                    break;
            }

            if (currentBlock >= 0)
            {
                blockOfLine[index] = (currentBlock, currentBranch);
            }
        }

        return (blockStarts, blockOfLine);
    }

    /// <summary>커서까지 지나온 블록만 — 아직 닿지 않은 갈래는 고를 것도 없다(칩 노출용).</summary>
    public static IReadOnlyList<Block> PassedBlocks<TLine>(
        Analysis<TLine> analysis,
        Func<TLine, string> lineIdOf,
        string? cursorLineId)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(lineIdOf);

        var scanned = new HashSet<string>(StringComparer.Ordinal);

        foreach (AnalyzedLine<TLine> line in analysis.Lines)
        {
            scanned.Add(lineIdOf(line.Source));

            if (string.Equals(lineIdOf(line.Source), cursorLineId, StringComparison.Ordinal))
            {
                break;
            }
        }

        return analysis.Blocks
            .Where(block => scanned.Contains(block.BlockLineId))
            .ToArray();
    }
}

/// <summary>
/// 발행 결과에서 갈래 인식 폴드 입력을 만든다 (W35) — <see cref="MiniStageFold.LinesUpTo"/>의
/// 갈래 인식 판. 선택 라인까지(포함) 자르고, 선택된 갈래의 라인만 폴드에 먹인다.
/// 미선택 블록의 라인은 기존 근사대로 전부 먹이되 HasBranchTransition으로 표시된다 —
/// 그래서 <see cref="MiniStageState.PassedBranchApproximation"/>이 "미선택 갈래가 있다"는
/// 정확한 뜻이 된다.
/// </summary>
public static class BranchAwareLines
{
    public sealed record Result(
        IReadOnlyList<MiniStageFoldLine> FoldLines,
        IReadOnlyList<DialogueResultLine> TakenLines,
        IReadOnlyList<BranchFlow.Block> Blocks);

    public static Result UpTo(
        DialogueResult dialogue,
        IReadOnlyList<PresentationResultBinding> bindings,
        string? selectedLineId,
        StageBranchSelection selection)
    {
        ArgumentNullException.ThrowIfNull(dialogue);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(selection);

        BranchFlow.Analysis<DialogueResultLine> analysis = BranchFlow.Analyze(
            dialogue.Lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            line => line.Transition?.Kind is ConditionTransitionKind.BeginChoice or ConditionTransitionKind.BeginNextOption
                ? line.Text
                : line.Transition?.ConditionName is { Length: > 0 } name
                    ? name
                    : line.Transition?.Expression ?? string.Empty,
            selection,
            selectedLineId is not null && dialogue.ContainsLine(selectedLineId) ? selectedLineId : null);

        var foldLines = new List<MiniStageFoldLine>();
        var takenLines = new List<DialogueResultLine>();
        var scannedLineIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (BranchFlow.AnalyzedLine<DialogueResultLine> line in analysis.Lines)
        {
            scannedLineIds.Add(line.Source.LineId);

            if (line.Taken || line.Unresolved) // 미선택 근사는 기존대로 전부
            {
                PresentationResultBinding? binding = bindings.FirstOrDefault(item =>
                    string.Equals(item.LineId, line.Source.LineId, StringComparison.Ordinal));

                foldLines.Add(new MiniStageFoldLine(
                    line.Source.LineId,
                    line.Unresolved,
                    binding?.Commands ?? Array.Empty<PresentationResultCommand>()));
                takenLines.Add(line.Source);
            }

            if (string.Equals(line.Source.LineId, selectedLineId, StringComparison.Ordinal))
            {
                break;
            }
        }

        // 지나온 블록만 칩으로 노출한다 — 아직 닿지 않은 갈래는 고를 것도 없다.
        BranchFlow.Block[] passedBlocks = analysis.Blocks
            .Where(block => scannedLineIds.Contains(block.BlockLineId))
            .ToArray();

        return new Result(foldLines, takenLines, passedBlocks);
    }
}
