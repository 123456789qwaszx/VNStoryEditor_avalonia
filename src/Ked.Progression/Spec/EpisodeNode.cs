using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    public sealed class EpisodeNode
    {
        public string EpisodeId { get; }
        public string Title { get; }
        public string DialogueEntryId { get; } // 호스트가 재생할 대본 키.
        public IReadOnlyList<EpisodeOption> NextOptions { get; } // 간선

        // 시청 완료 시 이벤트·보상 트리거. 해석 없이 실어 나르기만 함.
        public string EventKey { get; }

        // 이 에피소드가 속한 장면. 연출·롤백·커밋·저장의 경계가 이것 하나로 정해진다.
        //
        // 저작 쪽이 비워 두면 에피소드마다 고유한 값을 발급한다 — 장면 하나에
        // 에피소드 하나인 퇴화 상태이고, 그것이 장면 개념이 서기 전의 동작이다.
        public string SceneId { get; }

        public EpisodeNode(
            string episodeId,
            string title,
            string dialogueEntryId,
            IReadOnlyList<EpisodeOption> nextOptions = null,
            string eventKey = null,
            string sceneId = null)
        {
            EpisodeId = episodeId;
            Title = title ?? string.Empty;
            DialogueEntryId = dialogueEntryId ?? string.Empty;
            NextOptions = nextOptions ?? Array.Empty<EpisodeOption>();
            EventKey = eventKey ?? string.Empty;

            SceneId = string.IsNullOrEmpty(sceneId)
                ? DefaultSceneId(episodeId)
                : sceneId;
        }

        // 발급을 생성자에 두는 이유: 로더로 들어오든 코드로 만들든 SceneId가 비는
        // 노드가 없어야, 장면 비교가 "빈 것끼리 같다"로 무너지지 않는다.
        private static string DefaultSceneId(string episodeId) => "__scene_" + episodeId;
    }
}