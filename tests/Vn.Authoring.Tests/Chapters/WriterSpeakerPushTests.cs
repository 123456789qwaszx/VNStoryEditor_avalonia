using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 작가 화자 → 챕터 `화자` 시트 (2026-08-22 소유자: "모든 챕터가 공유하도록 하는 게
/// 맞아 … 대신에 그게 엑셀 챕터에 반영이 안 되고 있는데 자동으로 반영되도록").
///
/// 작가 화자는 프로젝트에 사는 판 전체의 것이라 어느 챕터의 설정노드에서도 보인다.
/// 그런데 대사 워크북의 화자 드롭다운은 <b>챕터 시트</b>에서 오므로, 시트에 적히지
/// 않으면 화면과 엑셀이 다른 목록을 들게 된다.
///
/// 여기서 지키는 것 셋: <b>모든 챕터에 적는다</b> · <b>이미 있으면 파일에 손대지 않는다</b>
/// (판정은 넘겨받은 모델로 — 자판마다 엑셀을 두드리지 않는다) · <b>더하기만 한다</b>
/// (작가가 지운 이름을 기획자의 시트에서 지우지 않는다).
/// </summary>
public sealed class WriterSpeakerPushTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-writer-speaker-push", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ChapterEntry Chapter(string chapterId)
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, chapterId);
        return Read(chapterId);
    }

    private ChapterEntry Read(string chapterId)
    {
        string path = Path.Combine(_directory, chapterId + ".xlsx");
        return new ChapterEntry(chapterId, path, ChapterWorkbookReader.Read(path), null);
    }

    private static string[] SpeakerNames(ChapterEntry entry) =>
        entry.Model!.Speakers.Select(speaker => speaker.Name).ToArray();

    [Fact]
    public void 작가_화자가_모든_챕터의_화자_시트에_적힌다()
    {
        ChapterEntry first = Chapter("ch01");
        ChapterEntry second = Chapter("ch02");

        WriterSpeakerPush.Result push = WriterSpeakerPush.ToChapters(
            [first, second], ["라루", "윌로"]);

        Assert.Equal(4, push.Written); // 챕터 둘 × 이름 둘
        Assert.Empty(push.Blocked);

        Assert.Equal(["라루", "윌로"], SpeakerNames(Read("ch01")));
        Assert.Equal(["라루", "윌로"], SpeakerNames(Read("ch02")));
    }

    [Fact]
    public void 이미_있는_이름은_파일에_손대지_않는다()
    {
        ChapterEntry chapter = Chapter("ch01");
        WriterSpeakerPush.ToChapters([chapter], ["라루"]);

        ChapterEntry after = Read("ch01");
        DateTime stamp = File.GetLastWriteTimeUtc(after.Path);

        // ⚠ 판정은 넘겨받은 모델로 한다 — 바뀐 것이 없으면 워크북을 열지도 않는다.
        WriterSpeakerPush.Result again = WriterSpeakerPush.ToChapters([after], ["라루"]);

        Assert.Equal(0, again.Written);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(after.Path));
        Assert.Equal(["라루"], SpeakerNames(Read("ch01")));
    }

    [Fact]
    public void 빈_이름은_거르고_지운_이름은_시트에서_안_지운다()
    {
        ChapterEntry chapter = Chapter("ch01");
        WriterSpeakerPush.ToChapters([chapter], ["라루", "  ", "윌로"]);

        ChapterEntry after = Read("ch01");
        Assert.Equal(["라루", "윌로"], SpeakerNames(after));

        // 작가가 '윌로'를 지웠다 — 시트는 그대로다(그 시트의 주인은 기획자다).
        WriterSpeakerPush.Result push = WriterSpeakerPush.ToChapters([after], ["라루"]);

        Assert.Equal(0, push.Written);
        Assert.Equal(["라루", "윌로"], SpeakerNames(Read("ch01")));
    }

    [Fact]
    public void 못_읽는_챕터에는_쓰지_않는다()
    {
        var broken = new ChapterEntry("ch_broken", Path.Combine(_directory, "ch_broken.xlsx"), null, "열지 못함");

        WriterSpeakerPush.Result push = WriterSpeakerPush.ToChapters([broken], ["라루"]);

        Assert.Equal(0, push.Written);
        Assert.Empty(push.Blocked);
        Assert.False(File.Exists(broken.Path));
    }
}
