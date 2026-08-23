using ClosedXML.Excel;
using System.Text;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 넘겨줄 <b>씨앗 프로젝트</b> (2026-08-17 소유자 — 비개발자 인계).
///
/// 빈 exe만 주면 첫 화면이 막다른 길이다: 새로 만든 `game.definition.json`은 `variables`가
/// 비어 있어 <b>스탯이 하나도 없고</b>, 스탯이 없으면 조건을 못 만들고, 조건이 없으면 간선의
/// 관문도 대본의 `IF`도 못 쓴다 — 분기 있는 이야기를 아예 시작할 수 없다.
///
/// 그래서 <b>이미 돌아가는 최소 한 판</b>을 함께 준다. 켜서 열면 분기 하나가 실제로 서 있고,
/// 그것을 고쳐 나가면 된다.
///
/// <b>이 테스트가 곧 씨앗의 정의다.</b> 만드는 법과 "오류 0"이 한자리에 있어서, 규격이 바뀌면
/// 여기가 먼저 빨개진다 — 손으로 만든 폴더를 저장소에 넣어 두면 조용히 낡는다.
/// </summary>
public sealed class StarterProjectTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-starter", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 씨앗_프로젝트는_오류_없이_열리고_분기가_서_있다() => HeadlessUi.Run(() =>
    {
        string manifest = StarterProject.Create(_directory);

        var session = new AuthoringSession();
        session.Open(manifest);

        // 스탯이 있다 — 여기서 막히면 조건도 관문도 못 쓴다(빈 프로젝트의 막다른 길).
        Assert.Contains("trust", session.Definition.Variables.Select(item => item.Name));

        // 받는 사람이 처음 여는 것은 폴더다 — 무엇부터 열지 적힌 한 장이 같이 있다.
        Assert.True(File.Exists(Path.Combine(_directory, "읽어주세요.txt")));

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(
            Path.Combine(_directory, ChapterLibrary.FolderName, "ch01.xlsx"),
            session.Definition);

        Assert.DoesNotContain(chapter.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Error);

        // 분기 하나가 실제로 서 있다 — 문구 둘이 갈라졌다 다시 만난다.
        Assert.Equal(4, chapter.Episodes.Count);
        Assert.Equal(
            ["라루를 믿는다", "문을 연다", "문을 연다", "혼자 간다"],
            chapter.Edges.Where(edge => !edge.HasNoOptionLabel)
                .Select(edge => edge.OptionLabel!)
                .Order(StringComparer.Ordinal));

        // 도달성 증명이 통과한다 — 닿을 수 없는 에피소드가 없다.
        ChapterValidationResult validation = ChapterValidator.Validate(
            chapter, EpisodeLibrary.FolderFor(manifest, "ch01")!);

        Assert.False(validation.HasErrors, string.Join("\n", validation.All.Select(item => item.Message)));
    });

    [Fact]
    public void 씨앗의_대본은_v10_블록으로_읽히고_펴진다() => HeadlessUi.Run(() =>
    {
        string manifest = StarterProject.Create(_directory);

        string script = Path.Combine(
            EpisodeLibrary.FolderFor(manifest, "ch01")!, "믿는길.xlsx");

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(
            Path.Combine(_directory, ChapterLibrary.FolderName, "ch01.xlsx"));

        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(
            script, chapter.Conditions.Select(condition => condition.Label).ToList());

        Assert.Empty(model.Errors);
        Assert.Contains(model.Rows, row => row.Kind == EpisodeRowKind.If);
        Assert.Contains(model.Rows, row => row.Kind == EpisodeRowKind.End);

        EpisodeFlattenResult flattened = EpisodeFlattener.Flatten(
            model, chapter.Conditions.ToDictionary(item => item.Label, StringComparer.Ordinal));

        Assert.Empty(flattened.Errors);
        Assert.Contains("<<if", flattened.Text, StringComparison.Ordinal);
        Assert.Contains("<<endif>>", flattened.Text, StringComparison.Ordinal);
    });

    [Fact]
    public void 씨앗을_열면_진행_JSON이_저절로_나간다() => HeadlessUi.Run(() =>
    {
        // 검증을 통과하는 판이라는 뜻이다 — 받는 사람의 첫 화면에 빨간 것이 없다.
        string manifest = StarterProject.Create(_directory);

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(
            Path.Combine(_directory, ChapterLibrary.FolderName, "ch01.xlsx"));

        ChapterExportResult result = ChapterProgressionExporter.Export(
            chapter, EpisodeLibrary.FolderFor(manifest, "ch01")!);

        Assert.False(result.Refused, string.Join("\n", result.Validation.All.Select(item => item.Message)));
        Assert.Contains("\"StartEpisodeId\": \"시작\"", result.Json!, StringComparison.Ordinal);
    });
}

/// <summary>
/// 씨앗 한 판을 만든다 — <b>사람이 손으로 하는 순서 그대로</b> 진짜 라이터를 지난다.
/// 그래서 규격이 바뀌면 여기도 같이 깨지고, 손으로 만든 폴더처럼 조용히 낡지 않는다.
/// </summary>
internal static class StarterProject
{
    /// <returns>만들어진 프로젝트 매니페스트 경로.</returns>
    public static string Create(string folder)
    {
        Directory.CreateDirectory(folder);

        string manifest = Path.Combine(folder, "예제" + ProjectManifestJson.FileExtension);

        // ① 프로젝트 — 첫 저장이 assets 폴더와 기본 튜닝, 빈 정의 파일까지 준비한다.
        var session = new AuthoringSession();
        session.Save(manifest);

        // ② 스탯 — 정의 파일이 어휘의 원천이다(챕터 `스탯` 시트는 그 미러).
        //    여기가 비면 조건도 관문도 못 만든다.
        GameDefinitionStore.AddVariables(manifest,
        [
            new VariableSpec { Name = "trust", Type = "number", Description = "신뢰" },
            new VariableSpec { Name = "fatigue", Type = "number", Description = "피로" }
        ]);

        // ③ 챕터 — 스탯 미러가 함께 깔린다.
        string chapters = Path.Combine(folder, ChapterLibrary.FolderName);
        ChapterWorkbookWriter.EnsureChapterWorkbook(
            chapters, "ch01", [("trust", "신뢰"), ("fatigue", "피로")]);

        string chapter = Path.Combine(chapters, "ch01.xlsx");

        // 화자는 챕터가 아니라 프로젝트의 것이다 (2026-08-23) — 툴 [화자] 탭이 쓰는
        // 그 배열에 그대로 적는다.
        GameDefinitionStore.SaveSpeakers(manifest,
        [
            new SpeakerSpec { Name = "윌로", CharacterId = "willo" },
            new SpeakerSpec { Name = "라루", CharacterId = "laru" }
        ]);

        ChapterWorkbookWriter.AddCondition(chapter, "신뢰높음", "trust >= 2", "라루를 믿기로 했다면");

        // ④ 에피소드 넷 — 갈라졌다 다시 만나는 최소 모양.
        ChapterWorkbookWriter.AddEpisode(chapter, "시작", "복도", 0, 0);
        ChapterWorkbookWriter.AddEpisode(chapter, "믿는길", "함께 간다", 1, 0);
        ChapterWorkbookWriter.AddEpisode(chapter, "혼자길", "혼자 간다", 1, 1);
        ChapterWorkbookWriter.AddEpisode(chapter, "끝", "문 앞", 2, 0);

        // ⑤ 길 — 문구가 붙은 둘이 선택지, 문구 없는 둘이 보이지 않는 기본(자동 진행).
        ChapterWorkbookWriter.AddEdge(
            chapter, "시작", "믿는길", optionLabel: "라루를 믿는다", statChanges: "trust +2");
        ChapterWorkbookWriter.AddEdge(
            chapter, "시작", "혼자길", optionLabel: "혼자 간다", statChanges: "fatigue +1");
        // v12 — 문구 없는 길은 폐지됐다. 넘어가기만 하는 자리도 버튼 이름을 갖는다.
        ChapterWorkbookWriter.AddEdge(chapter, "믿는길", "끝", optionLabel: "문을 연다");
        ChapterWorkbookWriter.AddEdge(chapter, "혼자길", "끝", optionLabel: "문을 연다");

        // ⑥ 대본 — 툴이 만드는 것은 빈 규격 워크북까지다(v4). 대사는 사람이 쓰는 것이라
        //    여기서는 씨앗을 심는 손이 대신 쓴다.
        string scripts = EpisodeLibrary.FolderFor(manifest, "ch01")!;
        List<string> speakers = ["윌로", "라루"];
        List<string> labels = ["신뢰높음"];

        foreach (string episode in (string[])["시작", "믿는길", "혼자길", "끝"])
        {
            EpisodeLibrary.EnsureWorkbook(scripts, episode, speakers, labels);
        }

        WriteScript(scripts, "시작",
        [
            (10, "", "", "윌로", "복도는 조용했다."),
            (20, "", "", "라루", "같이 갈까?")
        ]);

        // 조건 블록이 있는 대본 — IF ~ ENDIF가 어떻게 생겼는지 실물로 보여 준다.
        WriteScript(scripts, "믿는길",
        [
            (10, "", "", "라루", "고맙다는 말은 안 할래."),
            (20, "IF", "신뢰높음", "", ""),
            (30, "", "", "윌로", "알아. 너답네."),
            (40, "ENDIF", "", "", ""),
            (50, "", "", "라루", "가자.")
        ]);

        WriteScript(scripts, "혼자길",
        [
            (10, "", "", "윌로", "혼자 걷는 복도는 길었다.")
        ]);

        WriteScript(scripts, "끝",
        [
            (10, "", "", "윌로", "문이 열렸다.")
        ]);

        // ⑦ 받는 사람이 처음 여는 것은 exe가 아니라 폴더다 — 무엇부터 열지 한 장으로 적어 둔다.
        File.WriteAllText(Path.Combine(folder, "읽어주세요.txt"), Guide, Encoding.UTF8);

        return manifest;
    }

    /// <summary>폴더를 처음 연 사람에게 주는 한 장 — 무엇부터 열고, 무엇을 만지는지.</summary>
    private const string Guide = """
        예제 프로젝트 — 돌아가는 최소 한 판

        [처음 할 일]
        1. Vn.App.exe 를 켠다.
        2. 이 폴더의 `예제.vnproject.json` 을 연다.
        3. 챕터 그래프 탭에서 ch01 을 고른다. 네모 넷과 그것을 잇는 길이 보인다.

        [무엇이 들어 있나]
          시작 ─(라루를 믿는다 · 신뢰 +2)→ 믿는길 ─→ 끝
            └─(혼자 간다 · 피로 +1)→ 혼자길 ─┘
          갈라졌다 다시 만나는 최소 모양이다. 이 위에 덧그리면 된다.

        [무엇이 어디 있나]
          chapters/ch01.xlsx   기획자의 판 — 스탯 · 화자 · 조건 · 에피소드 · 길
          episodes/ch01/*.xlsx 대본 — 에피소드 하나에 파일 하나, 여기에 대사를 쓴다
          assets/portraits/    표정 그림을 넣는 곳 (지금은 비어 있다)
          game.definition.json 스탯 이름의 원천 (trust · fatigue)

        [엑셀을 열어 둔 채로는 툴이 그 파일을 고치지 못한다]
        툴에서 편집이 잠기면 대개 그 엑셀이 아직 열려 있어서다. 엑셀을 닫으면 풀린다.

        [대본에서 조건 쓰는 법]  episodes/ch01/믿는길.xlsx 를 열어 보면 실물이 있다.
          유형 칸에서 IF 를 고르고, 조건라벨 칸에서 챕터가 정해 둔 라벨을 고른다.
          그 아래 대사들을 쓰고, 끝나는 자리에 ENDIF 를 한 줄 둔다.
          (중간에 갈래를 더 두고 싶으면 ELSEIF 를 쓴다.)

        [새 스탯 · 새 조건이 필요하면]
        스탯은 chapters/ch01.xlsx 의 `스탯` 시트, 조건은 `조건` 시트에 한 줄 더한다.
        대본의 드롭다운은 툴이 챕터를 읽을 때 따라온다.
        """;

    /// <summary>대본 행을 채운다 — v13 6열(인덱스·유형·LineId·조건라벨·화자·내용).</summary>
    private static void WriteScript(
        string folder,
        string episodeId,
        (int Index, string Kind, string Label, string Speaker, string Text)[] rows)
    {
        string path = EpisodeLibrary.FindExisting(folder, episodeId)!;

        using var memory = new MemoryStream(File.ReadAllBytes(path));
        using var workbook = new XLWorkbook(memory);
        IXLWorksheet sheet = workbook.Worksheet("대본");

        for (int offset = 0; offset < rows.Length; offset++)
        {
            int number = offset + 2;
            (int index, string kind, string label, string speaker, string text) = rows[offset];

            sheet.Cell(number, 1).SetValue(index);
            Set(sheet, number, 2, kind);   // v13 — 유형이 앞이다
            Set(sheet, number, 4, label);
            Set(sheet, number, 5, speaker);
            Set(sheet, number, 6, text);
        }

        workbook.SaveAs(path);
    }

    private static void Set(IXLWorksheet sheet, int row, int column, string value)
    {
        if (value.Length > 0)
        {
            sheet.Cell(row, column).SetValue(value);
        }
    }
}
