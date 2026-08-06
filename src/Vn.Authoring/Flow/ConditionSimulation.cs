using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>
/// 조건 값 시뮬 (W36-b) — "변수가 이 값이면 어느 갈래를 타는가"를 문서 순서대로 판정한다.
///
/// 규칙:
/// - 수동 선택(칩·라벨 클릭)이 있으면 그것이 이긴다 — 자동은 빈자리에만 들어간다.
/// - 조건 블록은 갈래 식을 앞에서부터 평가해 <b>참인 첫 갈래</b>(if/elseif 의미), 전부
///   거짓이면 건너뜀. 갈래 식 하나라도 평가 불가(지원 밖 문법·미지 변수)면 그 블록은
///   자동 판정 없이 남는다 — 추측하지 않는다.
/// - 선택지 블록은 자동 판정이 없다(플레이어의 몫).
/// - 변수 값은 등록 초기값(+시뮬 시작값 오버라이드)에서 시작해, 그때까지 정해진 갈래
///   기준으로 set을 누적한다(<see cref="StatFold.Apply"/> 한 벌). 미결정 블록의 set은
///   기존 근사대로 전부 적용된다(HUD와 같은 규칙).
///
/// 순수 함수이고, 구조 해석은 <see cref="BranchFlow"/>의 것 하나를 쓴다(사본 금지).
/// </summary>
public static class ConditionSimulation
{
    public sealed record Result(
        /// <summary>수동 + 자동을 합친 유효 선택 — 폴드·목록·칩이 이것을 쓴다.</summary>
        StageBranchSelection Effective,
        /// <summary>자동으로 판정된 조건 블록 id — 칩이 "(자동)"을 단다.</summary>
        IReadOnlySet<string> AutoBlocks,
        /// <summary>평가 불가로 남은 조건 블록 id — 칩 툴팁이 이유를 단다.</summary>
        IReadOnlySet<string> UndecidableBlocks);

    public static Result Decide<TLine>(
        IReadOnlyList<TLine> lines,
        Func<TLine, ConditionTransitionKind?> kindOf,
        Func<TLine, string> lineIdOf,
        Func<TLine, string?> expressionOf,
        Func<TLine, IEnumerable<(string Variable, SetOperatorKind Operator, string Value)>> setsOf,
        IEnumerable<(string Variable, string Value)> initialValues,
        StageBranchSelection manual)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(kindOf);
        ArgumentNullException.ThrowIfNull(lineIdOf);
        ArgumentNullException.ThrowIfNull(expressionOf);
        ArgumentNullException.ThrowIfNull(setsOf);
        ArgumentNullException.ThrowIfNull(initialValues);
        ArgumentNullException.ThrowIfNull(manual);

        (List<(string BlockId, bool IsChoice, List<BranchFlow.Branch> Branches)> blockStarts,
            (int BlockIndex, int BranchIndex)?[] blockOfLine) =
            BranchFlow.BuildStructure(lines, kindOf, lineIdOf, lineIdOf);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string variable, string value) in initialValues)
        {
            if (variable.Length > 0)
            {
                values.TryAdd(variable, value);
            }
        }

        var lineByLineId = new Dictionary<string, TLine>(StringComparer.Ordinal);

        foreach (TLine line in lines)
        {
            lineByLineId.TryAdd(lineIdOf(line), line);
        }

        StageBranchSelection effective = manual.Clone();
        var autoBlocks = new HashSet<string>(StringComparer.Ordinal);
        var undecidable = new HashSet<string>(StringComparer.Ordinal);
        var decisions = new int?[blockStarts.Count]; // null = 미결정(근사)
        var decided = new bool[blockStarts.Count];

        for (int index = 0; index < lines.Count; index++)
        {
            TLine line = lines[index];

            if (blockOfLine[index] is not { } at)
            {
                ApplySets(values, setsOf(line));
                continue;
            }

            (string blockId, bool isChoice, List<BranchFlow.Branch> branches) = blockStarts[at.BlockIndex];

            // 블록에 처음 닿는 순간 — 그 시점 값으로 판정한다.
            if (!decided[at.BlockIndex])
            {
                decided[at.BlockIndex] = true;
                decisions[at.BlockIndex] = DecideBlock(
                    blockId, isChoice, branches, lineByLineId, expressionOf,
                    values, manual, effective, autoBlocks, undecidable);
            }

            int? decision = decisions[at.BlockIndex];
            bool taken = decision is null || at.BranchIndex == decision;

            if (taken)
            {
                ApplySets(values, setsOf(line));
            }
        }

        return new Result(effective, autoBlocks, undecidable);
    }

    private static int? DecideBlock<TLine>(
        string blockId,
        bool isChoice,
        List<BranchFlow.Branch> branches,
        IReadOnlyDictionary<string, TLine> lineByLineId,
        Func<TLine, string?> expressionOf,
        IReadOnlyDictionary<string, string> values,
        StageBranchSelection manual,
        StageBranchSelection effective,
        HashSet<string> autoBlocks,
        HashSet<string> undecidable)
    {
        // 수동이 이긴다.
        if (isChoice)
        {
            if (manual.TryGetChoice(blockId, out string optionLineId))
            {
                int manualIndex = branches.FindIndex(branch =>
                    string.Equals(branch.LineId, optionLineId, StringComparison.Ordinal));
                return manualIndex >= 0 ? manualIndex : null;
            }

            return null; // 선택지는 자동 판정이 없다 — 플레이어의 몫
        }

        if (manual.TryGetCondition(blockId, out int manualBranch))
        {
            return manualBranch;
        }

        // 자동: 참인 첫 갈래, 전부 거짓이면 건너뜀. 하나라도 평가 불가면 판정하지 않는다.
        var results = new bool[branches.Count];

        for (int branchIndex = 0; branchIndex < branches.Count; branchIndex++)
        {
            if (!lineByLineId.TryGetValue(branches[branchIndex].LineId, out TLine? branchLine) ||
                branchLine is null ||
                !ConditionExpression.TryEvaluate(expressionOf(branchLine), values, out results[branchIndex]))
            {
                undecidable.Add(blockId);
                return null;
            }
        }

        int decision = Array.IndexOf(results, true) is var first and >= 0
            ? first
            : StageBranchSelection.SkipAllBranches;

        effective.SelectCondition(blockId, decision);
        autoBlocks.Add(blockId);
        return decision;
    }

    private static void ApplySets(
        Dictionary<string, string> values,
        IEnumerable<(string Variable, SetOperatorKind Operator, string Value)> sets)
    {
        foreach ((string variable, SetOperatorKind op, string value) in sets)
        {
            StatFold.Apply(values, variable, op, value);
        }
    }
}
