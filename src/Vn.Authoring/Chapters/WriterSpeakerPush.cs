namespace Vn.Authoring.Chapters;

/// <summary>
/// 작가가 더한 화자를 <b>모든 챕터</b>의 `화자` 시트에 옮겨 적는다
/// (2026-08-22 소유자: "모든 챕터가 공유하도록 하는 게 맞아 … 자동으로 반영되도록").
///
/// <b>왜 옮겨 적는가</b> — 작가 화자는 프로젝트에 사는 판 전체의 것이라 어느 챕터에서
/// 더해도 모든 설정노드에 보인다(소유자 확인으로 그 공유가 규격이 됐다). 그런데 대사
/// 워크북의 화자 드롭다운은 <b>챕터 `화자` 시트</b>에서 온다 — 시트에 없으면 엑셀에서는
/// 그 이름을 고를 수 없었다. 화면과 엑셀이 다른 목록을 들고 있던 셈이다.
///
/// ⚠ <b>더하기만 한다.</b> 작가가 지운 이름을 시트에서 지우지 않는다 — 그 시트의 주인은
/// 기획자이고, 같은 이름을 기획자가 따로 등록해 두었을 수 있다. 지우는 것은 사람의 일이다.
/// </summary>
public static class WriterSpeakerPush
{
    /// <param name="Written">실제로 적은 줄 수. 0이면 파일에 아예 손대지 않았다.</param>
    /// <param name="Blocked">못 적은 챕터의 사유 — 대개 엑셀이 그 파일을 잡고 있다.</param>
    public sealed record Result(int Written, IReadOnlyList<string> Blocked);

    /// <summary>
    /// ⚠ 있는지 없는지는 <b>넘겨받은 모델</b>로 판정한다 — 흔한 경우(바뀐 것 없음)에는
    /// 워크북을 한 번도 열지 않는다. 매번 파일을 읽어 확인하면 자판마다 챕터 수만큼
    /// 엑셀을 두드리게 되고, 그것이 §성능 규칙이 경계하는 바로 그 모양이다.
    /// </summary>
    public static Result ToChapters(
        IEnumerable<ChapterEntry> chapters,
        IEnumerable<string> writerSpeakerNames)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(writerSpeakerNames);

        string[] names = writerSpeakerNames
            .Select(name => name?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var blocked = new List<string>();
        int written = 0;

        if (names.Length == 0)
        {
            return new Result(0, blocked);
        }

        foreach (ChapterEntry entry in chapters)
        {
            if (entry.Model is null)
            {
                continue; // 못 읽는 챕터에는 쓰지 않는다 — 검증 보고가 먼저 할 말이 있다
            }

            var have = new HashSet<string>(
                entry.Model.Speakers.Select(speaker => speaker.Name.Trim()),
                StringComparer.Ordinal);

            foreach (string name in names.Where(name => !have.Contains(name)))
            {
                ChapterWriteResult result = ChapterWorkbookWriter.AddSpeaker(entry.Path, name);

                if (result.Written)
                {
                    written++;
                    have.Add(name);
                }
                else if (result.Failure is { } failure)
                {
                    blocked.Add($"{entry.ChapterId} — {failure}");
                }
            }
        }

        return new Result(written, blocked);
    }
}
