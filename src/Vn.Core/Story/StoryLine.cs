namespace Vn.Core.Story;

/// <summary>
/// 노드 본문에서 재생되는 라인 하나. 대사이거나 선택지다.
///
/// Yarn은 라인 단위로 재생된다. 명령(<c>&lt;&lt;...&gt;&gt;</c>)은 그 자리에서 실행되지 않고
/// 쌓였다가 다음 라인이 재생될 때 함께 실행된다. 그래서 한 라인이 박스 하나를 닫는다.
/// </summary>
public sealed record StoryLine(
    /// <summary>
    /// 첫 <c>:</c> 앞부분. 공백·<c>{</c>·<c>&lt;</c>·<c>//</c>가 섞여 있으면 화자로 보지 않고 null이다.
    /// Yarn과 Unity가 쓰는 관례와 같은 결과를 낸다.
    /// </summary>
    string? Speaker,

    /// <summary>
    /// 화자와 해시태그와 주석을 뺀 본문. 원본에서 잘라낸 것이라
    /// <c>{$var}</c> 같은 인터폴레이션과 조건부 선택지의 <c>&lt;&lt;if ...&gt;&gt;</c>가 그대로 남아 있다.
    /// </summary>
    string Text,

    /// <summary><c>#</c>를 뗀 해시태그. 원본 순서를 지킨다.</summary>
    IReadOnlyList<string> Hashtags,

    string FilePath,
    int Line,
    int Column,

    /// <summary>들여쓰기 깊이. 공백 4칸이 1이다.</summary>
    int Depth,

    /// <summary><c>-&gt;</c>로 시작하는 선택지인지 여부.</summary>
    bool IsOption,

    /// <summary>
    /// 이 라인과 직전 라인 사이의 텍스트에 나타난 명령들. 원본 순서를 지킨다.
    ///
    /// <b>이것은 텍스트 순서일 뿐 실행 순서가 아니다.</b>
    /// 선택지 갈래 안의 명령은 다음 선택지의 목록에 붙는다.
    /// 실행 관계는 분기 트리(다음 단계)가 결정하며, 그 판정에
    /// <see cref="StoryCommand.Depth"/>를 쓴다.
    ///
    /// 뒤에 라인이 없는 명령은 어느 목록에도 들어가지 않는다.
    /// 그 정보는 <see cref="StoryNode.CommandCalls"/>와 <see cref="StoryNode.Jumps"/>에 이미 있다.
    /// </summary>
    IReadOnlyList<StoryCommand> CommandsSincePreviousLine);
