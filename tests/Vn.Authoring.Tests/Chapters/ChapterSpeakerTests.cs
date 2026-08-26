using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 대본 워크북의 화자 드롭다운과, <b>폐지된 챕터 `화자` 시트</b>의 뒷정리.
///
/// ⚠ 2026-08-23에 챕터 시트가 사라졌다 (소유자: "엑셀 내 어떤 것에서도 화자를 사용하지
/// 않는다 … 애초부터 챕터엑셀에 화자가 들어갈 이유가 전혀없다"). 등록 자리는 툴의 [화자]
/// 탭이고 값은 `game.definition.json`에 산다. 여기 남은 챕터 쪽 고정은 <b>만들지 않는다</b>와
/// <b>구판 시트를 지운다</b> 둘뿐이다.
///
/// 대본 쪽 계약은 그대로다: ① 드롭다운 갱신은 원고 파일의 <b>숨김 목록 시트만</b> 만지고,
/// 목록이 같으면 파일에 아예 손대지 않는다 ② 검증은 조언일 뿐 — 목록 밖 이름도 적을 수 있다.
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
                new string?[] { "EpisodeId", "대사엔트리", "제목", "이벤트키", "X", "Y" },
                new string?[] { "ep01", "ep01", null, null, "0", "0" }
            }),
            (ChapterSheetNames.Edges, new[]
            {
                new string?[] { "출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금시 숨김", "잠금 안내문" }
            }),
            (ChapterSheetNames.Conditions, new[] { new string?[] { "라벨", "스탯", "연산자", "값", "설명" } }),
            (ChapterSheetNames.Stats, new[]
            {
                new string?[] { "타입", "스탯키", "표시명", "초기값", "최소", "최대" },
                new string?[] { null, "trust", "신뢰", "0", "0", "10" },
                new string?[] { null, "fatigue", "피로", "0", "0", "10" }
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

        // 시트가 없는 것이 이제 정상이다 (2026-08-23 폐지) — 진단으로 떠들지 않는다.
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
    public void 새_챕터_워크북에는_화자_시트가_없다()
    {
        // 2026-08-23 — 챕터 엑셀의 어느 시트도 화자를 안 쓴다. 만들지 않는 것이 규격이다.
        Assert.True(ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch_new"));

        ChapterGraphModel model = ChapterWorkbookReader.Read(
            Path.Combine(_directory, "ch_new.xlsx"));

        Assert.False(model.HasSpeakerSheet);
        Assert.Empty(model.Speakers);
    }

    [Fact]
    public void 구판_화자_시트를_지운다()
    {
        // 이행의 절반 — 나머지 절반(이름을 정의 파일로 옮기기)은 앱 계층이 진다.
        // 여기서는 <b>지워졌는가</b>와 <b>원본이 .bak에 남는가</b>만 못 박는다.
        string path = WriteChapter(["라루", "raru", null]);
        Assert.True(ChapterWorkbookReader.Read(path).HasSpeakerSheet);

        (bool removed, ChapterWriteResult result) = ChapterWorkbookWriter.RemoveSpeakerSheet(path);
        Assert.True(removed);
        Assert.Null(result.Failure);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        Assert.False(model.HasSpeakerSheet);
        Assert.Empty(model.Speakers);
        Assert.True(File.Exists(path + ".bak"));

        // 나머지 시트는 그대로다 — 지우는 것은 `화자` 하나뿐이다.
        Assert.False(model.HasErrors);
        Assert.Single(model.Episodes);
    }

    [Fact]
    public void 이미_없는_화자_시트에는_손대지_않는다()
    {
        // 재읽기마다 불리는 자리다 — 없는 시트를 지우겠다고 파일을 다시 저장하면
        // 폴더 감시가 맴돈다.
        string path = WriteChapter(); // 화자 시트 없음
        DateTime before = File.GetLastWriteTimeUtc(path);

        (bool removed, ChapterWriteResult result) = ChapterWorkbookWriter.RemoveSpeakerSheet(path);

        Assert.False(removed);
        Assert.Null(result.Failure);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
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

        // E열(화자, v10)에 목록 검증이 걸려 있고, 조언일 뿐이라 오류 경고는 띄우지 않는다.
        IXLWorksheet script = workbook.Worksheet("대본");
        IXLDataValidation validation = script.DataValidations.Single(candidate =>
            candidate.Ranges.Any(range => range.RangeAddress.FirstAddress.ColumnNumber == 5));

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

        EpisodeLibrary.VocabularyPush push =
            EpisodeLibrary.PushVocabulary(_directory, "ep02", ["라루"], []);

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
            script.Cell(2, 5).SetValue("라루");
            script.Cell(2, 6).SetValue("첫 줄이다.");
            workbook.Save();
        }

        EpisodeLibrary.VocabularyPush push =
            EpisodeLibrary.PushVocabulary(_directory, "ep03", ["라루", "윌로"], []);

        Assert.True(push.Changed);
        Assert.True(File.Exists(path + ".bak")); // 원고를 다시 쓰는 유일한 순간 — 직전을 남긴다

        using var updated = new XLWorkbook(path);
        IXLWorksheet list = updated.Worksheet(EpisodeLibrary.SpeakerListSheetName);
        Assert.Equal("윌로", list.Cell(2, 1).GetString());

        IXLWorksheet after = updated.Worksheet("대본");
        Assert.Equal("라루", after.Cell(2, 5).GetString());
        Assert.Equal("첫 줄이다.", after.Cell(2, 6).GetString());
    }

    [Fact]
    public void 드롭다운_없이_만든_구판_워크북도_갱신이_목록과_검증을_세운다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep04"); // 화자 기능 전의 워크북

        EpisodeLibrary.VocabularyPush push =
            EpisodeLibrary.PushVocabulary(_directory, "ep04", ["라루"], []);

        Assert.True(push.Changed);

        using var workbook = new XLWorkbook(EpisodeLibrary.PathFor(_directory, "ep04"));
        Assert.Equal("라루",
            workbook.Worksheet(EpisodeLibrary.SpeakerListSheetName).Cell(1, 1).GetString());
        Assert.Contains(workbook.Worksheet("대본").DataValidations, validation =>
            validation.Ranges.Any(range => range.RangeAddress.FirstAddress.ColumnNumber == 5));
    }

    [Fact]
    public void 잠긴_워크북이면_갱신하지_않고_사유를_말한다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep05", ["라루"]);
        string path = EpisodeLibrary.PathFor(_directory, "ep05");

        // 공유 없는 핸들 — 엑셀의 배타 잠금을 흉내 낸다.
        using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        EpisodeLibrary.VocabularyPush push =
            EpisodeLibrary.PushVocabulary(_directory, "ep05", ["라루", "윌로"], []);

        Assert.False(push.Changed);
        Assert.Contains("넣지 못했습니다", push.Failure);
    }

    [Fact]
    public void 워크북이_아직_없으면_갱신은_아무것도_하지_않는다()
    {
        EpisodeLibrary.VocabularyPush push =
            EpisodeLibrary.PushVocabulary(_directory, "ep_none", ["라루"], []);

        Assert.False(push.Changed);
        Assert.Null(push.Failure);
    }
}
