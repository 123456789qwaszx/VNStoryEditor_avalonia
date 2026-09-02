using System.Collections.Generic;

namespace Ked.Progression
{
    // 저작 쪽 `ChapterProgressionExporter`가 내는 JSON과 필드 1:1.
    public sealed class ChapterProgressionDto
    {
        public string ChapterId { get; set; }
        public string DisplayName { get; set; }
        public string StartEpisodeId { get; set; }

        public List<StatDto> Stats { get; set; }

        public List<EpisodeNodeDto> Nodes { get; set; }
    }
}