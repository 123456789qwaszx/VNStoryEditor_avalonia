using System.Collections.Generic;

namespace Ked.Progression
{
    public sealed class EpisodeNodeDto
    {
        public string EpisodeId { get; set; }
        public string DialogueEntryId { get; set; }
        
        public string Title { get; set; }

        // "이 에피소드를 다 시청했을 때"의 이벤트·보상 트리거.
        // (툴은 해석하지 않음.)
        public string EventKey { get; set; }

        // 이 에피소드가 속한 장면. 비어 있으면 에피소드마다 고유 장면으로 읽는다 —
        // 장면 칸이 서기 전에 나간 JSON이 그대로 실려야 한다.
        public string SceneId { get; set; }

        public List<EpisodeOptionDto> NextOptions { get; set; }
    }
}