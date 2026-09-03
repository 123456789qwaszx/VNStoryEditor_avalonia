using System.Text.Json;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 마지막으로 출시했다고 사람이 확정한 진행 JSON과 현재 산출물의 선택지 배열을 비교한다.
/// 자동 산출물과 분리해야 다시 읽기가 과거 기준을 덮지 않는다.
/// </summary>
public static class ChapterReleaseBaseline
{
    public const string FolderName = "release-baselines";

    public static string PathFor(string projectPath, string chapterId) => Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(projectPath))!,
        FolderName,
        chapterId + ".progression.json");

    /// <summary>현재 export를 출시 기준선으로 승격한다. 서버 수입을 확인한 뒤 명시적으로 부른다.</summary>
    public static bool Capture(string projectPath, string chapterId)
    {
        string source = ChapterExportService.ExportPathFor(projectPath, chapterId);
        string target = PathFor(projectPath, chapterId);

        try
        {
            byte[] bytes = File.ReadAllBytes(source);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 기존 항목의 앞 삽입·삭제·교환만 찾는다. 같은 자리의 문구·조건·효과 수정은 새 버전의
    /// 정상 콘텐츠 변경이므로 순서 오류로 만들지 않는다.
    /// </summary>
    public static IReadOnlyList<string> FindOrderChanges(string baselineJson, string currentJson)
    {
        Dictionary<string, string[]> before = OptionsByEpisode(baselineJson);
        Dictionary<string, string[]> after = OptionsByEpisode(currentJson);
        var changed = new List<string>();

        foreach ((string episodeId, string[] oldOptions) in before.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!after.TryGetValue(episodeId, out string[]? newOptions) || oldOptions.Length == 0)
            {
                continue;
            }

            if (IsIndexMovingChange(oldOptions, newOptions))
            {
                changed.Add(episodeId);
            }
        }

        return changed;
    }

    private static bool IsIndexMovingChange(string[] before, string[] after)
    {
        if (before.SequenceEqual(after, StringComparer.Ordinal))
        {
            return false;
        }

        // 끝에 더하는 것은 기존 index를 보존한다.
        if (after.Length >= before.Length &&
            before.SequenceEqual(after.Take(before.Length), StringComparer.Ordinal))
        {
            return false;
        }

        bool sameItems = before.Length == after.Length &&
            before.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(after.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);
        bool insertedBeforeEnd = after.Length > before.Length && IsSubsequence(before, after);
        bool removedBeforeEnd = after.Length < before.Length && IsSubsequence(after, before);

        return sameItems || insertedBeforeEnd || removedBeforeEnd;
    }

    private static bool IsSubsequence(IReadOnlyList<string> smaller, IReadOnlyList<string> larger)
    {
        int at = 0;
        foreach (string item in larger)
        {
            if (at < smaller.Count && string.Equals(smaller[at], item, StringComparison.Ordinal))
            {
                at++;
            }
        }
        return at == smaller.Count;
    }

    private static Dictionary<string, string[]> OptionsByEpisode(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (JsonElement node in document.RootElement.GetProperty("Nodes").EnumerateArray())
        {
            string id = node.GetProperty("EpisodeId").GetString() ?? string.Empty;
            result[id] = node.GetProperty("NextOptions").EnumerateArray()
                .Select(option => Canonical(option))
                .ToArray();
        }

        return result;
    }

    private static string Canonical(JsonElement option) => JsonSerializer.Serialize(option);
}
