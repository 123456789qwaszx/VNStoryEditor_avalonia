using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 챕터 `화자` 시트 (2026-08-16 소유자 지시) — 기획자가 챕터에서 화자를 등록하면
/// 에피소드 워크북의 화자 열(H)이 그 목록의 드롭다운을 받는다.
///
/// 계약의 핵심 셋: ① 시트가 없어도(구판) 챕터는 성립한다 ② 드롭다운 갱신은 원고
/// 파일의 <b>숨김 목록 시트만</b> 만지고, 목록이 같으면 파일에 아예 손대지 않는다
/// ③ 검증은 조언일 뿐 — 목록 밖 이름도 그대로 적을 수 있다.
/// </summary>
public sealed class ChapterSpeakerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-speaker-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ── 챕터 워크북 ─────────────────────────────────────────────────────────

    private string WriteChapter(params string?[][] speakerRows)
    {
        var sheets = new List<(string, string?[][])>
        {
            (ChapterSheetNames.Episodes, new[]
            {
                new string?[] { "EpisodeId", "제목", "종류", "대사엔트리", "X", "Y" },
                new string?[] { "ep01", null, "Main", "ep01", "0", "0" }
            }),
            (ChapterSheetNames.Edges, new[]
            {
                new string?[] { "출발", "도착", "스탯변화", "선택지수", "조건", "잠금시 숨김", "잠금 안내문" }
            }),
            (ChapterSheetNames.Conditions, new[] { new string?[] { "라벨", "스탯", "연산자", "값", "설명" } }),
            (ChapterSheetNames.Stats, new[]
            {
                new string?[] { "스탯키", "표시명", "초기값", "최소", "최대", "타입" },
                new string?[] { "trust", "신뢰", "0", "0", "10", null },
                new string?[] { "fatigue", "피로", "0", "0", "10", null }
            })
        };

        if (speakerRows.Length > 0)
        {
            var rows = new List<string?[]> { new string?[] { "이름", "캐릭터키", "메모" } };
            rows.AddRange(speakerRows);
            sheets.Add((ChapterSheetNames.Speakers, rows.ToArray()));
        }

        return XlsxTestWorkbook.Write(_directory, "ch_speaker.xlsx", sheets.ToArray());
    }

    [Fact]
    public void 화자_시트가_이름_캐릭터키_메모로_읽힌다()
    {
        string path = WriteChapter(
            ["라루", "raru", "주연"],
            ["윌로", null, null]);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        Assert.True(model.HasSpeakerSheet);
        Assert.Equal(["라루", "윌로"], model.Speakers.Select(speaker => speaker.Name).ToArray());
        Assert.Equal("raru", model.Speakers[0].CharacterId);
        Assert.Equal("주연", model.Speakers[0].Memo);
        Assert.Null(model.Speakers[1].CharacterId);
        Assert.False(model.HasErrors);
    }

    [Fact]
    public void 화자_시트가_없는_구판_워크북은_진단_없이_빈_목록이다()
    {
        string path = WriteChapter();

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        Assert.False(model.HasSpeakerSheet);
        Assert.Empty(model.Speakers);

        // 구판 파일마다 "화자 시트가 없다"고 떠들지 않는다 — 앱이 조용히 만들어 준다.
        Assert.DoesNotContain(model.Diagnostics, item =>
            item.Sheet == ChapterSheetNames.Speakers);
    }

    [Fact]
    public void 중복_화자는_경고하고_첫_행만_쓴다()
    {
        string path = WriteChapter(
            ["라루", "raru", null],
            ["라루", "raru2", null]);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        Assert.Single(model.Speakers, speaker => speaker.Name == "라루");
        Assert.Equal("raru", model.Speakers[0].CharacterId);
        Assert.Contains(model.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Warning &&
            item.Sheet == ChapterSheetNames.Speakers &&
            item.Message.Contains("두 번 등록"));
    }

    [Fact]
    public void 새_챕터_워크북에_화자_시트가_생긴다()
    {
        Assert.True(ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch_new"));

        ChapterGraphModel model = ChapterWorkbookReader.Read(
            Path.Combine(_directory, "ch_new.xlsx"));

        Assert.True(model.HasSpeakerSheet);
        Assert.Empty(model.Speakers);
    }

    [Fact]
    public void 구판_챕터에_화자_시트를_한_번만_만든다()
    {
        string path = WriteChapter(); // 화자 시트 없음

        (bool created, ChapterWriteResult result) = ChapterWorkbookWriter.EnsureSpeakerSheet(path);
        Assert.True(created);
        Assert.True(result.Written);
        Assert.True(ChapterWorkbookReader.Read(path).HasSpeakerSheet);

        // 두 번째 부름은 파일에 손대지 않는다 — 폴더 감시가 맴돌면 안 된다.
        DateTime before = File.GetLastWriteTimeUtc(path);
        (bool again, ChapterWriteResult second) = ChapterWorkbookWriter.EnsureSpeakerSheet(path);

        Assert.False(again);
        Assert.True(second.Written || second.Failure is null);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void 툴의_화자_추가는_시트에_한_줄을_쓰고_중복을_거부한다()
    {
        string path = WriteChapter(["라루", null, null]);

        Assert.True(ChapterWorkbookWriter.AddSpeaker(path, "윌로", "willo").Written);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        Assert.Equal(["라루", "윌로"], model.Speakers.Select(speaker => speaker.Name).ToArray());

        ChapterWriteResult duplicate = ChapterWorkbookWriter.AddSpeaker(path, "라루");
        Assert.False(duplicate.Written);
        Assert.Contains("이미 있습니다", duplicate.Failure);
    }

    // ── 에피소드 워크북 드롭다운 ────────────────────────────────────────────

    [Fact]
    public void 화자가_있으면_새_에피소드_워크북에_숨김_목록과_드롭다운이_선다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep01", ["라루", "윌로"]);
        string path = EpisodeLibrary.PathFor(_directory, "ep01");

        using var workbook = new XLWorkbook(path);

        IXLWorksheet list = workbook.Worksheet(EpisodeLibrary.SpeakerListSheetName);
        Assert.Equal(XLWorksheetVisibility.Hidden, list.Visibility);
        Assert.Equal("라루", list.Cell(1, 1).GetString());
        Assert.Equal("윌로", list.Cell(2, 1).GetString());

        // H열(화자)에 목록 검증이 걸려 있고, 조언일 뿐이라 오류 경고는 띄우지 않는다.
        IXLWorksheet script = workbook.Worksheet("대본");
        IXLDataValidation validation = script.DataValidations.Single(candidate =>
            candidate.Ranges.Any(range => range.RangeAddress.FirstAddress.ColumnNumber == 8));

        Assert.Equal(XLAllowedValues.List, validation.AllowedValues);
        Assert.Contains(EpisodeLibrary.SpeakerListSheetName, validation.Value);
        Assert.False(validation.ShowErrorMessage);
    }

    [Fact]
    public void 화자가_없으면_에피소드_워크북은_이전과_같다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep_plain");

        using var workbook = new XLWorkbook(EpisodeLibrary.PathFor(_directory, "ep_plain"));

        Assert.DoesNotContain(workbook.Worksheets, sheet =>
            sheet.Name == EpisodeLibrary.SpeakerListSheetName);
    }

    [Fact]
    public void 목록이_같으면_기존_워크북에_손대지_않는다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep02", ["라루"]);
        string path = EpisodeLibrary.PathFor(_directory, "ep02");
        DateTime before = File.GetLastWriteTimeUtc(path);

        EpisodeLibrary.SpeakerListPush push =
            EpisodeLibrary.PushSpeakerList(_directory, "ep02", ["라루"]);

        Assert.False(push.Changed);
        Assert.Null(push.Failure);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path)); // B7 — 툴은 원고 시각을 안 바꾼다
        Assert.False(File.Exists(path + ".bak"));             // 안 쓴 파일에 백업도 없다
    }

    [Fact]
    public void 목록이_바뀌면_숨김_시트만_갈리고_원고_행은_그대로다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep03", ["라루"]);
        string path = EpisodeLibrary.PathFor(_directory, "ep03");

        // 작가의 원고를 흉내 낸다 — 갱신 뒤에도 그대로여야 한다.
        using (var workbook = new XLWorkbook(path))
        {
            IXLWorksheet script = workbook.Worksheet("대본");
            script.Cell(2, 8).SetValue("라루");
            script.Cell(2, 9).SetValue("첫 줄이다.");
            workbook.Save();
        }

        EpisodeLibrary.SpeakerListPush push =
            EpisodeLibrary.PushSpeakerList(_directory, "ep03", ["라루", "윌로"]);

        Assert.True(push.Changed);
        Assert.True(File.Exists(path + ".bak")); // 원고를 다시 쓰는 유일한 순간 — 직전을 남긴다

        using var updated = new XLWorkbook(path);
        IXLWorksheet list = updated.Worksheet(EpisodeLibrary.SpeakerListSheetName);
        Assert.Equal("윌로", list.Cell(2, 1).GetString());

        IXLWorksheet after = updated.Worksheet("대본");
        Assert.Equal("라루", after.Cell(2, 8).GetString());
        Assert.Equal("첫 줄이다.", after.Cell(2, 9).GetString());
    }

    [Fact]
    public void 드롭다운_없이_만든_구판_워크북도_갱신이_목록과_검증을_세운다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep04"); // 화자 기능 전의 워크북

        EpisodeLibrary.SpeakerListPush push =
            EpisodeLibrary.PushSpeakerList(_directory, "ep04", ["라루"]);

        Assert.True(push.Changed);

        using var workbook = new XLWorkbook(EpisodeLibrary.PathFor(_directory, "ep04"));
        Assert.Equal("라루",
            workbook.Worksheet(EpisodeLibrary.SpeakerListSheetName).Cell(1, 1).GetString());
        Assert.Contains(workbook.Worksheet("대본").DataValidations, validation =>
            validation.Ranges.Any(range => range.RangeAddress.FirstAddress.ColumnNumber == 8));
    }

    [Fact]
    public void 잠긴_워크북이면_갱신하지_않고_사유를_말한다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep05", ["라루"]);
        string path = EpisodeLibrary.PathFor(_directory, "ep05");

        // 공유 없는 핸들 — 엑셀의 배타 잠금을 흉내 낸다.
        using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        EpisodeLibrary.SpeakerListPush push =
            EpisodeLibrary.PushSpeakerList(_directory, "ep05", ["라루", "윌로"]);

        Assert.False(push.Changed);
        Assert.Contains("넣지 못했습니다", push.Failure);
    }

    [Fact]
    public void 워크북이_아직_없으면_갱신은_아무것도_하지_않는다()
    {
        EpisodeLibrary.SpeakerListPush push =
            EpisodeLibrary.PushSpeakerList(_directory, "ep_none", ["라루"]);

        Assert.False(push.Changed);
        Assert.Null(push.Failure);
    }
}
