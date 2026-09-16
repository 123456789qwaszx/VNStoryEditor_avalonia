using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// <b>자동 길의 규칙</b> — 다섯 가지 (V1 · 2026-09-16, <c>docs/plans/R6.md</c> §9).
///
/// 자동 진행(<see cref="ChapterEdge.Auto"/>)은 플레이어 입력 없이 <b>같은 장면의</b> 다음
/// 에피소드로 잇는 길이다. 그러려면 갈릴 데가 없어야 한다: 유일한 간선 · 조건 없음 ·
/// 스탯변화 없음 · 같은 장면 · 문구 없음.
///
/// ⛔ <b>왜 따로 떼어 냈나</b>: 이 다섯이 <see cref="ChapterWorkbookReader"/> 안에만 있었다.
/// R-F로 판의 주인이 워크북에서 프로젝트로 넘어가자 읽는 길이 사라졌고, 그와 함께
/// <b>툴이 소유한 챕터에서는 이 검사가 조용히 안 돌게 됐다</b>. 내보내기 관문은 코어가
/// 여전히 거부하므로 잘못된 것이 나가지는 않지만, 기획자는 <b>고치는 중에</b> 못 본다 —
/// 관문에서야 알면 어디를 고칠지 되짚어야 한다.
///
/// ⚠ <b>새 규칙을 만들지 않는다.</b> 다섯 가지는 코어의 <c>ChapterInvariants.VerifyAuto</c>와
/// 같은 것이고, 여기서 하는 일은 그것을 <b>더 일찍, 셀을 짚어</b> 말해 주는 것뿐이다.
///
/// ⚠ <see cref="ChapterDocument.StatDiagnostics"/>와 같은 부류다 — <b>파일의 흠이 아니라
/// 값의 흠</b>이라 주인이 바뀌어도 남는다. 다만 그쪽은 두 벌로 갈라져 있고 이쪽은 한 벌이다:
/// 검사가 두 자리에 있으면 언젠가 한쪽만 고쳐진다(V1이 정확히 그 사고였다).
/// </summary>
public static class ChapterAutoEdgeCheck
{
    /// <summary>
    /// 이 챕터의 자동 길들을 본다. 깨진 것이 없으면 빈 목록이다.
    /// </summary>
    /// <param name="path">진단이 사람에게 짚어 줄 파일 — 읽은 자리가 아니라 <b>낼</b> 자리다.</param>
    public static IReadOnlyList<ChapterDiagnostic> Of(
        IReadOnlyList<ChapterEpisode> episodes,
        IReadOnlyList<ChapterEdge> edges,
        string path)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(edges);

        var diagnostics = new List<ChapterDiagnostic>();

        // ⚠ Id가 겹친 챕터에서도 터지지 않아야 한다 — 그 흠은 다른 진단이 따로 짚는다.
        var episodeById = new Dictionary<string, ChapterEpisode>(StringComparer.Ordinal);

        foreach (ChapterEpisode episode in episodes)
        {
            episodeById[episode.EpisodeId] = episode;
        }

        var outgoing = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (ChapterEdge edge in edges)
        {
            outgoing[edge.FromEpisodeId] = outgoing.GetValueOrDefault(edge.FromEpisodeId) + 1;
        }

        foreach (ChapterEdge edge in edges.Where(item => item.Auto))
        {
            void Error(ChapterDiagnosticCode code, int column, string message) =>
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Error, code, path, ChapterSheetNames.Edges,
                    edge.SourceRow > 0 ? edge.SourceRow : null,
                    column > 0 ? XLHelper.GetColumnLetterFromNumber(column) : "?",
                    message));

            if (outgoing.GetValueOrDefault(edge.FromEpisodeId) != 1)
            {
                Error(ChapterDiagnosticCode.AutoEdgeHasSiblings, 8,
                    $"자동 길 '{edge.FromEpisodeId}'→'{edge.ToEpisodeId}'은 그 에피소드의 유일한 간선이어야 합니다.");
            }

            if (edge.HasGate)
            {
                Error(ChapterDiagnosticCode.AutoEdgeHasConditions, 8,
                    "자동 길에는 표시조건·해금조건을 둘 수 없습니다. 자동 진행은 언제나 같은 결과여야 합니다.");
            }

            if (edge.StatChanges.Count > 0)
            {
                Error(ChapterDiagnosticCode.AutoEdgeHasStatChanges, 8,
                    "자동 길에는 스탯변화를 둘 수 없습니다. 효과가 필요하면 일반 선택지로 바꾸세요.");
            }

            if (episodeById.TryGetValue(edge.FromEpisodeId, out ChapterEpisode? from) &&
                episodeById.TryGetValue(edge.ToEpisodeId, out ChapterEpisode? to) &&
                !string.Equals(from.EffectiveSceneId, to.EffectiveSceneId, StringComparison.Ordinal))
            {
                Error(ChapterDiagnosticCode.AutoEdgeCrossesScene, 8,
                    $"자동 길은 같은 장면 안에서만 이어집니다. 출발은 '{from.EffectiveSceneId}', 도착은 '{to.EffectiveSceneId}'입니다.");
            }

            if (!edge.HasNoOptionLabel)
            {
                Error(ChapterDiagnosticCode.AutoEdgeHasChoiceLabel, 4,
                    "자동 길의 선택지 문구는 비워야 합니다. 문구가 있으면 플레이어 선택과 자동 진행의 뜻이 충돌합니다.");
            }
        }

        return diagnostics;
    }
}
