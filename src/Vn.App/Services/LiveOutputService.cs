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
    private string? _lastFailure;

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
                return; // 출력 폴더 미지정 — 라이브 출력 없음.
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

            if (bundles.Count > 0)
            {
                YarnBundleEmitter.WriteBundles(bundles, directory);
            }

            // 같은 실패를 키 입력마다 반복해서 알리지 않는다 — 바뀔 때만.
            string failure = string.Join(" / ", blocked);

            if (!string.Equals(_lastFailure, failure, StringComparison.Ordinal))
            {
                _lastFailure = failure;

                if (failure.Length > 0)
                {
                    _session.SetStatus($"라이브 출력에서 제외된 노드: {failure}");
                }
            }
        }
        catch (Exception exception)
        {
            string failure = exception.Message;

            if (!string.Equals(_lastFailure, failure, StringComparison.Ordinal))
            {
                _lastFailure = failure;
                _session.SetStatus($"라이브 출력에 실패했습니다: {failure}");
            }
        }
    }
}
