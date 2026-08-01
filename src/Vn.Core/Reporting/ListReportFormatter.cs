using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Core.Story;

namespace Vn.Core.Reporting;

/// <summary>
/// 회귀 비교용 출력.
///
/// 골든 픽스처를 사람이 읽는 문장에 걸면, 메시지 문구를 다듬을 때마다 픽스처가 전부 깨진다.
/// 그런데 이 도구에서 메시지 문구는 자주 다듬어야 하는 것이다 — 작가가 읽을 문장이니까.
/// 픽스처가 문구 수정에 저항하게 만들면 결국 문구를 안 고치게 된다.
///
/// 그래서 회귀의 본체는 여기, 문구가 빠진 형태에 둔다.
/// 코드·심각도·파일·줄·열만으로 드리프트는 전부 잡힌다.
/// 메시지 품질은 픽스처가 아니라 사람이 text 출력을 눈으로 보고 판단할 일이다.
///
/// 콘솔이 아니라 문자열 목록을 돌려주므로, CLI를 거치지 않고도 같은 결과를 테스트에서 비교할 수 있다.
/// 골든 비교가 셸의 콘솔 코드 페이지에 좌우되지 않게 하려면 이 경계가 필요하다.
/// </summary>
public static class ListReportFormatter
{
    public static IReadOnlyList<string> Format(AnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        string root = StablePath.RootFor(report.ProjectPath);
        var lines = new List<string>();

        foreach (string source in report.SourceFiles)
        {
            lines.Add($"source\t{StablePath.ToStable(source, root)}");
        }

        foreach (StoryNode node in report.Nodes)
        {
            lines.Add(
                $"node\t{node.Title}\t{StablePath.ToStable(node.FilePath, root)}\t{node.HeaderLine}");

            foreach (StoryJump jump in node.Jumps)
            {
                lines.Add(
                    $"jump\t{jump.SourceNodeTitle}\t{jump.DestinationNodeTitle}\t" +
                    $"{StablePath.ToStable(jump.FilePath, root)}\t{jump.Line}\t{jump.Column}");
            }

            // 텍스트는 넣지 않는다. 픽스처가 대사 문구에 걸리면 문구를 고칠 때마다 깨진다.
            // 개수와 위치만으로 드리프트는 잡힌다.
            foreach (StoryLine line in node.Lines)
            {
                lines.Add(
                    $"line\t{node.Title}\t{StablePath.ToStable(line.FilePath, root)}\t{line.Line}\t" +
                    $"{line.Depth}\t{(line.IsOption ? "opt" : "-")}\t" +
                    $"{(string.IsNullOrEmpty(line.Speaker) ? "-" : line.Speaker)}\t" +
                    $"{line.CommandsSincePreviousLine.Count}\t{line.Hashtags.Count}");
            }

            // 블록은 서로 중첩되므로 트리를 훑어 안쪽 것까지 낸다.
            foreach (StoryBlock block in Flatten(node.Body))
            {
                lines.Add(
                    $"block\t{node.Title}\t{StablePath.ToStable(block.FilePath, root)}\t" +
                    $"{block.StartLine}\t{block.EndLine}\t{block.Kind}\t" +
                    $"{block.Depth}\t{block.Branches.Count}");
            }
        }

        foreach (VnDiagnostic diagnostic in report.Diagnostics)
        {
            lines.Add(
                $"diag\t{diagnostic.Code}\t{diagnostic.Severity}\t" +
                $"{StablePath.ToStable(diagnostic.FilePath, root)}\t{diagnostic.Line}\t{diagnostic.Column}");
        }

        return lines;
    }

    // 바깥 블록 다음에 그 안의 블록이 오도록, 원본 순서대로 훑는다.
    private static IEnumerable<StoryBlock> Flatten(IReadOnlyList<StoryElement> elements)
    {
        foreach (StoryElement element in elements)
        {
            if (element is not StoryBlockElement blockElement)
            {
                continue;
            }

            yield return blockElement.Block;

            foreach (StoryBranch branch in blockElement.Block.Branches)
            {
                foreach (StoryBlock nested in Flatten(branch.Children))
                {
                    yield return nested;
                }
            }
        }
    }
}
