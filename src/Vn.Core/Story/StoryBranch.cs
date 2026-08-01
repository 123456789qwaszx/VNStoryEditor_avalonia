namespace Vn.Core.Story;

/// <summary>
/// 분기의 갈래 하나. 라벨과 그 아래 내용을 가진다.
///
/// 라벨은 종류에 따라 다른 것을 담는다.
///   선택지 → 화면에 뜨는 표시 텍스트 (<c>-&gt;</c>를 뗀 것)
///   조건   → 조건식 (<c>&lt;&lt;if $favor &gt;= 5&gt;&gt;</c>의 원본 문자열)
/// <c>&lt;&lt;else&gt;&gt;</c> 갈래처럼 조건식이 없는 자리도 있다.
/// </summary>
public sealed record StoryBranch(
    string Label,

    /// <summary>이 갈래를 여는 원본 줄. 선택지면 <c>-&gt;</c> 줄, 조건이면 <c>&lt;&lt;if&gt;&gt;</c> 계열 줄이다.</summary>
    int Line,

    /// <summary>갈래 안의 내용. 라인과 중첩된 블록이 원본 순서대로 섞여 있다.</summary>
    IReadOnlyList<StoryElement> Children);
