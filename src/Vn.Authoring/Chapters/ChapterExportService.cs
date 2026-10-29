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

    /// <summary>모델이 그대로면 다시 증명하지 않는다 — 챕터별 (지문, 결과) 한 벌.</summary>
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
    /// 챕터 하나의 검증 결과 — <b>모델이 그대로면 지난 결과를 그대로 준다</b> (2026-08-18).
    ///
    /// 검증은 순수 함수다: 챕터 모델과 판(프로젝트)만 읽고 상태공간을 훑는다(챕터 하나에
    /// 수십~수백 ms). 화면은 갱신할 때마다 이것을 처음부터 다시 돌리고 있었다. 판을 한 번
    /// 다시 그리는 값이 6ms인데 그 옆에서 400ms를 태우고 있던 셈이다.
    ///
    /// ⚠ <b>지문은 검증이 실제로 읽는 것 — 모델 — 에서 짓는다</b> (2026-10-29 ·
    /// `docs/plans/excel-boundary.md` C3). 그 전에는 챕터·대본 워크북의 SHA-256이었다 — 원본이
    /// 프로젝트가 된 뒤에는 산출물의 해시라서, 되돌리기·조건 변경·출력 실패에 지문이 안 움직여
    /// 옛 판정을 되쓸 수 있었다.
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

        string fingerprint = Fingerprint(entry.Model!, BoardNames(project, entry.ChapterId));

        if (_cache.TryGetValue(entry.ChapterId, out var cached) &&
            string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return cached.Result;
        }

        ChapterValidationResult result = ChapterValidator.Validate(entry.Model!, project);
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
    /// 검증이 읽는 것 전부의 지문 — 챕터 모델의 내용과 판의 상태. 하나라도 다르면 다시 증명한다.
    ///
    /// 모델의 값 객체는 전부 레코드라 <c>ToString</c>이 내용 그대로다(진단까지). 파일을 읽지
    /// 않으므로 디스크가 바뀌어도(출력·잠김·손상) 지문은 그대로이고, 모델이 바뀌면(편집·되돌리기·
    /// 가져오기) 반드시 움직인다 — 캐시가 <b>검증과 같은 것</b>을 보는 것이 규칙이다.
    /// </summary>
    private static string Fingerprint(ChapterGraphModel chapter, IReadOnlyList<string>? boardNames)
    {
        var builder = new StringBuilder();

        builder.Append("판|").Append(boardNames is null ? "없음" : string.Join(",", boardNames)).Append('\n');
        builder.Append("챕터|").Append(chapter.ChapterId).Append('\n');

        void Append(string label, IEnumerable<object> items)
        {
            builder.Append(label).Append('|');

            foreach (object item in items)
            {
                builder.Append(item).Append('\u001f');
            }

            builder.Append('\n');
        }

        Append("에피소드", chapter.Episodes);
        Append("간선", chapter.Edges);
        Append("조건", chapter.Conditions);
        Append("스탯", chapter.Stats);
        Append("픽스처", chapter.Fixtures);
        Append("선택지", chapter.ChoiceOptions);
        Append("진단", chapter.Diagnostics);

        return builder.ToString();
    }

    /// <summary>
    /// 지문에 실을 <b>판의 상태</b> — 그 챕터 판의 대사노드(이름·표식·줄이 있는지). 판이 없으면
    /// null이고, 그것도 상태의 일부다(안 연 챕터 ↔ 연 챕터가 지문으로 갈린다).
    ///
    /// 그 챕터의 판만 본다 — 대사 노드를 찾는 규칙(<see cref="ChapterBoard"/>)이 2026-10-29부터
    /// 그 판만 보므로, 다른 판이 서는 것은 이 챕터의 판정을 바꾸지 않는다. 줄이 있는지까지 싣는
    /// 이유: 빈 노드가 오류라 줄이 생기고 없어지는 것도 판정을 바꾼다.
    /// </summary>
    private static IReadOnlyList<string>? BoardNames(StoryProject? project, string chapterId)
    {
        if (project is null || EpisodeNaming.BoardOf(project, chapterId) is not { } board)
        {
            return null;
        }

        return board.Nodes
            .OfType<DialogueNode>()
            .Select(node =>
                $"{node.Name}{node.MarkedEpisodeId}" +
                (project.FindScript(node.ScriptId)?.ActiveLines.Any() == true ? "1" : "0"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }
}
