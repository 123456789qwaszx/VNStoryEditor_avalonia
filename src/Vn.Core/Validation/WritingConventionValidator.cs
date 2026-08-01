using Vn.Core.Diagnostics;
using Vn.Core.Story;

namespace Vn.Core.Validation;

/// <summary>
/// 작성 규약 검사. 전부 Warning이다.
///
/// 파서는 관대하게, 작성기는 엄격하게. 이 <c>.yarn</c>은 Unity 안에서도, 이전 도구에서도,
/// AI가 연출을 붙이면서도 편집되므로 규약을 지키지 않는 파일이 반드시 존재한다.
/// 못 읽으면 작가가 자기 파일을 열 수 없게 되므로, 읽어낸 뒤 알리기만 한다.
///
/// 규약 위반은 "틀린 것"이 아니라 "이 툴이 편하게 다루기 어려운 것"이다.
/// 그래서 Error가 아니라 Warning이고, 종료 코드를 바꾸지 않는다.
/// </summary>
internal static class WritingConventionValidator
{
    public static IReadOnlyList<VnDiagnostic> Validate(IReadOnlyList<StoryNode> nodes)
    {
        var diagnostics = new List<VnDiagnostic>();

        foreach (StoryNode node in nodes)
        {
            Walk(node.Body, node.FilePath, diagnostics);
        }

        return diagnostics;
    }

    private static void Walk(
        IReadOnlyList<StoryElement> elements,
        string filePath,
        ICollection<VnDiagnostic> diagnostics)
    {
        foreach (StoryElement element in elements)
        {
            if (element is not StoryBlockElement blockElement)
            {
                continue;
            }

            StoryBlock block = blockElement.Block;

            foreach (StoryBranch branch in block.Branches)
            {
                if (block.Kind == StoryBlockKind.Option)
                {
                    Check(branch, filePath, diagnostics);
                }

                Walk(branch.Children, filePath, diagnostics);
            }
        }
    }

    private static void Check(
        StoryBranch branch,
        string filePath,
        ICollection<VnDiagnostic> diagnostics)
    {
        if (!HasAnyLine(branch.Children))
        {
            diagnostics.Add(new VnDiagnostic(
                VnDiagnosticCodes.OptionBranchHasNoLine,
                DiagnosticSeverity.Warning,
                $"선택지 '{branch.Label}'에 대사가 없습니다. " +
                "고르고 나면 아무 말도 나오지 않습니다.",
                filePath,
                branch.Line,
                1));
        }

        int jumps = branch.Commands.Count(IsJump);

        if (jumps > 1)
        {
            diagnostics.Add(new VnDiagnostic(
                VnDiagnosticCodes.OptionBranchHasManyJumps,
                DiagnosticSeverity.Warning,
                $"선택지 '{branch.Label}' 안에 <<jump>>가 {jumps}개 있습니다. " +
                "어디로 이어지는지 하나로 정해지지 않아 목적지를 잡지 못합니다.",
                filePath,
                branch.Line,
                1));

            // 목적지가 없는 이유를 이미 말했다. 같은 자리에 두 번 말하지 않는다.
            return;
        }

        if (branch.Destination is null)
        {
            diagnostics.Add(new VnDiagnostic(
                VnDiagnosticCodes.OptionBranchHasNoDestination,
                DiagnosticSeverity.Warning,
                $"선택지 '{branch.Label}'에 목적지가 없습니다. " +
                "갈래가 끝나면 그룹 다음으로 흘러갑니다. 그것이 의도였다면 무시하세요.",
                filePath,
                branch.Line,
                1));
        }
    }

    /// <summary>
    /// 갈래 안 어딘가에 대사가 하나라도 있는지. 중첩된 블록 안까지 본다.
    /// 조건문으로 감싼 대사도 "고르면 나오는 말"이므로 대사가 있는 것으로 친다.
    /// </summary>
    private static bool HasAnyLine(IReadOnlyList<StoryElement> elements)
    {
        foreach (StoryElement element in elements)
        {
            if (element is StoryLineElement)
            {
                return true;
            }

            if (element is StoryBlockElement blockElement &&
                blockElement.Block.Branches.Any(branch => HasAnyLine(branch.Children)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJump(StoryCommand command)
    {
        string raw = command.Raw;

        if (!raw.StartsWith("<<", StringComparison.Ordinal) ||
            !raw.EndsWith(">>", StringComparison.Ordinal))
        {
            return false;
        }

        string inner = raw[2..^2].TrimStart();

        return inner.StartsWith("jump", StringComparison.Ordinal) &&
            (inner.Length == 4 || char.IsWhiteSpace(inner[4]));
    }
}
