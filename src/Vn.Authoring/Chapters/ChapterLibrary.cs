using Vn.Authoring.Definition;

namespace Vn.Authoring.Chapters;

/// <param name="ChapterId">파일 이름에서 온다 — `chapters/{ChapterId}.xlsx` (§3.1).</param>
/// <param name="Model">읽기에 성공했을 때의 모델. 파일을 열지 못했으면 null이다.</param>
/// <param name="OpenFailure">파일 자체를 열지 못한 사유. 데이터 오류가 아니라 접근 실패다.</param>
public sealed record ChapterEntry(
    string ChapterId,
    string Path,
    ChapterGraphModel? Model,
    string? OpenFailure)
{
    public bool IsReadable => Model is not null;

    public bool HasErrors => Model?.HasErrors ?? true;
}

/// <summary>
/// 프로젝트의 `chapters/` 폴더를 훑어 챕터 워크북을 읽는다.
///
/// <b>읽기만 한다.</b> 폴더를 만들지도, 파일을 쓰지도 않는다 — 이 레이어에서 엑셀은 언제나
/// 사람이 소유하는 원본이고 툴은 소비자다(G-2).
///
/// 파일 하나를 못 열어도 나머지는 읽는다. 워크북 하나가 엑셀에서 열려 잠겨 있다고 해서
/// 챕터 목록 전체가 사라지면, 기획자는 무엇이 문제인지 볼 방법이 없다.
/// </summary>
public static class ChapterLibrary
{
    public const string FolderName = "chapters";

    /// <summary>프로젝트 매니페스트 경로에서 `chapters/` 폴더 경로를 얻는다.</summary>
    public static string? FolderFor(string? projectManifestPath)
    {
        if (string.IsNullOrWhiteSpace(projectManifestPath))
        {
            return null;
        }

        string? root = Path.GetDirectoryName(Path.GetFullPath(projectManifestPath));
        return root is null ? null : Path.Combine(root, FolderName);
    }

    /// <summary>폴더가 없으면 빈 목록이다. 폴더를 만들지 않는다.</summary>
    public static IReadOnlyList<ChapterEntry> Load(string? folder, GameDefinition? definition = null)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return Array.Empty<ChapterEntry>();
        }

        var entries = new List<ChapterEntry>();

        foreach (string path in Directory.EnumerateFiles(folder, "*.xlsx")
                     .Where(candidate => !Path.GetFileName(candidate).StartsWith("~$", StringComparison.Ordinal))
                     .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(Read(path, definition));
        }

        return entries;
    }

    /// <summary>
    /// 워크북 하나를 읽는다. 엑셀이 저장 중이면 잠깐 잠기므로 몇 번 다시 시도한다 —
    /// 한 번 실패했다고 "읽을 수 없는 파일"로 단정하면 저장 직후마다 화면이 비는 것처럼 보인다.
    /// </summary>
    public static ChapterEntry Read(string path, GameDefinition? definition = null, int attempts = 3)
    {
        string chapterId = Path.GetFileNameWithoutExtension(path);
        string? failure = null;

        for (int attempt = 0; attempt < Math.Max(1, attempts); attempt++)
        {
            try
            {
                return new ChapterEntry(chapterId, path, ChapterWorkbookReader.Read(path, definition), null);
            }
            catch (XlsxReadException exception)
            {
                failure = exception.Message;
            }
            catch (IOException exception)
            {
                failure = $"파일이 사용 중입니다: {exception.Message}";
            }

            if (attempt + 1 < attempts)
            {
                Thread.Sleep(120);
            }
        }

        return new ChapterEntry(chapterId, path, null, failure);
    }
}
