using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 챕터들을 묶는 자리.
    // [1] 영구 계층을 다루지 않기에 사실상 껍데기.
    //
    // 챕터 ID가 유일한가, 시작 챕터가 실재하는가, 엔딩이 가리키는 다음 챕터가 실재하는가.
    public sealed class ScenarioProgression
    {
        private readonly Dictionary<string, ChapterProgression> _chaptersById;

        public string ScenarioId { get; }
        public string DisplayName { get; }

        // 새 게임이 시작하는 챕터.
        public string StartChapterId { get; }

        public IReadOnlyList<ChapterProgression> Chapters { get; }

        public ScenarioProgression(
            string scenarioId,
            string displayName,
            string startChapterId,
            IReadOnlyList<ChapterProgression> chapters)
        {
            if (string.IsNullOrEmpty(scenarioId))
                throw new ArgumentException("시나리오 ID가 비어 있다.", nameof(scenarioId));

            ScenarioId = scenarioId;
            DisplayName = displayName ?? string.Empty;
            StartChapterId = startChapterId ?? string.Empty;
            Chapters = chapters ?? Array.Empty<ChapterProgression>();

            var diagnostics = new List<ProgressionDiagnostic>();

            ScenarioInvariants.Collect(
                Chapters, StartChapterId, diagnostics, out _chaptersById);

            if (diagnostics.Count > 0)
                throw new ArgumentException(diagnostics[0].ToString());
        }

        public bool TryGetChapter(string chapterId, out ChapterProgression chapter)
        {
            if (chapterId == null)
            {
                chapter = null;
                return false;
            }

            return _chaptersById.TryGetValue(chapterId, out chapter);
        }

        // 시작 챕터. 생성자에서 존재 보장.
        public ChapterProgression StartChapter => _chaptersById[StartChapterId];

        public override string ToString() => $"{ScenarioId}(챕터 {Chapters.Count})";
    }
}