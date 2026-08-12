using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// Gate A 3번 — "엑셀 저장 → 뷰 즉시 갱신"을 화면 없이 닫는다.
///
/// 확인하는 것은 셋이다: 저장이 알림으로 이어지는가, 한 번의 저장이 한 번의 읽기가 되는가
/// (엑셀은 저장마다 이벤트를 여러 개 낸다), 그리고 저장 중 잠긴 파일을 어떻게 넘기는가.
/// </summary>
public sealed class ChapterFolderWatcherTests : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "vn-chapter-watch", Guid.NewGuid().ToString("N"), "chapters");

    public ChapterFolderWatcherTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        string root = Path.GetDirectoryName(_folder)!;

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 워크북을_저장하면_알림이_오고_바뀐_값이_읽힌다()
    {
        string path = WriteChapter("ch05.xlsx", episodeTitle: "닫힌 문 앞에서");

        using var signal = new CountdownEvent(1);
        using var watcher = new ChapterFolderWatcher(_folder, () => Signal(signal), Debounce);

        WriteChapter("ch05.xlsx", episodeTitle: "다시 쓴 제목");

        Assert.True(signal.Wait(Patience), "저장했는데 알림이 오지 않았다");

        ChapterEntry entry = ChapterLibrary.Read(path);
        Assert.True(entry.IsReadable);
        Assert.Equal("다시 쓴 제목", entry.Model!.Episodes[0].Title);
    }

    [Fact]
    public void 한_번의_저장이_한_번의_알림이_된다()
    {
        WriteChapter("ch05.xlsx", episodeTitle: "처음");

        int notifications = 0;
        using var settled = new ManualResetEventSlim(false);

        using var watcher = new ChapterFolderWatcher(
            _folder,
            () =>
            {
                Interlocked.Increment(ref notifications);
                settled.Set();
            },
            Debounce);

        // 엑셀 한 번의 저장이 내는 이벤트 무리를 흉내낸다 — 잇달아 여러 번 건드린다.
        for (int touch = 0; touch < 5; touch++)
        {
            WriteChapter("ch05.xlsx", episodeTitle: $"저장 {touch}");
        }

        Assert.True(settled.Wait(Patience), "알림이 오지 않았다");

        // 디바운스 창이 닫힌 뒤에도 추가 알림이 쏟아지지 않아야 한다.
        Thread.Sleep(Debounce + Debounce);
        Assert.True(
            Volatile.Read(ref notifications) <= 2,
            $"저장 한 묶음에 알림이 {Volatile.Read(ref notifications)}번 왔다 — 디바운스가 듣지 않는다");
    }

    [Fact]
    public void 엑셀_잠금_파일은_저장_사건으로_세지_않는다()
    {
        WriteChapter("ch05.xlsx", episodeTitle: "처음");

        int notifications = 0;
        using var watcher = new ChapterFolderWatcher(
            _folder, () => Interlocked.Increment(ref notifications), Debounce);

        // 엑셀이 파일을 열면 ~$ 잠금 파일이 생긴다. 그건 저장이 아니다.
        File.WriteAllText(Path.Combine(_folder, "~$ch05.xlsx"), "lock");

        Thread.Sleep(Debounce + Debounce + Debounce);
        Assert.Equal(0, Volatile.Read(ref notifications));
    }

    [Fact]
    public void 엑셀이_열어_둔_워크북도_읽힌다()
    {
        string path = WriteChapter("ch05.xlsx", episodeTitle: "열린 채로");

        // 엑셀이 파일을 열어 두는 방식(공유 읽기·쓰기)에서도 읽혀야 한다.
        // 기획자가 엑셀을 켜 둔 채 그래프를 보는 것이 이 레이어의 일상이다.
        using var open = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        ChapterEntry entry = ChapterLibrary.Read(path);

        Assert.True(entry.IsReadable);
        Assert.Equal("열린 채로", entry.Model!.Episodes[0].Title);
    }

    [Fact]
    public void 배타적으로_잠긴_워크북은_재시도_후_사유를_남긴다()
    {
        string path = WriteChapter("ch05.xlsx", episodeTitle: "잠김");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ChapterEntry locked = ChapterLibrary.Read(path);

            // 조용히 빈 챕터가 되지 않는다 — 왜 못 읽었는지가 남는다(규칙 14).
            Assert.False(locked.IsReadable);
            Assert.NotNull(locked.OpenFailure);
            Assert.Equal("ch05", locked.ChapterId);
        }

        // 잠금이 풀리면 다음 읽기는 성공한다. 한 번 실패가 영구 실패가 아니다.
        Assert.True(ChapterLibrary.Read(path).IsReadable);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static void Signal(CountdownEvent signal)
    {
        if (!signal.IsSet)
        {
            signal.Signal();
        }
    }

    /// <summary>오류 없이 읽히는 최소 챕터 워크북. 제목만 바꿔 가며 저장을 흉내낸다.</summary>
    private string WriteChapter(string fileName, string episodeTitle)
    {
        string path = Path.Combine(_folder, fileName);

        using var workbook = new XLWorkbook();

        Fill(workbook.AddWorksheet("에피소드"),
        [
            ["EpisodeId", "제목", "인덱스", "종류", "대사엔트리", "X", "Y", "표시조건", "해금조건", "엔딩키", "메모"],
            ["ep1", episodeTitle, "01", "Main", "Story_ep1", "0", "0", null, null, null, null],
            ["ep2", "둘째 화", "02", "Main", "Story_ep2", "200", "0", null, null, null, null]
        ]);

        Fill(workbook.AddWorksheet("간선"),
        [
            ["출발", "도착", "선택지 라벨", "조건", "잠금시 숨김", "잠금 안내문"],
            ["ep1", "ep2", null, null, "FALSE", null]
        ]);

        Fill(workbook.AddWorksheet("조건"),
        [
            ["라벨", "조건식", "설명"],
            ["신뢰높음", "trust >= 3", "라루를 신뢰"]
        ]);

        Fill(workbook.AddWorksheet("스탯"),
        [
            ["스탯키", "표시명", "초기값", "최소", "최대"],
            ["trust", "신뢰", "0", "0", "10"],
            ["anger", "분노", "0", "0", "10"]
        ]);

        Fill(workbook.AddWorksheet("픽스처"),
        [
            ["픽스처명", "활성", "trust", "anger", "고정 선택 (에피소드ID→도착ID)"],
            ["기본", "TRUE", "0", "0", "ep1→ep2"]
        ]);

        workbook.SaveAs(path);
        return path;
    }

    private static void Fill(IXLWorksheet sheet, string?[][] rows)
    {
        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is { Length: > 0 } value)
                {
                    sheet.Cell(row + 1, column + 1).SetValue(value);
                }
            }
        }
    }
}
