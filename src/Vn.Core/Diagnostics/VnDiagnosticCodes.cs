namespace Vn.Core.Diagnostics;

/// <summary>
/// 진단 코드의 단일 원본.
/// 코드는 문서와 억제 규칙(<c>// vn:disable VN3001</c>)에 그대로 노출되는 영구 계약이므로
/// 문자열 리터럴을 여기저기 흩뿌리지 않고 이 클래스에서만 정의한다.
///
/// 접두사 규칙:
///   VN1xxx  게임 스키마 로드·형식
///   VN2xxx  Yarn 프로젝트·컴파일 인프라
///   VN3xxx  변수·명령 사용
///   VN4xxx  노드 그래프
///   YS####  Yarn 컴파일러가 낸 진단을 그대로 통과시킨 것
///
/// 코드는 예외 없이 <c>접두사 두 글자 + 숫자 네 자리</c> 한 가지 형태만 갖는다.
/// 억제 규칙 파서가 형태를 하나만 알면 되도록 하기 위한 것이며,
/// 이 규칙을 깨는 순간 문서·검색·억제가 전부 두 갈래로 갈라진다.
/// 형태가 맞지 않는 Yarn 코드는 <see cref="YarnUnclassified"/>로 보내고
/// 원본 문자열은 코드가 아니라 메시지에 담는다. 코드는 기계가 읽고 메시지는 사람이 읽는다.
/// </summary>
public static class VnDiagnosticCodes
{
    /// <summary>우리 도구가 직접 만든 진단의 접두사.</summary>
    public const string VnPrefix = "VN";

    /// <summary>Yarn 컴파일러 진단을 통과시킬 때 사용하는 접두사.</summary>
    public const string YarnPrefix = "YS";

    // VN1xxx — 게임 스키마 로드·형식
    public const string SchemaFileNotFound = "VN1001";
    public const string SchemaEmpty = "VN1002";
    public const string SchemaJsonInvalid = "VN1003";
    public const string SchemaFileUnreadable = "VN1004";
    public const string SchemaVersionInvalid = "VN1010";
    public const string SchemaDuplicateVariable = "VN1011";
    public const string SchemaDuplicateCommand = "VN1012";
    public const string SchemaVariableIdEmpty = "VN1013";
    public const string SchemaVariableTypeUnsupported = "VN1014";
    public const string SchemaCommandIdEmpty = "VN1015";
    public const string SchemaCommandIdConflict = "VN1016";
    public const string SchemaDefaultValueInvalid = "VN1017";

    // VN2xxx — Yarn 프로젝트·컴파일 인프라
    public const string YarnProjectNotFound = "VN2001";
    public const string YarnProjectHasNoSource = "VN2002";
    public const string YarnUnexpectedFailure = "VN2003";

    /// <summary>
    /// Yarn 진단 객체의 모양이 우리가 아는 것과 다를 때.
    /// 이 진단이 뜨면 Yarn이 낸 나머지 진단의 심각도와 위치를 믿을 수 없다.
    /// 조용히 넘어가면 오류가 Info로 강등되어 종료 코드 0이 나가므로 반드시 오류로 알린다.
    /// </summary>
    public const string YarnDiagnosticShapeUnknown = "VN2004";

    // VN3xxx — 변수·명령 사용
    public const string UnknownVariable = "VN3001";
    public const string UnknownCommand = "VN3002";

    // VN4xxx — 노드 그래프
    public const string UnknownJumpTarget = "VN4001";

    // VN5xxx — 작성 규약. 전부 Warning이다.
    //
    // 규약 위반은 "틀린 것"이 아니라 "이 툴이 편하게 다루기 어려운 것"이다.
    // 이 .yarn은 Unity에서도, 이전 도구에서도 편집되므로 규약을 지키지 않는 파일이 반드시 있다.
    // 파서는 그것을 읽어내고, 알리기만 한다.
    public const string OptionBranchHasNoLine = "VN5001";
    public const string OptionBranchHasNoDestination = "VN5002";
    public const string OptionBranchHasManyJumps = "VN5003";

    /// <summary>Yarn 진단 코드를 우리 형태로 옮기지 못했을 때 쓰는 대체 코드.</summary>
    public const string YarnUnclassified = "YS0000";

    /// <summary>이 진단이 Yarn 컴파일러에서 온 것인지 여부.</summary>
    public static bool IsFromYarnCompiler(string code)
    {
        return code.StartsWith(YarnPrefix, StringComparison.Ordinal);
    }

    /// <summary>이 진단을 VnTool이 직접 만들었는지 여부.</summary>
    public static bool IsFromVnTool(string code)
    {
        return code.StartsWith(VnPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Yarn이 낸 코드를 우리 코드 형태로 옮긴다.
    /// <c>YS</c> 뒤에 숫자만 오는 형태여야 통과시키며, 그 외에는 전부 실패로 보고
    /// <see cref="YarnUnclassified"/>를 돌려준다. 호출부는 실패한 경우 원본 문자열을
    /// 메시지에 담아 정보를 잃지 않게 해야 한다.
    /// </summary>
    public static bool TryNormalizeYarnCode(string? code, out string normalized)
    {
        normalized = YarnUnclassified;

        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string trimmed = code.Trim();

        // "YS" + 숫자 최소 한 자리.
        if (trimmed.Length < 3)
        {
            return false;
        }

        if (trimmed[0] is not ('Y' or 'y') ||
            trimmed[1] is not ('S' or 's'))
        {
            return false;
        }

        for (int index = 2; index < trimmed.Length; index++)
        {
            if (trimmed[index] is < '0' or > '9')
            {
                return false;
            }
        }

        normalized = trimmed.ToUpperInvariant();
        return true;
    }
}