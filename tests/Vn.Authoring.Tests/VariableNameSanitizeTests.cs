using Vn.Authoring.Model;
using Vn.Authoring.Rendering;

namespace Vn.Authoring.Tests;

/// <summary>
/// 변수 이름에 공백이 못 들어간다 (2026-08-25).
///
/// <b>Yarn 식별자의 제약이다.</b> 그대로 나가면 <c>&lt;&lt;set $능력이 바뀌 += 4&gt;&gt;</c>가
/// 되어 파서가 거기서 끊기고, 그 노드만이 아니라 <b>번들 전체가</b> 컴파일에 실패한다.
///
/// 입력과 출력 양쪽에 건다 — 입력에서 막으면 새로 안 생기고, 출력에서 걸면
/// <b>이미 저장된 프로젝트</b>도 그날로 컴파일된다.
/// </summary>
public sealed class VariableNameSanitizeTests
{
    [Theory]
    [InlineData("능력이 바뀌", "능력이_바뀌")]
    [InlineData("$능력이 바뀌", "능력이_바뀌")]
    [InlineData("  앞뒤 공백  ", "앞뒤_공백")]
    [InlineData("점.과-하이픈", "점_과_하이픈")]
    [InlineData("추리1", "추리1")]
    [InlineData("__ch_2", "__ch_2")]
    public void 문자_숫자_밑줄만_남는다(string input, string expected) =>
        Assert.Equal(expected, YarnSyntax.SanitizeVariableName(input));

    [Fact]
    public void 빈_이름은_지어내지_않는다() =>
        // 이름이 없는 것은 발행 검증이 따로 막는다 — 여기서 무언가를 발명하면 그 오류가 숨는다.
        Assert.Equal(string.Empty, YarnSyntax.SanitizeVariableName("   "));

    [Fact]
    public void 설정_노드에_적으면_그_자리에서_다듬어진다()
    {
        // 쓰는 자리가 하나다 — 편집 UI·CLI·테스트가 전부 이 함수를 지난다.
        var sample = new Sample();

        sample.Editor.SetAssignments(sample.SetNode.Id,
        [
            new VariableAssignment { Variable = "능력이 바뀌", Value = "0" }
        ]);

        VariableAssignment stored = Assert.Single(sample.SetNode.Assignments);

        Assert.Equal("능력이_바뀌", stored.Variable);
    }

    [Fact]
    public void 이미_저장된_이름도_내보낼_때_다듬어진다()
    {
        // 입력 쪽만 막으면 어제 저장한 프로젝트는 그대로 깨진 채 나간다.
        // 접두를 붙이는 갈래든 아니든 같은 규칙을 지난다.
        var statNames = new HashSet<string>(StringComparer.Ordinal) { "스탯 이름" };

        Assert.Equal(
            "__t1_판_능력이_바뀌",
            Tier1Namespace.Apply("능력이 바뀌", "__t1_판_", statNames));

        // A계층 스탯은 접두를 안 받지만 이름은 그대로 다듬어진다 — 새는 갈래가 없다.
        Assert.Equal("스탯_이름", Tier1Namespace.Apply("스탯 이름", "__t1_판_", statNames));
    }

    [Fact]
    public void 공백_든_이름을_쓴_노드가_유효한_yarn을_낸다()
    {
        // ⛔ 이것이 고친 버그다. 산출물이 Yarn 문법 오류로 통째로 컴파일에 실패했다.
        var sample = new Sample();
        string lineId = sample.Line("한 줄");

        sample.Editor.SetLineSetOperations(sample.Dialogue.Id, lineId,
        [
            new SetOperation { Variable = "능력이 바뀌", Operator = SetOperatorKind.Add, Value = "4" }
        ]);

        YarnBundle bundle = YarnBundleEmitter.Emit(
            sample.Editor.PublishDialogue(sample.Dialogue.Id).Result,
            project: sample.Project,
            definition: Sample.Definition);

        Assert.DoesNotContain("능력이 바뀌", bundle.StoryText);
        Assert.Contains("능력이_바뀌", bundle.StoryText);
    }
}
