using System.Text;
using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// 산출물 컴파일 검증 (2026-08-23) — 이미터가 방금 쓴 대본이 실제로 컴파일되는가.
///
/// 이 검사가 없던 동안 프로덕션 경로에서 Yarn 컴파일러를 부르는 자리가 <b>0건</b>이었다.
/// 컴파일 안 되는 대본을 써도 유니티까지 아무도 몰랐다.
///
/// ⚠ 여기 테스트는 <b>헤드리스 UI가 필요 없다</b> — 검증기가 세션도 화면도 모른다.
/// </summary>
public sealed class YarnOutputVerificationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-yarn-verify", Guid.NewGuid().ToString("N"));

    public YarnOutputVerificationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 컴파일되는_대본에는_아무_말도_하지_않는다()
    {
        // 상태줄은 사람이 할 일이 있을 때만 쓴다 — 고아 보고와 같은 규칙.
        string[] written = [Write("Story_ep1.yarn", Story("Story_ep1", "라루: 첫 줄 #line:ln_001"))];

        YarnOutputVerdict verdict = YarnOutputVerification.Verify(written);

        Assert.False(verdict.HasErrors, string.Join(" / ", verdict.Errors));
        Assert.Equal(1, verdict.FileCount);
        Assert.Null(YarnOutputVerification.ReportOf(verdict));
    }

    [Fact]
    public void 문법이_깨진_대본을_잡는다()
    {
        // `<<if>>`를 열고 안 닫았다. 이미터 회귀가 이런 모양으로 나온다.
        string[] written =
        [
            Write("Story_broken.yarn",
                """
                title: Story_broken
                ---
                <<if $x >= 1>>
                라루: 닫는 endif가 없다 #line:ln_001
                ===
                """)
        ];

        YarnOutputVerdict verdict = YarnOutputVerification.Verify(written);

        Assert.True(verdict.HasErrors);
        Assert.Contains("Story_broken.yarn", YarnOutputVerification.ReportOf(verdict));
    }

    [Fact]
    public void 같은_노드_이름이_두_파일에_있으면_잡는다()
    {
        // 2026-08-23의 이름 사건이 남긴 자리 — 타이틀은 세이브 키이자 진입 키다(계약서 C2).
        // 두 대본이 같은 타이틀을 내면 런타임이 어느 쪽을 재생할지 아무도 답할 수 없다.
        string[] written =
        [
            Write("Story_a.yarn", Story("Story_같은이름", "라루: 이쪽 #line:ln_001")),
            Write("Story_b.yarn", Story("Story_같은이름", "윌로: 저쪽 #line:ln_002"))
        ];

        YarnOutputVerdict verdict = YarnOutputVerification.Verify(written);

        Assert.True(verdict.HasErrors, "같은 타이틀 둘을 통과시키면 안 된다");
    }

    [Fact]
    public void 검사는_산출_폴더를_바꾸지_않는다()
    {
        // ⛔ 규율. 검증하려고 `.yarnproject`를 산출 폴더에 만들면 유니티가 읽는 폴더가
        // 달라지고 고아 스캔에도 걸린다 — 검증 때문에 산출물이 달라지면 검증이 아니다.
        string[] written = [Write("Story_ep1.yarn", Story("Story_ep1", "라루: 첫 줄 #line:ln_001"))];

        string[] before = Snapshot();
        YarnOutputVerification.Verify(written);
        string[] after = Snapshot();

        Assert.Equal(before, after);
    }

    [Fact]
    public void 대본이_아닌_동반_파일은_컴파일에_걸지_않는다()
    {
        // 이미터는 `curves.json`도 같은 폴더에 낸다. 그것을 대본으로 물면 없는 오류가 난다.
        string[] written =
        [
            Write("Story_ep1.yarn", Story("Story_ep1", "라루: 첫 줄 #line:ln_001")),
            Write("curves.json", """{ "curves": [] }""")
        ];

        YarnOutputVerdict verdict = YarnOutputVerification.Verify(written);

        Assert.False(verdict.HasErrors, string.Join(" / ", verdict.Errors));
        Assert.Equal(1, verdict.FileCount);   // json은 세지 않는다
    }

    [Fact]
    public void 볼_대본이_없으면_실패가_아니다()
    {
        // 출력 폴더 미지정·막힌 노드뿐 — 쓸 것이 없었던 것이지 잘못된 것이 아니다.
        Assert.False(YarnOutputVerification.Verify(null).HasErrors);
        Assert.False(YarnOutputVerification.Verify([]).HasErrors);
        Assert.Equal(0, YarnOutputVerification.Verify([]).FileCount);

        // 목록에 있는데 디스크에 없는 경로도 조용히 건너뛴다 — 쓰기 실패는 이미
        // 그쪽에서 보고된다. 같은 사고를 두 번 말하지 않는다.
        Assert.Equal(0,
            YarnOutputVerification.Verify([Path.Combine(_directory, "없는파일.yarn")]).FileCount);
    }

    [Theory]
    [InlineData("Story_golden_ep.yarn")]
    [InlineData("Story_choices_ep.yarn")]
    public void 실제_이미터_골든_산출물이_컴파일된다(string storyFile)
    {
        // 합성 픽스처만 통과시키는 검증기는 아무것도 지키지 않는다. 이미터가 실제로 내는
        // 글자(`Vn.Authoring.Tests/Golden`)에 그대로 건다 — 저 골든이 바뀌면 여기도 운다.
        //
        // ⚠ 한 벌은 **대본 하나 + declarations**다. 골든 폴더에는 서로 무관한 테스트 대본
        // 둘이 함께 살아서 통째로 걸면 LineId가 겹친다(둘 다 ln_004부터 쓴다) — 그것은
        // 이미터의 잘못이 아니라 픽스처 폴더의 사정이다. 진짜 산출 폴더는 한 프로젝트의
        // 것만 담으므로, 거기서 겹치면 그때는 진짜 오류다(계약서 C4 · 아래 테스트).
        //
        // ⚠ 실측으로 하나 배웠다: 자기 밖의 노드로 나가는 `<<jump>>`(`Story_기본으로_간다`)는
        // **컴파일 오류가 아니다.** 그래서 막혀서 빠진 노드가 있어도 이 검사가 그 이유로
        // 무더기 오탐을 내지 않는다 — 그 사실은 `blocked` 보고가 따로 말한다.
        string golden = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "Vn.Authoring.Tests", "Golden"));

        Assert.True(Directory.Exists(golden), $"골든 폴더를 못 찾았다: {golden}");

        string[] sources =
        [
            Path.Combine(golden, storyFile),
            Path.Combine(golden, "declarations.yarn")
        ];

        YarnOutputVerdict verdict = YarnOutputVerification.Verify(sources);

        Assert.False(verdict.HasErrors, string.Join(" / ", verdict.Errors));
        Assert.Equal(2, verdict.FileCount);
    }

    [Fact]
    public void 한_벌_안에서_LineId가_겹치면_잡는다()
    {
        // 계약서 C4 — LineId는 전역으로 유일해야 한다. 연출이 매달리는 열쇠라, 겹치면
        // 어느 줄의 연출인지 아무도 답할 수 없다. 그리고 파일 하나만 봐서는 절대 안 보인다.
        string[] written =
        [
            Write("Story_a.yarn", Story("Story_a", "라루: 이쪽 #line:ln_001")),
            Write("Story_b.yarn", Story("Story_b", "윌로: 저쪽 #line:ln_001"))
        ];

        YarnOutputVerdict verdict = YarnOutputVerification.Verify(written);

        Assert.True(verdict.HasErrors, "겹친 LineId를 통과시키면 안 된다");
        Assert.Contains(verdict.Errors, error => error.Contains("ln_001", StringComparison.Ordinal));
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static string Story(string title, string body) =>
        $"""
        title: {title}
        ---
        {body}
        ===
        """;

    private string Write(string fileName, string text)
    {
        string path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"), new UTF8Encoding(false));
        return path;
    }

    private string[] Snapshot() => Directory
        .GetFiles(_directory, "*", SearchOption.AllDirectories)
        .Select(Path.GetFileName)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray()!;
}
