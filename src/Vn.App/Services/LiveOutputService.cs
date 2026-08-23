using Avalonia.Threading;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.App.Services;

/// <summary>
/// 라이브 CompositionNode 출력 (X12c, D-1) — 편집이 멈추면 지정 폴더에 정식 산출물을
/// 다시 쓴다. 합성은 <see cref="LiveNodeComposer"/> 하나(발행 Freeze + 기존 이미터)라
/// 수동 내보내기와 바이트가 같다.
///
/// 키 입력마다 디스크를 두드리지 않도록 짧게 디바운스한다. 출력 폴더가 없으면
/// 아무것도 하지 않고(편의가 저작을 막지 않는다), 쓰기 실패·막는 문제는 조용히
/// 넘어가지 않고 상태줄로 알린다(불변식 4·5).
/// </summary>
internal sealed class LiveOutputService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(600);

    private readonly AuthoringSession _session;
    private readonly DispatcherTimer _timer;
    private string? _lastReport;

    /// <summary>
    /// 직전 저장에서 발견한 낡은 산출 파일. 지우지 않고 보여 주기만 한다(K1 ②안) —
    /// 목록 화면은 [양식…]이 읽는다.
    /// </summary>
    public OrphanOutputScan Orphans { get; private set; } = OrphanOutputScan.Empty;

    /// <summary>
    /// 직전 쓰기의 산출물 컴파일 판정 (2026-08-23). 상태줄은 실패만 말하므로, 화면이
    /// "몇 개를 검사해서 통과했나"를 보이고 싶으면 이 값을 읽는다.
    /// </summary>
    public YarnOutputVerdict LastVerdict { get; private set; } = YarnOutputVerdict.Skipped;

    public LiveOutputService(AuthoringSession session)
    {
        _session = session;
        _timer = new DispatcherTimer { Interval = Debounce };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            WriteAll();
        };

        session.Changed += (_, e) =>
        {
            // 좌표 이동은 산출물을 바꾸지 않는다. 상태줄 알림(Content)로 재무장하면
            // 쓰기 실패 알림이 다시 쓰기를 부르는 되먹임이 생기므로 그것도 제외한다.
            if (e.Kind is ProjectChangeKind.Layout or ProjectChangeKind.Content)
            {
                return;
            }

            _timer.Stop();
            _timer.Start();
        };
    }

    /// <summary>프로젝트를 새로 열었을 때 등 즉시 한 번 쓰고 싶을 때.</summary>
    public void WriteNow()
    {
        _timer.Stop();
        WriteAll();
    }

    private void WriteAll()
    {
        try
        {
            if (AssetRootSettings.ResolveFrom(
                    _session.ProjectPath ?? Path.Combine(Environment.CurrentDirectory, "unsaved.vnproject.json"),
                    _session.Project.OutputPath) is not { } directory)
            {
                // 출력 폴더 미지정 — 라이브 출력 없음. 볼 폴더가 없으니 고아 목록도 없다.
                Orphans = OrphanOutputScan.Empty;
                return;
            }

            var bundles = new List<YarnBundle>();
            var blocked = new List<string>();

            foreach (DialogueNode node in _session.Project.EnumerateNodes().OfType<DialogueNode>())
            {
                LiveComposition composition = LiveNodeComposer.Compose(
                    _session.Project,
                    node.Id,
                    _session.Definition,
                    DateTimeOffset.UtcNow);

                if (composition.CanWrite)
                {
                    bundles.Add(composition.Bundle!);
                }
                else if (composition.BlockingProblems.Count > 0)
                {
                    blocked.Add($"{composition.DialogueNodeName}: {composition.BlockingProblems[0]}");
                }
            }

            var written = new List<string>(bundles.Count > 0
                ? YarnBundleEmitter.WriteBundles(bundles, directory)
                : []);

            // 커스텀 곡선(W67 후속) — 라이브 출력도 정식 내보내기와 같은 동반 파일을 낸다.
            if (bundles.Count > 0 &&
                YarnBundleEmitter.WriteCurves(_session.Project.EaseCurves, directory) is { } curvesPath)
            {
                written.Add(curvesPath);
            }

            // 낡은 파일 판정은 이번에 쓴 것이 아니라 "지금 프로젝트가 만들 수 있는 것"과
            // 견준다. 막혀서 못 쓴 노드의 파일까지 고아로 몰지 않기 위해서다.
            Orphans = OutputManifest.Scan(
                directory,
                OutputManifest.ExpectedFileNames(_session.Project));

            if (written.Count > 0)
            {
                OutputManifest.Record(directory, written);
            }

            // 방금 쓴 것이 실제로 컴파일되는가 (2026-08-23). 여기가 아니면 아무도 안 본다 —
            // 종전에는 이 검사가 테스트에만 있어서, 컴파일 안 되는 대본이 유니티까지 갔다.
            // ⚠ 실패해도 쓰기를 되돌리지 않는다. 자세한 이유는 YarnOutputVerification 참조.
            LastVerdict = YarnOutputVerification.Verify(written);

            var parts = new List<string>();

            if (blocked.Count > 0)
            {
                parts.Add($"라이브 출력에서 제외된 노드: {string.Join(" / ", blocked)}");
            }

            if (YarnOutputVerification.ReportOf(LastVerdict) is { } compileReport)
            {
                parts.Add(compileReport);
            }

            if (OrphanReport(Orphans) is { } orphanReport)
            {
                parts.Add(orphanReport);
            }

            Report(string.Join("  ·  ", parts));
        }
        catch (Exception exception)
        {
            Report($"라이브 출력에 실패했습니다: {exception.Message}");
        }
    }

    /// <summary>같은 알림을 키 입력마다 반복하지 않는다 — 내용이 바뀔 때만 상태줄에 올린다.</summary>
    private void Report(string message)
    {
        if (string.Equals(_lastReport, message, StringComparison.Ordinal))
        {
            return;
        }

        _lastReport = message;

        if (message.Length > 0)
        {
            _session.SetStatus(message);
        }
    }

    /// <summary>
    /// 고아 목록을 한 줄로. 전부 나열하면 상태줄을 덮으므로 앞의 셋만 적고
    /// 나머지는 [양식…] 목록으로 넘긴다 — 숨기는 것이 아니라 자리를 옮기는 것이다.
    /// </summary>
    internal static string? OrphanReport(OrphanOutputScan scan)
    {
        if (scan.Orphans.Count == 0)
        {
            return scan.Note;
        }

        const int shown = 3;
        string names = string.Join(", ", scan.Orphans.Take(shown).Select(orphan => orphan.FileName));

        if (scan.Orphans.Count > shown)
        {
            names += $" 외 {scan.Orphans.Count - shown}개";
        }

        string message =
            $"출력 폴더에 낡은 산출 파일 {scan.Orphans.Count}개가 남아 있습니다({names}). " +
            "유니티가 폴더를 통째로 읽으면 없어진 노드의 옛 대사가 재생될 수 있습니다 — " +
            "[양식…]에서 목록을 보고 직접 지우세요.";

        return scan.Note is null ? message : $"{message} {scan.Note}";
    }
}
