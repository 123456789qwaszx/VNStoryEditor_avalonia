using Vn.Authoring.Definition;
using Vn.Authoring.Results;

namespace Vn.App.Services;

/// <summary>
/// 프리뷰 재생이 라인에 도달했을 때 그 라인의 오디오 커맨드를 실제 소리로 옮긴다 (W62).
///
/// 해석은 기본 게임 정의의 네 커맨드 id 규약(bgm·sfx·stop_bgm·stop_all_sfx)이다 —
/// 다른 id의 audio 카테고리 커맨드는 ♪ 칩(W34-b)으로만 남고, 소리를 못 낸다는 사실을
/// 상태줄로 알린다 (규칙 14). 표시 편의이지 결과 해석 규칙이 아닌 점은 칩과 같다.
/// </summary>
internal static class AudioCueRouter
{
    public static void Fire(AuthoringSession session, IReadOnlyList<PresentationResultCommand> commands)
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(session.Definition);

        foreach (PresentationResultCommand command in commands)
        {
            if (!string.Equals(
                catalog.Find(command.DefinitionId)?.CategoryId, "audio", StringComparison.Ordinal))
            {
                continue;
            }

            switch (command.DefinitionId)
            {
                case "bgm":
                    PlayClip(session, bgm: true, command);
                    break;

                case "sfx":
                    PlayClip(session, bgm: false, command);
                    break;

                case "stop_bgm":
                    AudioPreview.StopBgm();
                    break;

                case "stop_all_sfx":
                    AudioPreview.StopOneShots();
                    break;

                default:
                    AudioPreview.Problem?.Invoke(
                        $"오디오 커맨드 '{command.DefinitionId}'는 툴 미리 듣기가 소리 내지 못합니다 — ♪ 칩으로만 표시됩니다.");
                    break;
            }
        }
    }

    private static void PlayClip(AuthoringSession session, bool bgm, PresentationResultCommand command)
    {
        string kindFolder = bgm ? "assets/bgm" : "assets/sfx";

        if (!command.Arguments.TryGetValue("clipKey", out string? clipKey) ||
            string.IsNullOrWhiteSpace(clipKey))
        {
            AudioPreview.Problem?.Invoke($"'{command.DefinitionId}' 커맨드에 clipKey가 비어 있습니다.");
            return;
        }

        string? path = session.ResolveAudioClipPath(bgm ? session.BgmRoot : session.SfxRoot, clipKey);

        if (path is null)
        {
            AudioPreview.Problem?.Invoke($"clipKey '{clipKey}' 파일을 {kindFolder}에서 찾지 못했습니다.");
            return;
        }

        if (bgm)
        {
            AudioPreview.PlayBgm(path);
        }
        else
        {
            AudioPreview.PlayOneShot(path);
        }
    }
}
