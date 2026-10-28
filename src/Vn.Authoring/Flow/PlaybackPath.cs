using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>
/// 재생이 타는 실행 경로 (W39) — "이 노드에서 어떤 라인을 순서대로 재생하고,
/// 끝나면 어느 노드로 가는가"를 갈래 선택 기준으로 계산한다.
///
/// 갈래 출구는 여는 줄이 소유하지만 실행은 갈래가 닫히는 순간이다 — 발행문 합성
/// (<see cref="Rendering.ResultDocumentComposer"/>)과 같은 규칙. 그래서 확정 선택된
/// 갈래에 출구가 있으면 경로는 그 갈래의 마지막 타는 라인에서 끊기고 그 출구가
/// 다음 노드가 된다. 미선택(근사) 블록의 출구는 태우지 않는다 — 어느 갈래를 탈지
/// 모르는데 출구만 태우면 추측이 된다. 출구를 탄 갈래가 없으면 기본 출구다.
///
/// 작업 대본(DialogueLine)과 발행 결과(DialogueResultLine)가 델리게이트로 같은 구현
/// 하나를 지난다 — <see cref="BranchFlow"/>와 같은 모양(사본 금지).
/// </summary>
public static class PlaybackPath
{
    /// <param name="LineIds">타는 라인의 LineId, 문서 순서. 갈래 출구를 타면 거기서 끊긴다.</param>
    /// <param name="ExitTargetNodeId">경로 끝에서 넘어갈 노드. null이면 재생은 여기서 멈춘다.</param>
    /// <param name="ExitViaBranch">
    /// 갈래 출구로 나가는가(false = 기본 출구 또는 출구 없음). ⚠ 갈래 출구는 계약상
    /// <c>&lt;&lt;detour&gt;&gt;</c>다 — <b>재생하고 돌아온다</b>. 그래서 재생은 이 값이
    /// true일 때 돌아올 자리를 기억해 둔다(2026-08-22).
    /// </param>
    /// <param name="ExitOwnerLineId">
    /// 그 갈래 출구를 소유한(갈래를 여는) 줄. 돌아온 뒤 <b>같은 출구를 또 타지 않도록</b>
    /// 이 Id를 <c>spentBranchExits</c>로 다시 넘긴다 — 안 그러면 나갔다 돌아오기를 무한히 돈다.
    /// </param>
    public sealed record Result(
        IReadOnlyList<string> LineIds,
        string? ExitTargetNodeId,
        bool ExitViaBranch,
        string? ExitOwnerLineId = null);

    /// <param name="branchExitOf">갈래를 여는 줄이 소유한 출구. 그 외 라인에서는 null.</param>
    /// <param name="spentBranchExits">
    /// 이미 다녀온 detour의 소유 줄들 (2026-08-22). 여기 있는 줄의 출구는 <b>없는 것으로
    /// 친다</b> — 경로가 그 갈래를 지나 <b>나머지 대본으로 이어진다</b>. 계약의 detour
    /// 의미(씬을 재생하고 갈래로 돌아와 나머지를 계속한다)가 이 한 칸에 들어 있다.
    /// </param>
    public static Result Trace<TLine>(
        IReadOnlyList<TLine> lines,
        Func<TLine, ConditionTransitionKind?> kindOf,
        Func<TLine, string> lineIdOf,
        Func<TLine, string?> branchExitOf,
        string? defaultExitTargetNodeId,
        StageBranchSelection selection,
        string? cursorLineId,
        IReadOnlySet<string>? spentBranchExits = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(kindOf);
        ArgumentNullException.ThrowIfNull(lineIdOf);
        ArgumentNullException.ThrowIfNull(branchExitOf);
        ArgumentNullException.ThrowIfNull(selection);

        // 경로에 라벨은 필요 없다 — LineId로 채워 Analyze의 문법 해석만 빌린다.
        BranchFlow.Analysis<TLine> analysis = BranchFlow.Analyze(
            lines, kindOf, lineIdOf, lineIdOf, selection, cursorLineId);

        // 라인이 어느 블록·갈래에 속하는지는 BranchFlow와 같은 문법 해석 하나로 얻는다.
        // 프레임(W54)이라 조건 안 선택지 줄도 바깥 조건 갈래에 계속 속한다 — 바깥 갈래의
        // 출구는 안의 선택지를 다 지나고 나서야 실행된다.
        (_, IReadOnlyList<(int BlockIndex, int BranchIndex)>[] framesOfLine) =
            BranchFlow.BuildStructure(lines, kindOf, lineIdOf, lineIdOf);

        var lineIds = new List<string>();
        (int Block, int Branch, string Target, string Owner)? pendingExit = null;

        for (int index = 0; index < lines.Count; index++)
        {
            IReadOnlyList<(int BlockIndex, int BranchIndex)> frames = framesOfLine[index];

            // 대기 중인 출구는 그 갈래를 벗어나는 순간 실행된다 — 경로는 여기서 끝.
            if (pendingExit is { } pending &&
                !frames.Any(frame => frame.BlockIndex == pending.Block && frame.BranchIndex == pending.Branch))
            {
                return new Result(lineIds, pending.Target, ExitViaBranch: true, pending.Owner);
            }

            BranchFlow.AnalyzedLine<TLine> line = analysis.Lines[index];

            if (!line.Taken && !line.Unresolved)
            {
                continue;
            }

            lineIds.Add(lineIdOf(line.Source));

            // 갈래를 여는 줄이 출구를 소유한다 — 확정 선택된(근사 아님) 갈래만 출구를 태운다.
            // 프레임의 안쪽 것이 그 줄 자신의 갈래다.
            if (frames.Count > 0 &&
                !line.Unresolved &&
                line.Taken &&
                branchExitOf(line.Source) is { } target &&
                // 이미 다녀온 detour는 없는 출구다 — 경로가 그 갈래를 지나 이어진다.
                spentBranchExits?.Contains(lineIdOf(line.Source)) != true)
            {
                (int blockIndex, int branchIndex) = frames[^1];

                if (analysis.Blocks[blockIndex].SelectedBranch == branchIndex)
                {
                    pendingExit = (blockIndex, branchIndex, target, lineIdOf(line.Source));
                }
            }
        }

        // 구조가 깨져 갈래가 닫히지 않았어도 출구를 조용히 버리지 않는다(합성기와 같은 규칙).
        return pendingExit is { } last
            ? new Result(lineIds, last.Target, ExitViaBranch: true, last.Owner)
            : new Result(lineIds, defaultExitTargetNodeId, ExitViaBranch: false);
    }
}
