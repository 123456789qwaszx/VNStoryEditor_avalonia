using System.Collections.Generic;

namespace Ked.Progression
{
    public enum ChapterAdvanceKind
    {
        AwaitPlayerChoice = 0, // 고를 수 있는 것이 하나 이상 있다. 플레이어의 입력 대기.
        AutoAdvance = 1, // 고를 수 있는 것이 없을 시, 자동으로 진행.
        ChapterEnded = 2,
    }

    // 현재 노드 전체의 진행 판정 결과
    public readonly struct ChapterAdvance
    {
        public ChapterAdvanceKind Kind { get; }

        // 화면에 그릴 목록.
        // "ChapterAdvanceKind.AwaitPlayerChoice"일 때만 사용.
        public IReadOnlyList<ResolvedOption> Options { get; }

        // "ChapterAdvanceKind.AutoAdvance일 때만 사용.
        public EpisodeOption AutoOption { get; }


        // 표시조건 미달로 목록에서 빠진 개수. 그리는 데는 안 쓰인다.
        // (에디터 및 디버깅 용)
        public int HiddenCount { get; }

        internal ChapterAdvance(
            ChapterAdvanceKind kind,
            IReadOnlyList<ResolvedOption> options,
            EpisodeOption autoOption,
            int hiddenCount)
        {
            Kind = kind;
            Options = options;
            AutoOption = autoOption;
            HiddenCount = hiddenCount;
        }

        public override string ToString() => $"{Kind}(보임 {Options.Count}, 숨김 {HiddenCount})";
    }
}