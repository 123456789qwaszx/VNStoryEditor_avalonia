using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Results;

/// <summary>
/// 발행 결과 하나를 정확히 지목하는 값.
///
/// 버전 번호만으로는 부족하다. 같은 v1이라도 도구가 다른 규칙으로 만들었다면 다른 것이고
/// (<see cref="SchemaVersion"/>), 사람이 파일을 손으로 고쳤다면 또 다른 것이다
/// (<see cref="ContentHash"/>). Runtime Full을 다시 만들 때 "정확히 같은 입력"이라고
/// 말할 수 있으려면 세 값이 모두 맞아야 한다.
/// </summary>
/// <param name="ResultId">버전 계보의 Id. v1과 v2는 이 값을 공유한다.</param>
/// <param name="Version">1부터 시작한다. 0은 아직 발행하지 않은 작업 중 결과다.</param>
public readonly record struct ResultIdentity(
    string ResultId,
    int Version,
    int SchemaVersion,
    string ContentHash)
{
    /// <summary>발행하지 않은 작업 중 결과에 쓰는 계보 Id.</summary>
    public const string WorkingResultId = "(working)";

    /// <summary>발행된 결과인지. 작업 중 결과는 어떤 Presentation과도 호환되지 않는다.</summary>
    public bool IsPublished => Version > 0 &&
        !string.Equals(ResultId, WorkingResultId, StringComparison.Ordinal);

    public string Label => IsPublished ? $"{ResultId} v{Version}" : "작업 중";

    public static ResultIdentity Working(int schemaVersion, string contentHash) =>
        new(WorkingResultId, 0, schemaVersion, contentHash);
}

/// <summary>
/// PresentationResult가 자기 입력이었던 DialogueResult를 지목하는 값.
/// Id·Version·Hash를 모두 적어 두어야 나중에 "이 연출이 어느 대사 위에서 만들어졌는가"에
/// 답할 수 있다.
/// </summary>
public readonly record struct DialogueResultReference(
    string ResultId,
    int Version,
    string ContentHash)
{
    public static DialogueResultReference Of(DialogueResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new DialogueResultReference(
            result.Identity.ResultId,
            result.Identity.Version,
            result.Identity.ContentHash);
    }

    public bool Matches(ResultIdentity identity)
    {
        return string.Equals(ResultId, identity.ResultId, StringComparison.Ordinal) &&
            Version == identity.Version &&
            string.Equals(ContentHash, identity.ContentHash, StringComparison.Ordinal);
    }

    public string Label => $"{ResultId} v{Version}";
}

/// <summary>
/// 결과 본문의 내용 해시.
///
/// <b>identity와 발행 시각은 해시에 넣지 않는다.</b> 같은 내용을 다시 발행했을 때 같은 값이
/// 나와야 중복 발행을 걸러 낼 수 있고, 무엇이 실제로 달라졌는지 말할 수 있다.
/// 정규 표현은 저장 형식과 같은 결정적 JSON을 그대로 쓴다. 해시 전용 표현을 따로 만들면
/// 저장 형식이 바뀔 때 둘이 조용히 어긋난다.
/// </summary>
public static class ResultHash
{
    public static string Compute(JsonObject body)
    {
        ArgumentNullException.ThrowIfNull(body);
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSupport.ToDeterministicText(body)));
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    /// <summary>이 결과의 본문에서 해시를 다시 계산한다.</summary>
    public static string Of(DialogueResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Compute(DialogueResultJson.WriteBody(result));
    }

    public static string Of(PresentationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Compute(PresentationResultJson.WriteBody(result));
    }

    /// <summary>
    /// 결과가 자기 identity와 일치하는지. 결과 파일이 도구 밖에서 바뀌었는지 확인하는 자리다.
    /// </summary>
    public static bool IsIntact(DialogueResult result) =>
        string.Equals(Of(result), result.Identity.ContentHash, StringComparison.Ordinal);

    public static bool IsIntact(PresentationResult result) =>
        string.Equals(Of(result), result.Identity.ContentHash, StringComparison.Ordinal);
}
