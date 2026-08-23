using System;
using System.Collections.Generic;
using Ked.Progression.Dto;

namespace Ked.Progression
{
    /// <summary>
    /// 세이브를 되살린 결과. <b>오류가 있으면 상태를 내지 않는다</b> —
    /// 반쯤 되살린 진행으로 게임을 시작하면 무엇이 어긋났는지 플레이해 봐야 안다.
    /// 경고만 있으면 상태가 나온다(값이 조정됐을 뿐 이어할 수 있다).
    /// </summary>
    public sealed class ProgressionRestoreResult
    {
        public ProgressionState State { get; }
        public IReadOnlyList<ProgressionDiagnostic> Diagnostics { get; }

        public ProgressionRestoreResult(
            ProgressionState state, IReadOnlyList<ProgressionDiagnostic> diagnostics)
        {
            State = state;
            Diagnostics = diagnostics ?? Array.Empty<ProgressionDiagnostic>();
        }

        public bool IsValid => State != null;

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Diagnostics.Count; i++)
                {
                    if (Diagnostics[i].Severity == ProgressionDiagnosticSeverity.Error)
                        return true;
                }

                return false;
            }
        }
    }

    /// <summary>
    /// 진행 상태를 세이브 블록으로 굽고 되살린다.
    ///
    /// <b>어려운 것은 직렬화가 아니다.</b> 세이브는 <b>어제 만든 콘텐츠</b>로 저장되고
    /// <b>오늘 고친 콘텐츠</b>로 열린다 — 그 사이에 스탯이 생기고, 사라지고, 경계가 바뀌고,
    /// 에피소드가 지워진다. 그때 무엇을 조용히 해도 되고 무엇을 말해야 하는지가 이 타입의 내용이다.
    ///
    /// <b>조용해도 되는 것은 하나뿐이다</b> — 새로 생긴 스탯을 정의의 초기값으로 채우는 것.
    /// 나머지는 전부 진단이 붙는다.
    /// </summary>
    public static class ProgressionSave
    {
        /// <summary>
        /// 지금 모양의 번호. <b>필드가 사라지거나 뜻이 바뀔 때만</b> 올린다 —
        /// 추가는 올리지 않는다(없는 값은 정의의 초기값이 메운다).
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// 상태를 세이브 블록으로 굽는다.
        ///
        /// 시나리오를 <b>객체로</b> 받는 이유가 있다 — ID를 문자열로 받으면 엉뚱한 시나리오
        /// 이름이 붙은 세이브가 조용히 만들어진다. 여기서 받으면 그럴 수가 없다.
        /// </summary>
        public static ProgressionSaveDto Capture(
            ScenarioProgression scenario, ProgressionState state)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));

            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var endings = new List<ChapterEndingDto>();

            for (int i = 0; i < state.EndingHistory.Count; i++)
            {
                endings.Add(new ChapterEndingDto
                {
                    ChapterId = state.EndingHistory[i].ChapterId,
                    EndingKey = state.EndingHistory[i].EndingKey,
                });
            }

            return new ProgressionSaveDto
            {
                SchemaVersion = CurrentSchemaVersion,
                ScenarioId = scenario.ScenarioId,
                CurrentChapterId = state.CurrentChapterId,
                CurrentEpisodeId = state.CurrentEpisodeId,
                Stats = new Dictionary<string, int>(state.Stats, StringComparer.Ordinal),
                ClearedEpisodeIds = new List<string>(state.ClearedEpisodeIds),
                ClearedChapterIds = new List<string>(state.ClearedChapterIds),
                EndingHistory = endings,
            };
        }

        /// <summary>
        /// 세이브 블록을 지금 콘텐츠로 되살린다. 규칙은 아래 표 그대로다.
        ///
        /// <list type="table">
        /// <item>세이브에 없는 스탯이 정의에 생김 → <b>초기값으로 채운다(조용히)</b></item>
        /// <item>정의에 없는 스탯이 세이브에 있음 → 버린다 + 경고</item>
        /// <item>값이 새 경계 밖 → clamp + 경고</item>
        /// <item>클리어 목록에 없는 에피소드·챕터 → 버린다 + 경고 (관문이 다시 잠길 수 있다)</item>
        /// <item>지금 챕터·에피소드가 사라짐 → <b>오류.</b> 조용히 시작점으로 보내지 않는다</item>
        /// <item>시나리오가 다름 → <b>오류</b></item>
        /// <item><see cref="CurrentSchemaVersion"/>보다 높은 세이브 → <b>오류.</b> 미래를 추측하지 않는다</item>
        /// </list>
        /// </summary>
        public static ProgressionRestoreResult Restore(
            ScenarioProgression scenario, ProgressionSaveDto save)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));

            var diagnostics = new List<ProgressionDiagnostic>();

            if (save == null)
            {
                diagnostics.Add(ProgressionDiagnostic.Error(
                    string.Empty, "세이브가 null이다. 역직렬화가 실패한 것은 아닌지 확인할 것."));

                return new ProgressionRestoreResult(null, diagnostics);
            }

            VerifyIdentity(scenario, save, diagnostics);

            Dictionary<string, int> stats = RestoreStats(scenario, save, diagnostics);

            HashSet<string> clearedEpisodes = RestoreCleared(
                save.ClearedEpisodeIds, "ClearedEpisodeIds", diagnostics,
                id => KnowsEpisode(scenario, id));

            HashSet<string> clearedChapters = RestoreCleared(
                save.ClearedChapterIds, "ClearedChapterIds", diagnostics,
                id => scenario.TryGetChapter(id, out _));

            List<ChapterEnding> endings = RestoreEndings(scenario, save, diagnostics);

            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == ProgressionDiagnosticSeverity.Error)
                {
                    return new ProgressionRestoreResult(null, diagnostics);
                }
            }

            ProgressionState state = ProgressionState.FromSave(
                save.CurrentChapterId, save.CurrentEpisodeId,
                stats, clearedEpisodes, clearedChapters, endings);

            return new ProgressionRestoreResult(state, diagnostics);
        }

        // ── 신원 ────────────────────────────────────────────────────────────

        private static void VerifyIdentity(
            ScenarioProgression scenario, ProgressionSaveDto save,
            List<ProgressionDiagnostic> diagnostics)
        {
            if (save.SchemaVersion > CurrentSchemaVersion)
            {
                diagnostics.Add(ProgressionDiagnostic.Error(
                    "SchemaVersion",
                    $"세이브가 더 새 모양이다(세이브 {save.SchemaVersion} > 지금 " +
                    $"{CurrentSchemaVersion}). 미래 세이브를 추측해 읽지 않는다."));
            }

            if (!string.Equals(save.ScenarioId, scenario.ScenarioId, StringComparison.Ordinal))
            {
                diagnostics.Add(ProgressionDiagnostic.Error(
                    "ScenarioId",
                    $"세이브의 시나리오 '{save.ScenarioId}'가 지금 시나리오 " +
                    $"'{scenario.ScenarioId}'와 다르다."));
            }

            if (!scenario.TryGetChapter(save.CurrentChapterId, out ChapterProgression chapter))
            {
                diagnostics.Add(ProgressionDiagnostic.Error(
                    "CurrentChapterId",
                    $"세이브가 가리키는 챕터 '{save.CurrentChapterId}'가 지금 시나리오에 없다. " +
                    "조용히 처음으로 보내지 않는다 — 플레이어가 어디까지 왔는지가 사라진다."));

                return;
            }

            if (!chapter.TryGetNode(save.CurrentEpisodeId, out _))
            {
                diagnostics.Add(ProgressionDiagnostic.Error(
                    "CurrentEpisodeId",
                    $"세이브가 가리키는 에피소드 '{save.CurrentEpisodeId}'가 챕터 " +
                    $"'{chapter.ChapterId}'에 없다. 조용히 챕터 처음으로 보내지 않는다."));
            }
        }

        // ── 스탯 ────────────────────────────────────────────────────────────

        private static Dictionary<string, int> RestoreStats(
            ScenarioProgression scenario, ProgressionSaveDto save,
            List<ProgressionDiagnostic> diagnostics)
        {
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> saved = save.Stats;

            // ① 정의를 기준으로 세운다 — 세이브에 없는 것은 초기값이 메운다.
            //    **조용해도 되는 유일한 경우다.** 스탯이 새로 생기는 것은 콘텐츠가 자라는
            //    정상 경로이고, 그때 옛 세이브를 못 열게 하면 개발이 멈춘다.
            for (int i = 0; i < scenario.Stats.Count; i++)
            {
                StatDefinition definition = scenario.Stats[i];

                int value;

                if (saved == null || !saved.TryGetValue(definition.Key, out value))
                {
                    values[definition.Key] = definition.Initial;
                    continue;
                }

                int clamped = definition.Clamp(value);

                if (clamped != value)
                {
                    // 조용히 자르면 작가가 정한 경계와 플레이어가 겪은 값이 갈린다.
                    diagnostics.Add(ProgressionDiagnostic.Warning(
                        $"Stats[{definition.Key}]",
                        $"저장된 값 {value}가 지금 경계 [{definition.Minimum}, " +
                        $"{definition.Maximum}] 밖이라 {clamped}로 잘렸다."));
                }

                values[definition.Key] = clamped;
            }

            // ② 정의에 없는데 세이브에 있는 것 — 버리되 말한다.
            if (saved != null)
            {
                foreach (KeyValuePair<string, int> pair in saved)
                {
                    if (!scenario.StatsByKey.ContainsKey(pair.Key))
                    {
                        diagnostics.Add(ProgressionDiagnostic.Warning(
                            $"Stats[{pair.Key}]",
                            $"세이브의 스탯 '{pair.Key}'가 지금 시나리오에 정의되어 있지 않아 버린다."));
                    }
                }
            }

            return values;
        }

        // ── 클리어 · 엔딩 이력 ──────────────────────────────────────────────

        private static HashSet<string> RestoreCleared(
            List<string> saved,
            string where,
            List<ProgressionDiagnostic> diagnostics,
            Func<string, bool> known)
        {
            var kept = new HashSet<string>(StringComparer.Ordinal);

            if (saved == null)
            {
                return kept;
            }

            for (int i = 0; i < saved.Count; i++)
            {
                if (known(saved[i]))
                {
                    kept.Add(saved[i]);
                    continue;
                }

                // 콘텐츠에서 지워진 것이다. 조용히 버리면 그것을 보던 관문이 다시 잠긴다 —
                // 플레이어에게는 "열려 있던 길이 닫힌" 것으로 보인다.
                diagnostics.Add(ProgressionDiagnostic.Warning(
                    $"{where}[{i}]",
                    $"'{saved[i]}'가 지금 콘텐츠에 없어 클리어 기록에서 버린다. " +
                    "이것을 보던 관문이 있었다면 다시 잠긴다."));
            }

            return kept;
        }

        private static List<ChapterEnding> RestoreEndings(
            ScenarioProgression scenario, ProgressionSaveDto save,
            List<ProgressionDiagnostic> diagnostics)
        {
            var endings = new List<ChapterEnding>();

            if (save.EndingHistory == null)
            {
                return endings;
            }

            for (int i = 0; i < save.EndingHistory.Count; i++)
            {
                ChapterEndingDto entry = save.EndingHistory[i];

                if (entry == null)
                {
                    continue;
                }

                if (!scenario.TryGetChapter(entry.ChapterId, out _))
                {
                    diagnostics.Add(ProgressionDiagnostic.Warning(
                        $"EndingHistory[{i}]",
                        $"챕터 '{entry.ChapterId}'가 지금 시나리오에 없어 엔딩 이력에서 버린다."));

                    continue;
                }

                endings.Add(new ChapterEnding(entry.ChapterId, entry.EndingKey));
            }

            return endings;
        }

        private static bool KnowsEpisode(ScenarioProgression scenario, string episodeId)
        {
            for (int i = 0; i < scenario.Chapters.Count; i++)
            {
                if (scenario.Chapters[i].TryGetNode(episodeId, out _))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
