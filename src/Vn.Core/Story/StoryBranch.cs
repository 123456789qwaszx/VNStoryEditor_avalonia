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

    /// <summary>
    /// 이 갈래를 고르면 이야기가 이어지는 노드. 선택지 갈래에만 있고 조건 갈래는 언제나 null이다.
    ///
    /// 갈래의 마지막 요소가 <c>&lt;&lt;jump X&gt;&gt;</c> 하나뿐일 때만 여기로 올라오고,
    /// 그때 그 명령은 <see cref="Commands"/>에서 빠진다.
    /// null은 "점프 없이 갈래가 끝나고 그룹 다음으로 흘러간다"는 뜻이며 오류가 아니다.
    /// 점프가 여러 개면 규약 밖이라 올리지 않고 <see cref="Commands"/>에 그대로 남긴다.
    /// </summary>
    string? Destination,

    /// <summary>
    /// 이 갈래 안에서 실행되는 명령들. 원본 순서를 지킨다.
    ///
    /// 자식 트리와 따로 두는 이유는 박스 모델 때문이다. 명령은 그 자체로 화면 단위가 아니라
    /// 다음 라인에 붙어 한 박스를 이룬다. 명령을 자식으로 섞으면 라인과 형제가 되어
    /// 그 관계가 깨진다.
    ///
    /// 갈래 안에 대사가 하나도 없는 경우가 흔하다. 그때 이 목록이 갈래의 내용 전부다.
    /// </summary>
    IReadOnlyList<StoryCommand> Commands,

    /// <summary>갈래 안의 내용. 라인과 중첩된 블록이 원본 순서대로 섞여 있다.</summary>
    IReadOnlyList<StoryElement> Children);
