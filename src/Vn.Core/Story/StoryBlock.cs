namespace Vn.Core.Story;

/// <summary>
/// 분기의 종류. 선택지와 조건은 포함 관계가 아니라 형제다.
/// 선택지는 라인이지만 조건은 라인이 아니다 — 화면에 뜨지도, 번역되지도, 입력을 기다리지도 않는다.
/// </summary>
public enum StoryBlockKind
{
    /// <summary><c>-&gt;</c>로 갈라지는 선택지 그룹.</summary>
    Option,

    /// <summary><c>&lt;&lt;if&gt;&gt;</c>·<c>&lt;&lt;elseif&gt;&gt;</c>·<c>&lt;&lt;else&gt;&gt;</c>로 갈라지는 조건문.</summary>
    Condition
}

/// <summary>
/// 이야기가 갈라지는 지점 하나. 갈래를 N개 가진다.
///
/// 선택지와 조건은 종류만 다르고 구조가 같다. 갈래의 라벨이 선택지면 표시 텍스트,
/// 조건이면 조건식이며 그것이 유일한 차이다. 그래서 화면 위젯도 하나면 된다.
///
/// <b>이 트리도 <see cref="StoryLine"/>과 같은 손실 압축이다.</b>
/// 표시하려고 가공한 것이라 이것으로 Yarn을 재조립하면 안 된다.
/// 쓰기는 <see cref="StartLine"/>·<see cref="EndLine"/>으로 원본을 부분 교체하는 경로여야 한다.
/// </summary>
public sealed record StoryBlock(
    StoryBlockKind Kind,
    IReadOnlyList<StoryBranch> Branches,
    string FilePath,

    /// <summary>블록이 시작하는 원본 줄. 첫 갈래를 여는 줄이다.</summary>
    int StartLine,

    /// <summary>블록이 끝나는 원본 줄. 조건문이면 <c>&lt;&lt;endif&gt;&gt;</c> 줄이다.</summary>
    int EndLine,

    /// <summary>블록 자체의 들여쓰기 깊이. 갈래 안의 내용은 이보다 깊다.</summary>
    int Depth);
