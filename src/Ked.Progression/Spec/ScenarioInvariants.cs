using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 시나리오 전체 룰
    internal static class ScenarioInvariants
    {
        public static void Collect(
            IReadOnlyList<ChapterProgression> chapters,
            string startChapterId,
            ICollection<ProgressionDiagnostic> into,
            out Dictionary<string, ChapterProgression> chaptersById)
        {
            chaptersById = IndexChapters(chapters, into);

            VerifyStart(startChapterId, chaptersById, into);
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
                return;

            into.Add(ProgressionDiagnostic.Error(
                "StartChapterId",
                string.IsNullOrEmpty(startChapterId)
                    ? "시작 챕터가 비어 있다. 시나리오를 시작할 자리가 없다."
                    : $"시작 챕터 '{startChapterId}'가 없다. 있는 것: {Join(chaptersById.Keys)}"));
        }

        private static string Join(IEnumerable<string> keys) => string.Join(", ", keys);
    }
}