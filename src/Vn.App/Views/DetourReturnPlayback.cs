using Vn.App.Services;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>
/// detour에서 돌아오는 규칙 하나 (2026-08-22 소유자 보고: "detour로 중간에 다른 노드로
/// 넘어가는 건 구현이 되는데, 그걸 끝까지 재생한 뒤에 원래 노드로 돌아오지가 않네").
///
/// 계약 §A2-1에서 조건 갈래의 커스텀 씬 출구는 <c>&lt;&lt;jump&gt;&gt;</c>가 아니라
/// <c>&lt;&lt;detour&gt;&gt;</c>다 — <b>씬을 재생하고 갈래로 돌아와 나머지 대본을 계속한다</b>.
/// 프리뷰 재생은 나가는 절반만 하고 있었다.
///
/// <b>두 편집기가 같은 것을 부른다</b>(사본 금지): 나가는 노드는 연출 편집기일 때가 많고
/// detour 대상(커스텀 씬)은 대사 편집기로 열리므로, 돌아오는 쪽은 둘 중 어느 쪽이든 될 수
/// 있다. 어느 편집기가 라인을 짚을지는 셸이 정한다
/// (<see cref="MiniStagePreview.DetourResumeRequested"/>) — 그때 활성인 편집기 하나다.
/// </summary>
internal static class DetourReturnPlayback
{
    /// <summary>
    /// 경로 끝에서 노드를 나가는 <b>단 하나의 길</b>. 셋 중 하나가 일어난다:
    ///
    /// - 나갈 곳이 없다 → 쌓아 둔 detour 복귀를 태운다(없으면 false, 재생이 끝난다)
    /// - 갈래 출구(detour) → <b>돌아올 자리를 쌓고</b> 그 노드로 나간다
    /// - 기본 출구(jump) → 그냥 나간다. 돌아오지 않는다
    ///
    /// 부르는 자리가 둘이다 — 경로가 노드 끝에 닿았을 때(<c>NodeExitRequested</c>)와
    /// 경로가 갈래에서 끊겼을 때(<c>TryMoveAlongPath</c>). 둘이 다른 규칙을 쓰면
    /// 같은 detour가 한쪽에서만 돌아온다.
    /// </summary>
    public static bool Exit(
        AuthoringSession? session,
        MiniStagePreview? preview,
        string? nodeId,
        Vn.Authoring.Flow.PlaybackPath.Result path,
        Func<string?, bool> enterNext)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(enterNext);

        if (path.ExitTargetNodeId is null)
        {
            return TryReturn(session, preview);
        }

        if (path.ExitViaBranch &&
            preview is not null &&
            nodeId is not null &&
            path.LineIds.Count > 0 &&
            path.ExitOwnerLineId is { } owner)
        {
            // 돌아올 자리는 <b>지금 이 노드</b>와 <b>떠나는 줄</b>이다.
            preview.Playback.PushDetour(
                new StagePlayback.DetourReturn(nodeId, path.LineIds[^1], owner));
        }

        return enterNext(path.ExitTargetNodeId);
    }

    /// <summary>
    /// 쌓아 둔 복귀 하나를 태운다 — 그 노드로 돌아가 <b>떠난 줄 다음</b>부터 잇는다.
    /// 돌아갈 자리가 없으면 false, 곧 예전처럼 재생이 여기서 끝난다(기본 출구 = jump는
    /// 애초에 돌아오지 않는다).
    ///
    /// 다음 줄을 직접 짚지 않고 편집기의 이동에 맡기는 이유: 다녀온 detour를 뺀 나머지
    /// 경로는 <c>PlaybackPath.Trace</c>가 다시 계산한다(<c>PopDetour</c>가 그 출구를
    /// "다녀왔음"으로 표시했다). 경로 계산이 두 벌이 되지 않는다.
    /// </summary>
    public static bool TryReturn(AuthoringSession? session, MiniStagePreview? preview)
    {
        if (session is null || preview is null)
        {
            return false;
        }

        if (preview.Playback.PopDetour() is not { } ret ||
            session.Project.FindNode(ret.NodeId) is not (DialogueNode or PresentationNode))
        {
            return false;
        }

        preview.Playback.OnNodeSwitch();
        session.Select(ret.NodeId);
        preview.RequestDetourResume(ret.ResumeAfterLineId);
        return true;
    }
}
