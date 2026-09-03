using System.Security.Cryptography;
using Vn.Authoring.Model;
using System.Text;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 한 번의 내보내기 결과 — 못 나간 챕터들. 둘 다 비면 전부 나갔다는 뜻이다.
/// </summary>
/// <param name="Refused">검증 오류로 거부된 챕터. <b>파일은 손대지 않았다.</b></param>
/// <param name="Failed">쓰기 자체가 실패한 챕터(잠김·권한).</param>
public sealed record ChapterExportRun(
    IReadOnlyList<string> Refused,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string>? DeploymentBlocked = null,
    IReadOnlyDictionary<string, string>? Checksums = null)
{
    public static ChapterExportRun Empty { get; } = new([], [], [], new Dictionary<string, string>());

    public IReadOnlyList<string> Blocked => DeploymentBlocked ?? [];

    public bool AllExported => Refused.Count == 0 && Failed.Count == 0 && Blocked.Count == 0;

    /// <summary>
    /// 검증 보고 맨 위에 세울 결론 — <b>못 나갔을 때만 있다.</b> 잘 나간 것은 말하지 않는다.
    /// </summary>
    public string? Notice => AllExported
        ? null
        : string.Join("\n", new[]
        {
            Refused.Count == 0
                ? null
                : $"검증 오류로 진행 JSON이 나가지 않았습니다: {string.Join(", ", Refused)}" +
                  $" — 아래 오류를 고치면 저절로 나갑니다({ChapterExportService.ExportFolderName}/).",
            Failed.Count == 0
                ? null
                : $"진행 JSON을 쓰지 못했습니다: {string.Join(", ", Failed)}" +
                  $" — {ChapterExportService.ExportFolderName}/ 의 파일이 다른 프로그램에 " +
                  "잡혀 있는지 확인하세요.",
            Blocked.Count == 0
                ? null
                : $"출시 기준선 뒤 선택지 순번이 바뀌어 진행 JSON 갱신을 막았습니다: " +
                  $"{string.Join(", ", Blocked)} — 과거 선택 이력의 OptionIndex가 다른 선택을 " +
                  $"가리킬 수 있습니다. {ChapterReleaseBaseline.FolderName}/ 기준선과 순서를 확인하세요."
        }.Where(part => part is not null));
}

/// <summary>
/// 챕터를 <b>증명하고 내보내는</b> 자리. 검증 캐시와 내보내기가 한 객체인 이유는 규칙
/// 하나 때문이다 — <b>같은 증명을 두 번 돌리지 않는다</b>(2026-08-18). 화면은 보고
/// 패널을 세우려고 어차피 한 번 증명하는데, 예전에는 내보내기가 안에서 또 증명해 챕터
/// 하나당 200ms를 두 번 치렀다.
///
/// <b>왜 화면 밖으로 나왔나 (2026-08-23)</b> — 이 정책이 `ChapterGraphView` 3,835줄
/// 안에 살아서 <b>밖에서 보이지 않았다.</b> 실제로 물린 적이 있다: 동기화는 고른 챕터만
/// 도는데 내보내기는 전 챕터를 돈다는 사실이 코드비하인드에 묻혀 있어, 저작 관문을
/// 걸려던 시도가 "안 연 챕터가 전부 거부된다"는 것을 뒤늦게 알았다. 규칙이 안 보이는
/// 자리에 있으면 그 규칙을 지킬 수 없다.
///
/// 화면도 파일 대화상자도 모른다 — 프로젝트 경로 문자열 하나와 챕터 목록만 받는다.
/// </summary>
public sealed class ChapterExportService
{
    /// <summary>진행 JSON이 놓이는 폴더 이름. 프로젝트 파일 옆이다.</summary>
    public const string ExportFolderName = "exported";

    /// <summary>디스크가 그대로면 다시 증명하지 않는다 — 챕터별 (지문, 결과) 한 벌.</summary>
    private readonly Dictionary<string, (string Fingerprint, ChapterValidationResult Result)>
        _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// 실제로 증명을 돌린 횟수. 테스트가 "일을 몇 번 했는가"를 보는 창이다 —
    /// 시간(ms)은 기계마다 다르지만 횟수는 규칙이고, 느려지는 회귀는 언제나 횟수가
    /// 먼저 는다. <b>인스턴스에 갇혀 있어</b> 테스트가 병렬로 돌아도 서로 섞이지 않는다.
    /// </summary>
    public int ValidationComputeCount { get; private set; }

    /// <summary>그 챕터의 진행 JSON이 놓일 자리.</summary>
    public static string ExportPathFor(string projectPath, string chapterId) => Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(projectPath))!,
        ExportFolderName,
        chapterId + ".progression.json");

    /// <summary>
    /// 챕터 하나의 검증 결과 — <b>파일이 그대로면 지난 결과를 그대로 준다</b> (2026-08-18).
    ///
    /// 검증은 순수 함수다: 챕터 워크북과 그 챕터의 대본 워크북들만 읽는다(에피소드마다
    /// 파일을 열고, 평평화하고, 상태공간을 훑는다 — 챕터 하나에 200ms 가까이). 그런데
    /// 화면은 갱신할 때마다 이것을 처음부터 다시 돌리고 있었다. 판을 한 번 다시 그리는
    /// 값이 6ms인데 그 옆에서 400ms를 태우고 있던 셈이다.
    /// </summary>
    /// <param name="project">
    /// 있으면 <b>`대사엔트리`가 실재하는 대사노드를 가리키는지</b>까지 검증한다
    /// (<see cref="ChapterValidator"/>). 판의 노드 이름들이 <b>지문에 함께 들어가므로</b>,
    /// 동기화가 노드를 만든 순간 캐시가 깨지고 다시 증명한다 — 그러지 않으면 방금 만든
    /// 노드를 모르는 옛 결론이 남아 "고쳤는데 계속 거부한다"가 된다.
    /// </param>
    public ChapterValidationResult ValidationFor(
        ChapterEntry entry,
        string? projectPath,
        StoryProject? project = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string? episodesFolder = EpisodeLibrary.FolderFor(projectPath, entry.ChapterId);
        string fingerprint = Fingerprint(entry.Path, episodesFolder, BoardNames(project, entry.ChapterId));

        if (_cache.TryGetValue(entry.ChapterId, out var cached) &&
            string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return cached.Result;
        }

        ChapterValidationResult result =
            ChapterValidator.Validate(entry.Model!, episodesFolder, project);
        ValidationComputeCount++;
        _cache[entry.ChapterId] = (fingerprint, result);

        return result;
    }

    /// <summary>
    /// <b>모든</b> 챕터의 진행 JSON을 낸다 (2026-08-17 소유자: [내보내기] 단추가 "필요
    /// 없어 보이는데"). 작가의 Yarn은 이미 라이브 출력이 저절로 쓰는데 챕터만 사람 손을
    /// 기다렸고, 그래서 누른 순간의 낡은 파일이 남았다 — <b>고른 챕터만</b> 나가던 것도
    /// 같은 병이라 여기서는 전부 낸다.
    ///
    /// G8의 규칙은 그대로다: <b>검증을 통과해야만 나간다.</b> 거부되면 그 챕터의 파일은
    /// 손대지 않는다 — 낡은 파일이 남더라도 쓰레기로 덮는 것보다 낫다(런타임이 읽는 건
    /// 언제나 "한 번은 옳았던" 판이다).
    ///
    /// ⚠ <b>이 함수는 전 챕터를 돈다.</b> 에피소드 동기화는 고른 챕터 하나만 돈다 —
    /// 그 비대칭이 저작 관문(`대사엔트리`가 대사노드와 맞는가)을 여기 걸지 못하는 이유다.
    /// 한 번도 안 연 챕터는 판에 노드가 없어서 전부 거부되기 때문이다.
    /// </summary>
    public ChapterExportRun ExportAll(
        IReadOnlyList<ChapterEntry> entries,
        string? projectPath,
        StoryProject? project = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (projectPath is null)
        {
            return ChapterExportRun.Empty;
        }

        var refused = new List<string>();
        var failed = new List<string>();
        var blocked = new List<string>();
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (ChapterEntry entry in entries)
        {
            if (entry.Model is null)
            {
                continue;
            }

            // 검증은 챕터별로 한 벌만 계산한다 (2026-08-18) — 보고 패널이 쓰는 것과
            // 같은 결과다. 예전에는 내보내기가 안에서 또 증명해 같은 값을 두 번 치렀다.
            // 프로젝트를 함께 넘긴다 (2026-08-24) — 간선에 매달린 자유 씬(`ViaNodeId`)의
            // 원본이 워크북이 아니라 연출 그래프의 배선이라서, 챕터 모델만으로는 못 찾는다.
            ChapterExportResult result = ChapterProgressionExporter.ExportValidated(
                entry.Model,
                ValidationFor(entry, projectPath, project),
                project);

            if (result.Refused)
            {
                refused.Add(entry.ChapterId);
                continue;
            }

            string baselinePath = ChapterReleaseBaseline.PathFor(projectPath, entry.ChapterId);
            if (File.Exists(baselinePath))
            {
                try
                {
                    IReadOnlyList<string> moved = ChapterReleaseBaseline.FindOrderChanges(
                        File.ReadAllText(baselinePath), result.Json!);
                    if (moved.Count > 0)
                    {
                        blocked.Add($"{entry.ChapterId}({string.Join(", ", moved)})");
                        continue;
                    }
                }
                catch (IOException)
                {
                    failed.Add(entry.ChapterId);
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    failed.Add(entry.ChapterId);
                    continue;
                }
                catch (System.Text.Json.JsonException)
                {
                    failed.Add(entry.ChapterId);
                    continue;
                }
            }

            if (!TryWrite(ExportPathFor(projectPath, entry.ChapterId), result.Json!))
            {
                failed.Add(entry.ChapterId);
            }
            else
            {
                checksums[entry.ChapterId] = result.Checksum!;
            }
        }

        return new ChapterExportRun(refused, failed, blocked, checksums);
    }

    /// <summary>
    /// 같은 글이면 안 쓴다 — 다시 읽을 때마다 파일을 두드리면 클라우드 동기화가 계속
    /// 깨어나고, 바뀐 것이 없는데 시각만 새로 찍힌다.
    /// </summary>
    private static bool TryWrite(string path, string json)
    {
        try
        {
            byte[] bytes = ChapterExportBytes.Encode(json);
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
            {
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);

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
    /// 검증이 읽는 파일 전부의 (이름·내용 해시). 하나라도 다르면 다시 증명한다.
    ///
    /// 지문은 <b>파일 내용의 해시</b>다. 수정 시각·크기가 아니다: 화면은 자기가 워크북을
    /// 쓰고 <b>그 자리에서 곧바로</b> 다시 읽는다(단추 하나가 쓰기와 재읽기를 잇따라 낸다).
    /// 두 사건이 같은 시각 눈금에 들어가고 길이까지 같으면 시각·크기 지문은 "안 바뀌었다"고
    /// 답하고, 화면은 방금 적은 값을 모르는 옛 결과를 보여 준다 — <b>캐시가 만드는 가장
    /// 나쁜 거짓말</b>이다. 그 위험을 아예 없애려고 내용을 본다.
    ///
    /// 바이트를 읽는 값이 아깝지 않다: 검증은 그 파일들을 엑셀로 파싱하고 평평화한 뒤
    /// 상태공간까지 훑는다(200ms 가까이). 여기서 재는 것은 그 앞의 몇 ms다.
    ///
    /// 읽지 못하는 파일은 이름만 남긴다 — 잠겨 있다가 풀리는 순간을 놓치지 않으려면
    /// 그 상태도 지문의 일부여야 한다.
    /// </summary>
    /// <summary>
    /// 그 챕터 판의 대사노드 이름들 — 정렬해서 지문에 넣는다. 판이 없으면 null이고,
    /// 그것도 상태의 일부다(안 연 챕터 ↔ 연 챕터가 지문으로 갈린다).
    /// </summary>
    /// <summary>
    /// 지문에 실을 <b>판의 상태</b>.
    ///
    /// ⚠ <b>그 챕터의 판만 보면 안 된다</b> (2026-08-25). 대사 노드를 찾는 규칙이
    /// <c>ExcelEpisodeId</c>로 <b>프로젝트 전체</b>를 훑으므로(<see cref="ChapterBoard"/>),
    /// 다른 판이 서는 것만으로도 이 챕터의 판정이 바뀐다. 자기 판만 재면 그 변화를 놓쳐
    /// <b>"고쳤는데 계속 거부한다"</b>가 된다 — 실제로 한 번 그랬다: 처음 그리기에서
    /// 노드가 아직 없어 거부된 챕터가, 동기화가 노드를 세운 뒤에도 옛 결론을 들고 있었다.
    ///
    /// 대사 노드는 이름과 <b>줄이 있는지</b>까지 싣는다 — 빈 노드가 오류이므로 줄이
    /// 생기고 없어지는 것도 판정을 바꾼다.
    /// </summary>
    private static IReadOnlyList<string>? BoardNames(StoryProject? project, string chapterId)
    {
        if (project is null)
        {
            return null;
        }

        return project.EnumerateNodes()
            .OfType<DialogueNode>()
            .Select(node =>
                $"{node.Name}{node.ExcelEpisodeId}" +
                (project.FindScript(node.ScriptId)?.ActiveLines.Any() == true ? "1" : "0"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static string Fingerprint(
        string chapterWorkbook,
        string? episodesFolder,
        IReadOnlyList<string>? boardNames)
    {
        var builder = new StringBuilder();

        // 판의 노드 이름이 지문의 일부다 (2026-08-23) — `대사엔트리` 검사가 이것을 보므로,
        // 동기화가 노드를 만들어도 캐시가 안 깨지면 "고쳤는데 계속 거부한다"가 된다.
        builder.Append("판|").Append(boardNames is null ? "없음" : string.Join(",", boardNames))
               .Append('\n');

        void Append(string path)
        {
            try
            {
                byte[] hash = SHA256.HashData(File.ReadAllBytes(path));

                builder.Append(Path.GetFileName(path)).Append('|')
                       .Append(Convert.ToHexString(hash)).Append('\n');
            }
            catch (IOException)
            {
                builder.Append(path).Append("|?\n");
            }
            catch (UnauthorizedAccessException)
            {
                builder.Append(path).Append("|?\n");
            }
        }

        Append(chapterWorkbook);

        if (episodesFolder is not null && Directory.Exists(episodesFolder))
        {
            foreach (string path in Directory.EnumerateFiles(episodesFolder, "*.xlsx")
                         .Where(file => !Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal))
                         .OrderBy(file => file, StringComparer.Ordinal))
            {
                Append(path);
            }
        }

        return builder.ToString();
    }
}
