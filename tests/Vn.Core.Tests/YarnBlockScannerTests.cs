using Vn.Core.Yarn;
using Yarn.Compiler;

namespace Vn.Core.Tests;

/// <summary>
/// 줄 분류만 검증한다. 트리는 다음 커밋이다.
///
/// 여기서 틀리면 그 위에 세운 트리는 조용히 틀린다. 앞선 시도에서 실제로 그랬다 —
/// 들여쓴 <c>&lt;&lt;if&gt;&gt;</c>가 일반 명령으로 분류되는 바람에
/// 선택지 갈래 안의 조건 블록이 통째로 사라졌는데, 빌드도 테스트도 픽스처도 전부 통과했다.
/// </summary>
public class YarnBlockScannerTests
{
    private static List<YarnScannedLine> Scan(string yarn)
    {
        var job = CompilationJob.CreateFromString("Story.yarn", yarn);
        job.CompilationType = CompilationJob.Type.FullCompilation;

        CompilationResult result = Compiler.Compile(job);

        return YarnBlockScanner.ScanFile(result.ParseResults.Single());
    }

    private static YarnScannedLine At(string yarn, int line)
    {
        return Assert.Single(Scan(yarn), scanned => scanned.Line == line);
    }

    /// <summary>
    /// 앞선 시도가 빗나간 지점. 들여쓴 줄은 INDENT 토큰이 앞에 오기 때문에,
    /// 키워드를 줄의 시작 인덱스에서 찾으면 COMMAND_START 자신을 읽고 조건문을 놓친다.
    /// </summary>
    [Fact]
    public void 들여쓴_조건문도_조건_구분자로_분류한다()
    {
        const string yarn = """
            title: T
            ---
            -> 첫째 선택지
                <<if $favor >= 5>>
                라루: 안쪽.
                <<endif>>
            ===
            """;

        YarnScannedLine opened = At(yarn, 4);

        Assert.Equal(YarnLineKind.If, opened.Kind);
        Assert.Equal(1, opened.Depth);
        Assert.Equal("<<if $favor >= 5>>", opened.Raw);

        YarnScannedLine closed = At(yarn, 6);

        Assert.Equal(YarnLineKind.EndIf, closed.Kind);
        Assert.Equal(1, closed.Depth);
    }

    /// <summary>
    /// 노드 마지막 줄의 <c>&lt;&lt;endif&gt;&gt;</c>는 평평한 라인 모델에서 버려진다.
    /// 뒤에 라인이 없어 어느 박스도 닫지 않기 때문이다.
    /// 분류기는 그것까지 봐야 블록을 닫을 수 있다.
    /// </summary>
    [Fact]
    public void 노드_마지막_줄의_endif도_분류한다()
    {
        const string yarn = """
            title: T
            ---
            <<if $favor >= 5>>
            라루: 참일 때.
            <<endif>>
            ===
            """;

        Assert.Equal(YarnLineKind.EndIf, At(yarn, 5).Kind);
    }

    /// <summary>
    /// 조건 구분자만 승격 대상이다. 갈래 안의 보통 명령은 그대로 명령이어야 한다.
    /// 여기서 <c>&lt;&lt;jump&gt;&gt;</c>가 구분자로 새면 블록이 엉뚱한 곳에서 닫힌다.
    /// </summary>
    [Fact]
    public void 선택지_갈래_안의_jump는_일반_명령이다()
    {
        const string yarn = """
            title: T
            ---
            -> 열쇠를 건넨다
                <<set $has_room_key = true>>
                <<jump Ending>>
            ===
            """;

        YarnScannedLine set = At(yarn, 4);
        Assert.Equal(YarnLineKind.Command, set.Kind);
        Assert.Equal(1, set.Depth);

        YarnScannedLine jump = At(yarn, 5);
        Assert.Equal(YarnLineKind.Command, jump.Kind);
        Assert.Equal(1, jump.Depth);
        Assert.Equal("<<jump Ending>>", jump.Raw);
    }

    [Fact]
    public void 대사와_선택지를_구분한다()
    {
        const string yarn = """
            title: T
            ---
            Ann: 어서 오세요.
            -> 열쇠를 건넨다
                라루: 갈래 안.
            ===
            """;

        Assert.Equal(YarnLineKind.Line, At(yarn, 3).Kind);
        Assert.Equal(0, At(yarn, 3).Depth);

        Assert.Equal(YarnLineKind.Option, At(yarn, 4).Kind);
        Assert.Equal(0, At(yarn, 4).Depth);

        Assert.Equal(YarnLineKind.Line, At(yarn, 5).Kind);
        Assert.Equal(1, At(yarn, 5).Depth);
    }

    [Fact]
    public void elseif와_else를_각각_구분한다()
    {
        const string yarn = """
            title: T
            ---
            <<if $favor >= 8>>
            라루: 높음.
            <<elseif $favor >= 5>>
            윌로: 중간.
            <<else>>
            아야메: 낮음.
            <<endif>>
            ===
            """;

        Assert.Equal(YarnLineKind.If, At(yarn, 3).Kind);
        Assert.Equal(YarnLineKind.ElseIf, At(yarn, 5).Kind);
        Assert.Equal(YarnLineKind.Else, At(yarn, 7).Kind);
        Assert.Equal(YarnLineKind.EndIf, At(yarn, 9).Kind);

        // 조건문은 들여쓰지 않아도 된다. 그래서 깊이로는 갈래를 나눌 수 없고 구분자로 나눈다.
        Assert.All(Scan(yarn), scanned => Assert.Equal(0, scanned.Depth));
    }

    [Fact]
    public void 헤더와_구분선은_분류하지_않는다()
    {
        const string yarn = """
            title: T
            ---
            Ann: 한 줄.
            ===
            """;

        // title, ---, === 는 줄이 아니다. 남는 것은 대사 한 줄뿐이다.
        YarnScannedLine only = Assert.Single(Scan(yarn));

        Assert.Equal(3, only.Line);
        Assert.Equal(YarnLineKind.Line, only.Kind);
    }

    [Fact]
    public void 빈_줄은_분류하지_않는다()
    {
        const string yarn = """
            title: T
            ---
            Ann: 앞.

            Ann: 뒤.
            ===
            """;

        Assert.Equal(new[] { 3, 5 }, Scan(yarn).Select(scanned => scanned.Line));
    }
}
