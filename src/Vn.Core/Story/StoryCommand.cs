namespace Vn.Core.Story;

/// <summary>
/// 라인 사이에 나타난 명령 하나.
///
/// 이름과 인자를 나누지 않고 원본 문자열을 그대로 들고 있는다.
/// 대신 원본에서의 위치를 함께 담는다. 분기 트리는 <see cref="Depth"/>로
/// "선택지 갈래 안에 있던 명령"과 "선택지 사이에 있던 명령"을 가른다.
///
/// <code>
/// -&gt; 300만 받고 물러난다          Depth 0
///     &lt;&lt;set $paid_fee = 300&gt;&gt;     Depth 1 → 갈래 안
/// -&gt; 남은 600의 이유를 묻는다      Depth 0
/// </code>
/// </summary>
public sealed record StoryCommand(
    /// <summary><c>&lt;&lt;&gt;&gt;</c>를 포함한 원본 문자열.</summary>
    string Raw,

    int Line,
    int Column,

    /// <summary>들여쓰기 깊이. <see cref="StoryLine.Depth"/>와 같은 규칙으로 공백 4칸이 1이다.</summary>
    int Depth);
