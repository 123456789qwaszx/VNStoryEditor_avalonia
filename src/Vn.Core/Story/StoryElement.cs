namespace Vn.Core.Story;

/// <summary>
/// 갈래 안에 들어갈 수 있는 것. 라인이거나 중첩된 분기 블록이다.
///
/// <see cref="StoryLine"/>에 공통 인터페이스를 붙이지 않고 감싸는 방식을 쓴다.
/// <see cref="StoryLine"/>은 순수 텍스트 뷰와 CLI 픽스처가 이미 의존하고 있어서
/// 건드리지 않는 편이 안전하다.
/// </summary>
public abstract record StoryElement;

/// <summary>재생되는 라인 하나.</summary>
public sealed record StoryLineElement(StoryLine Line) : StoryElement;

/// <summary>갈래 안에서 다시 갈라지는 지점. 조건문 안의 선택지, 선택지 안의 조건문.</summary>
public sealed record StoryBlockElement(StoryBlock Block) : StoryElement;
