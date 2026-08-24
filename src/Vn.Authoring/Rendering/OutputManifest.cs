using System.Text.Json.Nodes;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Rendering;

/// <summary>고아 출력 하나를 왜 고아로 봤는지.</summary>
public enum OrphanOutputSource
{
    /// <summary>직전 산출 기록에 있던 파일 — VnTool이 쓴 것이 확실하다.</summary>
    Recorded,

    /// <summary>기록에는 없지만 산출 이름 형식이 같은 파일 — 기록 이전에 쓴 것일 수 있다.</summary>
    NameShape
}

/// <summary>출력 폴더에 남은 낡은 산출 파일 하나. 폴더 안 이름만 갖는다.</summary>
public sealed record OrphanOutput(string FileName, OrphanOutputSource Source);

/// <summary>
/// 고아 판정 한 번의 결과. <paramref name="Note"/>는 판정을 온전히 하지 못한 사유이며,
/// 있으면 숨기지 않고 사람에게 보인다(규칙 14).
/// </summary>
public sealed record OrphanOutputScan(IReadOnlyList<OrphanOutput> Orphans, string? Note)
{
    public static readonly OrphanOutputScan Empty = new([], null);
}

/// <summary>
/// 라이브 출력 폴더에 VnTool이 무엇을 썼는지 남기는 기록과, 그 기록으로 낡은 파일을
/// 찾아내는 판정(K1 ②안).
///
/// <b>이 클래스는 파일을 지우지 않는다.</b> 출력 폴더는 사용자의 폴더이지 VnTool의
/// 소유물이 아니므로, 낡아 보이는 파일을 임의로 회수하면 사용자 파일을 잃을 수 있다.
/// 대신 "노드를 지웠는데 그 노드의 .yarn이 아직 폴더에 있다"는 사실을 <b>보이게</b> 만든다 —
/// 유니티가 폴더를 통째로 읽으면 없어진 노드의 옛 대사를 그대로 재생하기 때문이다.
/// </summary>
public static class OutputManifest
{
    /// <summary>기록 파일 이름. 점으로 시작해 Yarn 컴파일 대상과 섞이지 않는다.</summary>
    public const string FileName = ".vntool-output.json";

    private const int CurrentVersion = 1;

    /// <summary>
    /// 지금 프로젝트가 이 폴더에 만들어 낼 수 있는 파일 이름 전부.
    /// 막힌 노드도 포함한다 — 이번에 못 썼을 뿐 그 노드의 파일은 고아가 아니다.
    /// </summary>
    public static IReadOnlyList<string> ExpectedFileNames(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var names = new List<string> { YarnBundleEmitter.DeclarationsFileName };

        // ⚠ <b>판을 돌면서</b> 센다 (2026-08-25) — 파일 이름이 챕터를 앞에 달게 되면서
        //    (챕터=판 1:1) 그 노드가 어느 판에 사는지를 알아야 이름을 맞출 수 있다.
        //    프로젝트 전체를 평평하게 돌면 챕터를 잃어버리고, 그러면 방금 나간 파일이
        //    전부 고아로 잡힌다.
        foreach (StoryFile file in project.Files)
        {
            foreach (DialogueNode node in file.Nodes.OfType<DialogueNode>())
            {
                names.AddRange(YarnBundleEmitter.FileNamesOf(
                    YarnBundleEmitter.BundleNameOf(node.Name, node.Id), file.Name));
            }
        }

        return names;
    }

    /// <summary>
    /// 폴더를 훑어 고아를 찾는다. <paramref name="expectedFileNames"/>에 없는 파일 중
    /// 기록에 있거나 산출 이름 형식과 같은 것만 고아로 본다 — 사용자가 넣어 둔 다른 파일은
    /// 목록에 넣지 않는다.
    /// </summary>
    public static OrphanOutputScan Scan(string directory, IReadOnlyCollection<string> expectedFileNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(expectedFileNames);

        if (!Directory.Exists(directory))
        {
            return OrphanOutputScan.Empty;
        }

        var expected = new HashSet<string>(expectedFileNames, StringComparer.OrdinalIgnoreCase);
        string? note = null;
        HashSet<string> recorded;

        try
        {
            recorded = new HashSet<string>(Read(directory), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // 기록을 잃어도 판정을 포기하지 않는다. 다만 그 사실을 조용히 삼키지 않는다.
            recorded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            note = $"출력 기록({FileName})을 읽지 못해 이름 형식만으로 판정했습니다: {exception.Message}";
        }

        var orphans = new List<OrphanOutput>();

        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(path);

            if (string.Equals(name, FileName, StringComparison.OrdinalIgnoreCase) ||
                expected.Contains(name))
            {
                continue;
            }

            if (recorded.Contains(name))
            {
                orphans.Add(new OrphanOutput(name, OrphanOutputSource.Recorded));
            }
            else if (LooksLikeOutput(name))
            {
                orphans.Add(new OrphanOutput(name, OrphanOutputSource.NameShape));
            }
        }

        orphans.Sort((left, right) => string.CompareOrdinal(left.FileName, right.FileName));
        return new OrphanOutputScan(orphans, note);
    }

    /// <summary>
    /// 이번에 쓴 파일을 기록한다. 직전 기록 중 아직 폴더에 남아 있는 파일도 함께 남긴다 —
    /// 그래야 "VnTool이 쓴 파일"이라는 사실이 한 번의 재저장으로 잊히지 않는다.
    /// </summary>
    public static void Record(string directory, IEnumerable<string> writtenFileNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(writtenFileNames);

        var names = new SortedSet<string>(
            writtenFileNames.Select(Path.GetFileName).OfType<string>(),
            StringComparer.Ordinal);

        try
        {
            foreach (string previous in Read(directory))
            {
                if (File.Exists(Path.Combine(directory, previous)))
                {
                    names.Add(previous);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // 읽지 못한 기록은 이번 산출 목록으로 다시 시작한다. Scan이 사유를 알린다.
        }

        var files = new JsonArray();

        foreach (string name in names)
        {
            files.Add(JsonValue.Create(name));
        }

        var root = new JsonObject
        {
            ["version"] = CurrentVersion,
            ["files"] = files
        };

        JsonSupport.WriteAtomic(
            Path.Combine(directory, FileName),
            JsonSupport.ToDeterministicText(root));
    }

    /// <summary>기록된 파일 이름. 기록이 없으면 빈 목록이고, 깨져 있으면 던진다.</summary>
    public static IReadOnlyList<string> Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string path = Path.Combine(directory, FileName);

        if (!File.Exists(path))
        {
            return [];
        }

        JsonObject root = JsonSupport.ParseObject(File.ReadAllText(path), "출력 기록");

        if (root["files"] is not JsonArray array)
        {
            throw new InvalidDataException($"{FileName}에 files 배열이 없습니다.");
        }

        return array
            .Select(node => node?.GetValue<string>())
            .OfType<string>()
            .ToArray();
    }

    /// <summary>이 폴더에서 VnTool이 만들었을 법한 이름인가.</summary>
    private static bool LooksLikeOutput(string fileName)
    {
        if (string.Equals(fileName, YarnBundleEmitter.DeclarationsFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!fileName.EndsWith(".yarn", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // ⚠ 여기 접두 셋은 <b>옛 이름</b>이다 (2026-08-24에 `Story_` 접두가 폐지됐다).
        // 그래도 남겨 둔다: 그날 이전에 쓴 폴더에는 `Story_*.yarn`이 그대로 있고, 그것을
        // 고아로 알아보는 유일한 단서가 이 이름이다.
        //
        // ⛔ <b>이제 이 판정은 "우리 것"의 증명이 아니다.</b> 새 파일 이름은 대사엔트리를
        // 따르므로 무엇이든 될 수 있다. 우리 것의 증명은 <b>기록
        // (`.vntool-output.json`)</b>이고, 위 <see cref="Scan"/>이 그것을 먼저 본다.
        // 기록에 없고 이름도 옛 모양이 아니면 <b>모른다고 답한다</b> — 못 지운 고아는
        // 다음 쓰기에서 덮이지만, 남의 파일을 지우면 되돌릴 수 없다.
        return fileName.StartsWith("Story_", StringComparison.Ordinal)
            || fileName.StartsWith(YarnBundleEmitter.SetPrefix, StringComparison.Ordinal)
            || fileName.StartsWith(YarnBundleEmitter.PresPrefix, StringComparison.Ordinal);
    }
}
