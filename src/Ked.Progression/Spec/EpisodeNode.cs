using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    public enum EpisodeKind
    {
        Main = 0,
        Attachment = 1,
    }

    public sealed class EpisodeNode
    {
        private readonly int _autoOptionIndex;

        public string EpisodeId { get; }
        public string Title { get; }
        public EpisodeKind Kind { get; }

        // 호스트가 재생할 대본 키.
        public string DialogueEntryId { get; }

        // 나가는 길. 배열 순서가 곧 화면에 뜨는 순서.
        // 저작엑셀의 간선시트의 행 순서 유지.
        public IReadOnlyList<EpisodeOption> NextOptions { get; }

        public string EndingKey { get; }

        public string DesignerNote { get; }

        public bool IsEndingCandidate => EndingKey.Length != 0;

        public EpisodeNode(
            string episodeId,
            string title,
            EpisodeKind kind,
            string dialogueEntryId,
            IReadOnlyList<EpisodeOption> nextOptions = null,
            string endingKey = null,
            string designerNote = null)
        {
            if (string.IsNullOrEmpty(episodeId))
            {
                throw new ArgumentException("에피소드 ID가 비어 있다.", nameof(episodeId));
            }

            EpisodeId = episodeId;
            Title = title ?? string.Empty;
            Kind = kind;
            DialogueEntryId = dialogueEntryId ?? string.Empty;
            NextOptions = nextOptions ?? Array.Empty<EpisodeOption>();
            EndingKey = endingKey ?? string.Empty;
            DesignerNote = designerNote ?? string.Empty;

            _autoOptionIndex = FindAutoOption(EpisodeId, NextOptions);
        }

        // 문구 없는 자동 진행 간선. 디폴트로 사용.
        public bool TryGetAutoOption(out EpisodeOption option)
        {
            if (_autoOptionIndex < 0)
            {
                option = null;
                return false;
            }

            option = NextOptions[_autoOptionIndex];
            return true;
        }

        public override string ToString() => $"{EpisodeId}({Kind})";

        // 자동 진행 간선은 에피소드당 하나. 
        private static int FindAutoOption(
            string episodeId, IReadOnlyList<EpisodeOption> options)
        {
            int found = -1;

            for (int i = 0; i < options.Count; i++)
            {
                EpisodeOption option = options[i];

                if (option == null)
                {
                    throw new ArgumentException(
                        $"에피소드 '{episodeId}'의 {i}번째 선택지가 null이다.", nameof(options));
                }

                if (option.Kind != OptionKind.AutoAdvance)
                {
                    continue;
                }

                if (found >= 0)
                {
                    throw new ArgumentException(
                        $"에피소드 '{episodeId}'에 자동 진행 간선이 둘 이상이다: " +
                        $"[{found}] → {options[found].TargetEpisodeId}, " +
                        $"[{i}] → {option.TargetEpisodeId}. " +
                        "에피소드당 하나여야 한다(§G6-2).",
                        nameof(options));
                }

                found = i;
            }

            return found;
        }
    }
}