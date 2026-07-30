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
///   YS####  Yarn 컴파일러가 낸 진단을 그대로 통과시킨 것 (원본 코드 보존)
///
/// 접두사만 보고 "우리가 낸 진단(VN)"과 "Yarn이 낸 진단(YS)"을 구분할 수 있어야 한다.
/// 억제 규칙은 이 구분 위에서 동작한다.
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

    // VN3xxx — 변수·명령 사용
    public const string UnknownVariable = "VN3001";
    public const string UnknownCommand = "VN3002";

    // VN4xxx — 노드 그래프
    public const string UnknownJumpTarget = "VN4001";

    /// <summary>Yarn 진단에 코드가 없을 때 사용하는 대체 코드.</summary>
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
}
