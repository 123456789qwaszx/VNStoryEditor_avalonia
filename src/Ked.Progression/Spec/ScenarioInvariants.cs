using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 시나리오 전체 룰
    internal static class ScenarioInvariants
    {
        public static void Collect(
            IReadOnlyList<StatDefinition> stats,
            IReadOnlyList<ChapterProgression> chapters,
            string startChapterId,
            ICollection<ProgressionDiagnostic> into,
            out Dictionary<string, StatDefinition> statsByKey,
            out Dictionary<string, ChapterProgression> chaptersById)
        {
            statsByKey = IndexStats(stats, into);
            chaptersById = IndexChapters(chapters, into);

            VerifyStart(startChapterId, chaptersById, into);
            VerifyChapterStats(chapters, statsByKey, into);
            VerifyChapterEdges(chapters, chaptersById, into);
        }

        private static Dictionary<string, StatDefinition> IndexStats(
            IReadOnlyList<StatDefinition> stats, ICollection<ProgressionDiagnostic> into)
        {
            var byKey = new Dictionary<string, StatDefinition>(StringComparer.Ordinal);

            for (int i = 0; i < stats.Count; i++)
            {
                StatDefinition stat = stats[i];

                if (stat == null)
                {
                    into.Add(ProgressionDiagnostic.Error($"Stats[{i}]", "스탯 정의가 null이다."));
                    continue;
                }

                if (byKey.ContainsKey(stat.Key))
                {
                    into.Add(ProgressionDiagnostic.Error(
                        $"Stats[{i}]", $"스탯 키 '{stat.Key}'가 중복 정의됐다."));
                    continue;
                }

                byKey[stat.Key] = stat;
            }

            return byKey;
        }

        private static Dictionary<string, ChapterProgression> IndexChapters(
            IReadOnlyList<ChapterProgression> chapters, ICollection<ProgressionDiagnostic> into)
        {
            var byId = new Dictionary<string, ChapterProgression>(StringComparer.Ordinal);

            for (int i = 0; i < chapters.Count; i++)
            {
                ChapterProgression chapter = chapters[i];

                if (chapter == null)
                {
                    into.Add(ProgressionDiagnostic.Error($"Chapters[{i}]", "챕터가 null이다."));
                    continue;
                }

                if (byId.ContainsKey(chapter.ChapterId))
                {
                    into.Add(ProgressionDiagnostic.Error(
                        $"Chapters[{i}]", $"챕터 ID '{chapter.ChapterId}'가 중복이다."));
                    continue;
                }

                byId[chapter.ChapterId] = chapter;
            }

            return byId;
        }

        private static void VerifyStart(
            string startChapterId,
            Dictionary<string, ChapterProgression> chaptersById,
            ICollection<ProgressionDiagnostic> into)
        {
            if (!string.IsNullOrEmpty(startChapterId) && chaptersById.ContainsKey(startChapterId))
            {
                return;
            }

            into.Add(ProgressionDiagnostic.Error(
                "StartChapterId",
                string.IsNullOrEmpty(startChapterId)
                    ? "시작 챕터가 비어 있다. 시나리오를 시작할 자리가 없다."
                    : $"시작 챕터 '{startChapterId}'가 없다. 있는 것: {Join(chaptersById.Keys)}"));
        }

        // 경계와 타입은 갈리면 안 되고, 초기값은 챕터별로 달라도 됨.
        private static void VerifyChapterStats(
            IReadOnlyList<ChapterProgression> chapters,
            Dictionary<string, StatDefinition> statsByKey,
            ICollection<ProgressionDiagnostic> into)
        {
            foreach (ChapterProgression chapter in chapters)
            {
                if (chapter == null)
                {
                    continue;
                }

                for (int i = 0; i < chapter.Stats.Count; i++)
                {
                    StatDefinition mine = chapter.Stats[i];
                    string at = $"Chapters[{chapter.ChapterId}].Stats[{i}]";

                    if (!statsByKey.TryGetValue(mine.Key, out StatDefinition owner))
                    {
                        into.Add(ProgressionDiagnostic.Error(
                            at,
                            $"스탯 '{mine.Key}'가 시나리오에 정의되어 있지 않다. " +
                            $"정의된 것: {Join(statsByKey.Keys)}"));
                        continue;
                    }

                    if (mine.Type != owner.Type)
                    {
                        into.Add(ProgressionDiagnostic.Error(
                            at,
                            $"스탯 '{mine.Key}'의 타입이 시나리오와 다르다: " +
                            $"챕터 {mine.Type} / 시나리오 {owner.Type}."));
                    }

                    if (mine.Minimum != owner.Minimum || mine.Maximum != owner.Maximum)
                    {
                        // 경계가 갈리면 증명이 걷는 상태공간과 실제 플레이가 갈림.
                        into.Add(ProgressionDiagnostic.Error(
                            at,
                            $"스탯 '{mine.Key}'의 경계가 시나리오와 다르다: " +
                            $"챕터 [{mine.Minimum}, {mine.Maximum}] / " +
                            $"시나리오 [{owner.Minimum}, {owner.Maximum}]. " +
                            "초기값은 챕터마다 달라도 되지만(증명 진입 가정) 경계는 아니다."));
                    }
                }
            }
        }

        private static void VerifyChapterEdges(
            IReadOnlyList<ChapterProgression> chapters,
            Dictionary<string, ChapterProgression> chaptersById,
            ICollection<ProgressionDiagnostic> into)
        {
            foreach (ChapterProgression chapter in chapters)
            {
                if (chapter == null)
                {
                    continue;
                }

                IReadOnlyList<EndingRule> rules = chapter.EndingRules;

                for (int i = 0; i < rules.Count; i++)
                {
                    EndingRule rule = rules[i];
                    string at = $"Chapters[{chapter.ChapterId}].EndingRules[{i}]";

                    if (rule.Outcome == EndingOutcome.NextChapter &&
                        !chaptersById.ContainsKey(rule.NextChapterId))
                    {
                        into.Add(ProgressionDiagnostic.Error(
                            at,
                            $"다음 챕터 '{rule.NextChapterId}'가 시나리오에 없다. " +
                            $"있는 것: {Join(chaptersById.Keys)}"));
                    }

                    VerifyChapterClearedTargets(rule.Conditions, chaptersById, at + ".Conditions", into);
                }

                foreach (EpisodeNode node in chapter.Nodes)
                {
                    IReadOnlyList<EpisodeOption> options = node.NextOptions;

                    for (int i = 0; i < options.Count; i++)
                    {
                        string at =
                            $"Chapters[{chapter.ChapterId}].Nodes[{node.EpisodeId}].NextOptions[{i}]";

                        VerifyChapterClearedTargets(
                            options[i].VisibleConditions, chaptersById, at + ".VisibleConditions", into);
                        VerifyChapterClearedTargets(
                            options[i].Conditions, chaptersById, at + ".Conditions", into);
                    }
                }
            }
        }

        // ChapterCleared의 대상이 실재하는지 체크.
        private static void VerifyChapterClearedTargets(
            IReadOnlyList<ProgressionCondition> conditions,
            Dictionary<string, ChapterProgression> chaptersById,
            string where,
            ICollection<ProgressionDiagnostic> into)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                ProgressionCondition condition = conditions[i];

                if (condition.Kind != ConditionKind.ChapterCleared)
                {
                    continue;
                }

                if (chaptersById.ContainsKey(condition.Key))
                {
                    continue;
                }

                into.Add(ProgressionDiagnostic.Error(
                    $"{where}[{i}]",
                    $"챕터 '{condition.Key}'가 시나리오에 없다. 있는 것: {Join(chaptersById.Keys)}"));
            }
        }

        private static string Join(IEnumerable<string> keys) => string.Join(", ", keys);
    }
}