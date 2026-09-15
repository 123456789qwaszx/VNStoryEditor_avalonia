using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.Authoring.Chapters.Import;

/// <summary>들여온 에피소드 하나.</summary>
/// <param name="IssuedLineIds">이 에피소드가 새로 발급받은 줄 신원.</param>
public sealed record EpisodeImportEntry(
    string EpisodeId,
    string WorkbookPath,
    string DialogueNodeId,
    IReadOnlyList<string> IssuedLineIds);

/// <param name="Applied">전부 반영됐는가. 거부면 <b>아무것도</b> 반영되지 않았다.</param>
/// <param name="Notices">사람에게 보일 말(이행했다·잠겨서 못 했다). 순서가 일어난 순서다.</param>
public sealed record EpisodeImport(
    bool Applied,
    IReadOnlyList<EpisodeImportEntry> Entries,
    IReadOnlyList<ChapterDiagnostic> Diagnostics,
    IReadOnlyList<string> Notices)
{
    public IEnumerable<ChapterDiagnostic> Errors =>
        Diagnostics.Where(item => item.Severity == ChapterDiagnosticSeverity.Error);
}

/// <summary>
/// <b>대본 워크북 → 대사노드는 여기를 지난다</b> (R-D · 2026-09-16 —
/// <c>docs/work-orders/tool-owns-workbooks-orders.md</c> §5).
///
/// <b>이것은 <see cref="EpisodeSyncService"/>의 후신이 아니라 그 <em>축소판</em>이다.</b>
/// 동기화는 "워크북이 바뀔 때마다 다시 읽고 프로젝트와 맞춘다"였기 때문에 신원 매칭
/// (<c>ExcelLineMap</c>) · 가지치기 보고 · 되쓰기가 필요했다. 임포트는 <b>한 번</b>이므로
/// 맞출 상대가 없다 — 읽어서 노드를 세우면 그걸로 끝이고, 그 뒤로 이 워크북은 산출물이다.
///
/// ⛔ <b>부분 성공하지 않는다</b> (§5.2). 하나라도 해석 못 하면 <b>아무것도</b> 들여오지
/// 않고 진단만 낸다. 그래서 읽기(1단계)와 반영(2단계)이 갈라져 있다 — 절반만 들어온
/// 프로젝트는 무엇이 원본인지 사람이 알 수 없게 만든다(<c>ScriptSynchronizer</c>의
/// <i>"애매하면 멈춘다"</i>가 여기로 옮겨 온 것이다).
///
/// ⚠ 이 클래스는 화면·세션을 모른다. 그래야 화면 없이 시험된다.
/// </summary>
public static class EpisodeWorkbookImporter
{
    // v15 — 대본은 조건을 모른다. 평평화가 받는 자리는 남아 있으나 쓰이지 않는다.
    private static readonly Dictionary<string, ChapterCondition> NoConditions =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 한 챕터의 대본 폴더를 프로젝트로 들여온다. <b>명시적 동작이다</b> — 파일 감시가
    /// 부르지 않는다(§5.2).
    /// </summary>
    /// <param name="fileId">이 챕터의 판. 노드는 그 판 안에서만 찾고 만든다.</param>
    public static EpisodeImport Run(
        ProjectEditor editor,
        GameDefinition definition,
        string fileId,
        string episodesFolder,
        ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(chapter);

        var diagnostics = new List<ChapterDiagnostic>();
        var notices = new List<string>();
        var planned = new List<Planned>();

        // ── 1단계 · 전부 읽고 편다. 아직 프로젝트에 손대지 않는다 ──────────
        foreach (ChapterEpisode episode in chapter.Episodes)
        {
            if (EpisodeLibrary.FindExisting(episodesFolder, episode.EpisodeId) is not { } path)
            {
                continue;   // 아직 대본이 없는 에피소드다 — 오류가 아니다.
            }

            // 구판 대본을 현행 규격으로. 임포트는 한 번이므로 이행도 한 번이다.
            EpisodeWorkbookMigrator.MigrationResult migration =
                EpisodeWorkbookMigrator.Migrate(path);

            if (migration.Failure is { } spoken)
            {
                notices.Add(spoken);
            }
            else if (migration.Migrated)
            {
                notices.Add(
                    $"'{Path.GetFileName(path)}'를 새 대본 규격(4열)으로 이행했습니다" +
                    "(이전 상태는 .bak).");
            }

            EpisodeWorkbookModel model;

            try
            {
                model = EpisodeWorkbookReader.Read(path);
            }
            catch (XlsxReadException exception)
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.SheetMissing,
                    path, null, null, null,
                    $"대본 워크북을 읽지 못했습니다: {exception.Message}"));
                continue;
            }

            diagnostics.AddRange(model.Diagnostics);
            WarnMergedSpeakers(model, definition, path, diagnostics);

            EpisodeFlattenResult flattened = EpisodeFlattener.Flatten(model, NoConditions);
            diagnostics.AddRange(flattened.Diagnostics);

            planned.Add(new Planned(episode.EpisodeId, path, flattened.Text));
        }

        // ⛔ 여기가 §5.2의 관문이다 — 하나라도 깨졌으면 아무것도 들여오지 않는다.
        if (diagnostics.Any(item => item.Severity == ChapterDiagnosticSeverity.Error))
        {
            return new EpisodeImport(
                Applied: false, Array.Empty<EpisodeImportEntry>(), diagnostics, notices);
        }

        // ── 2단계 · 반영 ──────────────────────────────────────────────────
        var entries = new List<EpisodeImportEntry>();

        foreach (Planned item in planned)
        {
            DialogueNode node = FindOrCreateNode(editor, fileId, item.EpisodeId, chapter);

            // 대사를 한 줄도 안 쓴 에피소드에도 <b>노드는 선다</b> (2026-08-17 소유자) —
            // 빈 노드라도 서 있으면 작가에게 "여기에 쓰면 된다"가 보인다. 본문만
            // 건드리지 않는다(빈 글을 밀어 넣으면 지우기로 읽힌다).
            if (item.Text.Trim().Length == 0)
            {
                entries.Add(new EpisodeImportEntry(
                    item.EpisodeId, item.WorkbookPath, node.Id, Array.Empty<string>()));
                continue;
            }

            ScenarioPasteOutcome outcome = editor.ApplyScenarioText(
                node.Id, item.Text, definition, confirmDeletes: true);

            if (!outcome.Applied)
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.SheetMissing,
                    item.WorkbookPath, null, null, null,
                    $"'{item.EpisodeId}'의 대사를 반영하지 못했습니다: {outcome.Summary()}"));

                return new EpisodeImport(
                    Applied: false, Array.Empty<EpisodeImportEntry>(), diagnostics, notices);
            }

            // 새로 발급된 신원 — 이 임포트가 이 에피소드에 심은 줄들이다.
            IReadOnlyList<string> issued = outcome.Plan!.Entries
                .Where(entry => entry.Kind == ScriptSyncKind.Inserted && entry.LineId is not null)
                .Select(entry => entry.LineId!)
                .ToList();

            entries.Add(new EpisodeImportEntry(
                item.EpisodeId, item.WorkbookPath, node.Id, issued));
        }

        return new EpisodeImport(Applied: true, entries, diagnostics, notices);
    }

    /// <summary>1단계가 거둔 것 — 읽고 편 텍스트. 프로젝트와는 아직 무관하다.</summary>
    private readonly record struct Planned(string EpisodeId, string WorkbookPath, string Text);

    /// <summary>
    /// 공백 있는 미등록 화자는 파서가 산문으로 보아 대사와 합친다("화자와 내용이 합쳐진다" —
    /// 실사례). 등록된 이름이면 공백이 있어도 통과하므로, 여기 걸리는 것은 오타이거나 아직
    /// 등록 안 된 이름이다 — 조용히 합쳐지기 전에 말해 준다.
    /// </summary>
    private static void WarnMergedSpeakers(
        EpisodeWorkbookModel model,
        GameDefinition definition,
        string path,
        List<ChapterDiagnostic> diagnostics)
    {
        foreach (EpisodeRow row in model.Rows)
        {
            if (row.Speaker.Any(char.IsWhiteSpace) &&
                definition.FindSpeakerCharacterId(row.Speaker) is null)
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, model.SheetName, row.SourceRow, "C",
                    $"화자 '{row.Speaker}'에 공백이 있는데 등록된 화자가 아니라서, 대사와 " +
                    "합쳐져 지문이 됩니다. 화자 칸에는 이름만 적거나, 챕터 그래프의 [화자] " +
                    "탭에서 그 이름을 등록해 주세요."));
            }
        }
    }

    /// <summary>
    /// 반영 대상 대사노드. 이름의 원천은 챕터 `에피소드` 시트의 `대사엔트리`다 — 런타임이
    /// 재생할 엔트리와 툴의 노드가 같은 이름을 쓴다. 챕터에 없는 에피소드면 EpisodeId를 쓴다.
    ///
    /// ⚠ <b>그 챕터의 판 안에서만</b> 찾는다 — 다른 챕터에 같은 이름의 에피소드가 있을 때
    /// 그쪽 노드에 이 챕터의 대사가 쏟아지는 실사례가 있었다(2026-08-16).
    /// </summary>
    private static DialogueNode FindOrCreateNode(
        ProjectEditor editor, string fileId, string episodeId, ChapterGraphModel chapter)
    {
        string name = chapter.FindEpisode(episodeId)?.DialogueEntry is { Length: > 0 } entry
            ? entry
            : episodeId;

        if (editor.Project.FindFile(fileId)?.Nodes.OfType<DialogueNode>()
                .FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.Ordinal))
            is { } existing)
        {
            return existing;
        }

        DialogueNode created = editor.AddDialogueNode(fileId, name: name);

        // 노드 생성이 자동으로 채우는 첫 빈 줄을 은퇴시킨다. 워크북의 줄들은 전부 제 신원을
        // 실어 오는데 이 줄만 고아로 남아, 반영이 "이 빈 줄이 지워진 것인지 어느 줄로 고쳐진
        // 것인지"를 확신하지 못하고 통째 거부한다.
        foreach (ScriptLine line in editor.Project.FindScript(created.ScriptId)!
                     .ActiveLines.ToList())
        {
            editor.RetireScriptLine(created.ScriptId!, line.Id);
        }

        return created;
    }
}
