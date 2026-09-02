using System.Collections.Generic;

namespace Ked.Progression
{
    public enum ChapterAdvanceKind
    {
        AwaitPlayerChoice = 0, // 고를 수 있는 것이 하나 이상 있다. 플레이어의 입력 대기.
        ChapterEnded = 1,
        AutoAdvance = 2,       // 자동 간선 하나뿐. 묻지 않고 Options[0]으로 간다.
    }

    // 현재 노드 전체의 진행 판정 결과
    public readonly struct ChapterAdvance
    {
        public ChapterAdvanceKind Kind { get; }

        // 화면에 그릴 목록.
        // "ChapterAdvanceKind.AwaitPlayerChoice"일 때 사용.
        public IReadOnlyList<ResolvedOption> Options { get; }
        
        // 표시조건 미달로 목록에서 빠진 개수. 그리는 데는 안 쓰인다.
        // (에디터 및 디버깅 용)
        public int HiddenCount { get; }

        internal ChapterAdvance(
            ChapterAdvanceKind kind,
            IReadOnlyList<ResolvedOption> options,
            int hiddenCount)
        {
            Kind = kind;
            Options = options;
            HiddenCount = hiddenCount;
        }

        public override string ToString() => $"{Kind}(보임 {Options.Count}, 숨김 {HiddenCount})";
    }
}