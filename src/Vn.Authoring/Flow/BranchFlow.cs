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
/// 체인: BeginIf/BeginElseIf…EndIf, BeginChoice/BeginNextOption…EndChoice.
/// Begin 라인부터 그 갈래이고 End 라인부터 바깥이다. <b>조건 갈래 안의 선택 블록은
/// 정식 구성이다 (W54)</b> — 라인은 자신을 감싼 블록 전부(프레임)를 알고, 접히려면
/// 감싼 모든 블록에서 그 갈래가 선택돼야 한다. 그 밖의 중첩(조건 안 조건 등)은
/// 유효하지 않은 구성으로 플로우 해석기가 따로 알린다.
///
/// 선택이 없는 블록은 기존 근사(전부 적용) + Unresolved 표시. <b>커서가 갈래 안에 있으면
/// 그 갈래(를 감싼 전부)가 선택을 덮는다</b> — 보고 있는 라인이 화면에서 사라지면 안 된다.
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

        // 1차: 블록 구조와 커서의 프레임(감싼 블록 전부)을 파악한다.
        (List<(string BlockId, bool IsChoice, List<Branch> Branches)> blockStarts,
            IReadOnlyList<(int BlockIndex, int BranchIndex)>[] framesOfLine) =
            BuildStructure(lines, kindOf, lineIdOf, labelOf);

        IReadOnlyList<(int BlockIndex, int BranchIndex)> cursorFrames =
            Array.Empty<(int, int)>();

        for (int index = 0; index < lines.Count; index++)
        {
            if (framesOfLine[index].Count > 0 && cursorLineId is not null &&
                string.Equals(lineIdOf(lines[index]), cursorLineId, StringComparison.Ordinal))
            {
                cursorFrames = framesOfLine[index];
            }
        }

        // 2차: 블록별 유효 선택(커서 덮어쓰기 포함)을 정하고 라인에 적용한다.
        var effective = new int?[blockStarts.Count];

        for (int blockIndex = 0; blockIndex < blockStarts.Count; blockIndex++)
        {
            (string blockId, bool isChoice, List<Branch> branches) = blockStarts[blockIndex];

            if (isChoice)
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

        // 보고 있는 라인의 갈래가 이긴다 — 감싼 블록 전부를 그 라인의 프레임으로 덮는다.
        foreach ((int blockIndex, int branchIndex) in cursorFrames)
        {
            effective[blockIndex] = branchIndex;
        }

        var analyzed = new AnalyzedLine<TLine>[lines.Count];

        for (int index = 0; index < lines.Count; index++)
        {
            IReadOnlyList<(int BlockIndex, int BranchIndex)> frames = framesOfLine[index];

            if (frames.Count == 0)
            {
                analyzed[index] = new AnalyzedLine<TLine>(lines[index], Taken: true, Unresolved: false);
                continue;
            }

            // 접히려면 감싼 모든 블록에서 이 라인의 갈래가 선택돼야 한다 (W54).
            // 하나라도 다른 갈래가 선택됐으면 안 타고, 미선택 블록이 남았으면 근사다.
            bool mismatch = false;
            bool unresolved = false;

            foreach ((int blockIndex, int branchIndex) in frames)
            {
                int? selected = effective[blockIndex];

                if (selected is null)
                {
                    unresolved = true;
                }
                else if (selected != branchIndex)
                {
                    mismatch = true;
                }
            }

            analyzed[index] = mismatch
                ? new AnalyzedLine<TLine>(lines[index], Taken: false, Unresolved: false)
                : new AnalyzedLine<TLine>(lines[index], Taken: true, Unresolved: unresolved);
        }

        Block[] blocks = blockStarts
            .Select((start, blockIndex) => new Block(
                start.BlockId, start.IsChoice, start.Branches, effective[blockIndex]))
            .ToArray();

        return new Analysis<TLine>(analyzed, blocks);
    }

    /// <summary>
    /// 블록 구조 — Analyze와 조건 값 시뮬(<see cref="ConditionSimulation"/>),
    /// 재생 경로(<see cref="PlaybackPath"/>)가 같은 문법 해석 하나를 쓴다(사본 금지).
    ///
    /// 라인마다 <b>프레임 목록</b>(바깥→안 순서로 감싼 (블록, 갈래) 전부)을 돌려준다 (W54).
    /// 평면 문서에서는 프레임이 0~1개라 이전 의미와 같다. 조건 갈래 안 선택 블록은
    /// 프레임 2개(조건, 선택)가 된다.
    /// </summary>
    internal static (
        List<(string BlockId, bool IsChoice, List<Branch> Branches)> BlockStarts,
        IReadOnlyList<(int BlockIndex, int BranchIndex)>[] FramesOfLine)
        BuildStructure<TLine>(
            IReadOnlyList<TLine> lines,
            Func<TLine, ConditionTransitionKind?> kindOf,
            Func<TLine, string> lineIdOf,
            Func<TLine, string> labelOf)
    {
        var blockStarts = new List<(string BlockId, bool IsChoice, List<Branch> Branches)>();
        var framesOfLine = new IReadOnlyList<(int BlockIndex, int BranchIndex)>[lines.Count];
        var empty = (IReadOnlyList<(int, int)>)Array.Empty<(int, int)>();

        // 스택: [0]=바깥(조건), [1]=안(선택). 유효 구성에서는 최대 2단이다.
        var stack = new List<(int BlockIndex, int BranchIndex)>();

        void PushNewBlock(TLine line, bool isChoice, bool withBranch)
        {
            blockStarts.Add((
                lineIdOf(line),
                isChoice,
                withBranch
                    ? new List<Branch> { new(lineIdOf(line), labelOf(line)) }
                    : new List<Branch>()));
            stack.Add((blockStarts.Count - 1, withBranch ? 0 : -1));
        }

        void SwitchTopBranch(TLine line)
        {
            (int blockIndex, _) = stack[^1];
            blockStarts[blockIndex].Branches.Add(new Branch(lineIdOf(line), labelOf(line)));
            stack[^1] = (blockIndex, blockStarts[blockIndex].Branches.Count - 1);
        }

        for (int index = 0; index < lines.Count; index++)
        {
            TLine line = lines[index];
            ConditionTransitionKind? kind = kindOf(line);

            switch (kind)
            {
                case ConditionTransitionKind.EndIf:
                    stack.Clear(); // 조건 종료는 안의 것까지 전부 닫는다 — End 라인부터 일반 흐름
                    break;

                case ConditionTransitionKind.EndChoice:
                    // 선택만 닫는다 — 조건 갈래 안이었다면 그 갈래로 돌아간다 (W54).
                    if (stack.Count > 0 && blockStarts[stack[^1].BlockIndex].IsChoice)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                    else
                    {
                        stack.Clear();
                    }

                    break;

                case ConditionTransitionKind.BeginIf:
                    stack.Clear(); // 조건 중첩은 비지원 — 해석기가 알리고, 여기서는 새 블록으로 시작
                    PushNewBlock(line, isChoice: false, withBranch: true);
                    break;

                case ConditionTransitionKind.BeginChoice:
                    if (stack.Count > 0 && blockStarts[stack[^1].BlockIndex].IsChoice)
                    {
                        SwitchTopBranch(line); // 선택 안 선택은 비지원 — 같은 블록의 다음 옵션 취급
                    }
                    else
                    {
                        PushNewBlock(line, isChoice: true, withBranch: true); // 바깥 또는 조건 안 (W54)
                    }

                    break;

                case ConditionTransitionKind.BeginElseIf:
                case ConditionTransitionKind.BeginNextOption:
                    if (stack.Count == 0)
                    {
                        // 깨진 구조(시작 없는 갈래) — 새 블록으로 취급해 조용히 삼키지 않는다.
                        PushNewBlock(line, kind is ConditionTransitionKind.BeginNextOption, withBranch: false);
                    }

                    SwitchTopBranch(line);
                    break;
            }

            framesOfLine[index] = stack.Count == 0 ? empty : stack.ToArray();
        }

        return (blockStarts, framesOfLine);
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
