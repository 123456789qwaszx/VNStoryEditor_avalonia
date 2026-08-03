using System.Security.Cryptography;

namespace Vn.Authoring.Model;

/// <summary>
/// 노드·줄·조건의 안정된 식별자를 만든다.
///
/// 이름이나 순서를 식별자로 쓰지 않는다. 작가는 노드 이름을 바꾸고 줄 순서를 계속 바꾸는데,
/// 그때마다 간선과 출구가 끊어지면 저작 도구로 쓸 수 없다.
/// 그래서 한 번 만들어지면 절대 변하지 않는 값을 따로 둔다.
///
/// 사람이 읽는 이름은 <see cref="StoryNode.Name"/>이고, 기계가 가리키는 것은 이 값이다.
/// 파일에도 이 값이 그대로 들어가므로 버전 관리 diff에서 알아볼 수 있도록 짧게 만든다.
/// </summary>
public static class Identifier
{
    private const string Alphabet = "0123456789abcdefghijkmnpqrstuvwxyz";

    /// <summary>
    /// <paramref name="prefix"/>로 시작하는 새 식별자. 예: <c>nd_7k2m9q4x</c>.
    /// 접두사는 파일을 눈으로 읽을 때 그것이 무엇을 가리키는지 알려 주는 용도다.
    /// </summary>
    public static string Create(string prefix)
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);

        Span<char> chars = stackalloc char[8];

        for (int index = 0; index < chars.Length; index++)
        {
            chars[index] = Alphabet[bytes[index] % Alphabet.Length];
        }

        return $"{prefix}_{new string(chars)}";
    }

    public static string File() => Create("sf");

    public static string Link() => Create("lk");

    public static string Node() => Create("nd");

    public static string Line() => Create("ln");

    public static string PresentationCommand() => Create("pc");

    public static string Condition() => Create("cd");

    /// <summary>선택지 옵션 하나. 라벨 문구가 바뀌어도 유지되는 정체성이다.</summary>
    public static string Option() => Create("op");

    /// <summary>대본 산출물 하나. 작가 파일의 이름이나 경로와 독립적이다.</summary>
    public static string Script() => Create("sc");

    /// <summary>
    /// 발행 결과의 계보. 같은 노드에서 v1, v2로 이어지는 결과는 이 Id를 공유하고
    /// 버전 번호로 구분한다.
    /// </summary>
    public static string Result() => Create("rs");

    public static string Composition() => Create("rc");
}
