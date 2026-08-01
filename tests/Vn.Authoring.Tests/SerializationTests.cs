using System.Text;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

public class SerializationTests
{
    [Fact]
    public void 저장하고_다시_열면_같은_구조가_복원된다()
    {
        var sample = new Sample();
        var (_, l1, _, l3, _, _, _) = sample.BuildSpecExample();

        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1.Id, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l3.Id, sample.TargetB.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetDefault.Id);
        sample.Editor.MoveNode(sample.Dialogue.Id, 120, 340);

        StoryProject reloaded = ProjectJson.Read(ProjectJson.Write(sample.Project));

        Assert.Equal(StoryProject.CurrentFormatVersion, reloaded.FormatVersion);
        StoryFile reloadedFile = Assert.Single(reloaded.Files);
        Assert.Equal(sample.File.Id, reloadedFile.Id);
        Assert.Equal(sample.File.Name, reloadedFile.Name);

        // 노드와 파일 순서
        Assert.Equal(
            sample.Project.EnumerateNodes().Select(node => node.Id),
            reloaded.EnumerateNodes().Select(node => node.Id));

        DialogueNode dialogue = reloaded.FindDialogue(sample.Dialogue.Id)!;

        // 줄과 그 정체성
        Assert.Equal(
            sample.Dialogue.Lines.Select(line => line.Id),
            dialogue.Lines.Select(line => line.Id));

        // 조건 전환
        Assert.Equal(ConditionTransitionKind.BeginIf, dialogue.Lines[1].Transition!.Kind);
        Assert.Equal(sample.ConditionA.Id, dialogue.Lines[1].Transition!.ConditionId);
        Assert.Equal(ConditionTransitionKind.BeginElseIf, dialogue.Lines[3].Transition!.Kind);
        Assert.Equal(ConditionTransitionKind.EndIf, dialogue.Lines[5].Transition!.Kind);
        Assert.Null(dialogue.Lines[5].Transition!.ConditionId);

        // 출구
        Assert.Equal(sample.TargetA.Id, dialogue.BranchExits[l1.Id]);
        Assert.Equal(sample.TargetB.Id, dialogue.BranchExits[l3.Id]);
        Assert.Equal(sample.TargetDefault.Id, dialogue.DefaultExitTargetNodeId);

        // 그래프 좌표
        Assert.Equal(120, dialogue.Layout.X);
        Assert.Equal(340, dialogue.Layout.Y);

        // 계산 결과까지 같은지 확인한다. 파일이 아니라 의미가 같아야 한다.
        DialogueFlow before = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        DialogueFlow after = ConditionFlowResolver.Resolve(dialogue, reloaded);

        Assert.Equal(
            before.Branches.Select(branch => (branch.OpenLineId, branch.ConditionId, branch.ExitTargetNodeId, branch.LastLineIndex)),
            after.Branches.Select(branch => (branch.OpenLineId, branch.ConditionId, branch.ExitTargetNodeId, branch.LastLineIndex)));

        Assert.Equal(
            before.Lines.Select(line => (line.Line.Id, line.Depth, line.IsBranchExit)),
            after.Lines.Select(line => (line.Line.Id, line.Depth, line.IsBranchExit)));
    }

    [Fact]
    public void 조건_정의도_함께_복원된다()
    {
        var sample = new Sample();

        StoryProject reloaded = ProjectJson.Read(ProjectJson.Write(sample.Project));
        List<ConditionDefinition> conditions = reloaded.EnumerateConditions().ToList();

        Assert.Equal(2, conditions.Count);
        Assert.Equal("호감 높음", conditions[0].Name);
        Assert.Equal("favor >= 5", conditions[0].Expression);
        Assert.Equal(sample.ConditionA.Id, conditions[0].Id);
    }

    [Fact]
    public void 설정_노드의_변수_지정도_복원된다()
    {
        var sample = new Sample();
        sample.Editor.SetAssignments(sample.SetNode.Id, new[]
        {
            new VariableAssignment { Variable = "favor", Value = "0" },
            new VariableAssignment { Variable = "route", Value = "red" }
        });

        var reloaded = (SetNode)ProjectJson.Read(ProjectJson.Write(sample.Project))
            .FindNode(sample.SetNode.Id)!;

        Assert.Equal(2, reloaded.Assignments.Count);
        Assert.Equal("route", reloaded.Assignments[1].Variable);
        Assert.Equal("red", reloaded.Assignments[1].Value);
    }

    [Fact]
    public void 두_번_저장하면_같은_바이트가_나온다()
    {
        var sample = new Sample();
        sample.BuildSpecExample();

        // diff가 편집한 곳에만 뜨려면 같은 상태가 언제나 같은 문자열이 되어야 한다.
        Assert.Equal(ProjectJson.Write(sample.Project), ProjectJson.Write(sample.Project));
    }

    [Fact]
    public void 줄바꿈은_LF로_고정된다()
    {
        var sample = new Sample();
        sample.BuildSpecExample();

        string json = ProjectJson.Write(sample.Project);

        Assert.Contains('\n', json);
        Assert.DoesNotContain('\r', json);
    }

    [Fact]
    public void 한글은_이스케이프하지_않는다()
    {
        var sample = new Sample();
        LineBox line = sample.Line("안녕하세요");
        sample.Editor.SetLineText(sample.Dialogue.Id, line.Id, "윌로", "안녕하세요");

        string json = ProjectJson.Write(sample.Project);

        // 사람이 diff에서 읽을 수 있어야 한다.
        Assert.Contains("안녕하세요", json, StringComparison.Ordinal);
        Assert.Contains("윌로", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 파일로_저장하고_읽을_수_있다()
    {
        var sample = new Sample();
        sample.BuildSpecExample();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"VnTool.Serialization.{Guid.NewGuid():N}",
            "story" + ProjectJson.FileExtension);

        try
        {
            ProjectJson.Save(path, sample.Project);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.NotEqual(0xEF, bytes[0]); // BOM 없는 UTF-8

            StoryProject loaded = ProjectJson.Load(path);
            Assert.Equal(sample.Project.EnumerateNodes().Count(), loaded.EnumerateNodes().Count());
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);

            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void 앞으로의_형식_버전은_열지_않고_알린다()
    {
        string json = $$"""{ "formatVersion": {{StoryProject.CurrentFormatVersion + 1}}, "nodes": [] }""";

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ProjectJson.Read(json));
        Assert.Contains("형식 버전", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 관대하게 읽으면 다른 도구의 JSON이 "노드 0개짜리 프로젝트"로 열리고,
    /// 작가가 저장을 누르는 순간 원본이 빈 프로젝트로 덮어써진다.
    /// 열리지 않는 것은 되돌릴 수 있지만 덮어써진 원고는 되돌릴 수 없다.
    /// </summary>
    [Theory]
    // Yarn 프로젝트 파일. 예전 VnTool이 열던 것이다.
    [InlineData("""{ "projectFileVersion": 3, "baseLanguage": "ko", "sourceFiles": [ "**/*.yarn" ] }""")]
    // 게임 스키마 파일.
    [InlineData("""{ "schemaVersion": 1, "variables": [], "commands": [] }""")]
    // nodes가 배열이 아니다.
    [InlineData("""{ "formatVersion": 1, "nodes": {} }""")]
    // formatVersion이 없다.
    [InlineData("""{ "title": "무엇인가", "nodes": [] }""")]
    public void VnTool_프로젝트가_아니면_열지_않는다(string json)
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ProjectJson.Read(json));
        Assert.Contains("VnTool 프로젝트", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 버전1의_평면_nodes는_하나의_StoryFile로_승격된다()
    {
        const string json = """
            {
              "formatVersion": 1,
              "title": "이전 형식",
              "nodes": [
                { "id": "nd_a", "kind": "dialogue", "name": "A", "lines": [] },
                { "id": "nd_b", "kind": "set", "name": "B" }
              ]
            }
            """;

        StoryProject project = ProjectJson.Read(json);
        StoryFile file = Assert.Single(project.Files);

        Assert.Equal(StoryProject.CurrentFormatVersion, project.FormatVersion);
        Assert.Equal(new[] { "nd_a", "nd_b" }, file.Nodes.Select(node => node.Id));
        string upgraded = ProjectJson.Write(project);
        Assert.Contains("\"files\"", upgraded, StringComparison.Ordinal);
        Assert.True(
            upgraded.IndexOf("\"files\"", StringComparison.Ordinal) <
            upgraded.IndexOf("\"nodes\"", StringComparison.Ordinal));
    }

    [Fact]
    public void 여러_StoryFile의_순서와_노드_소유가_왕복된다()
    {
        var project = new StoryProject { Title = "여러 파일" };
        var first = new StoryFile("sf_a", "A");
        var second = new StoryFile("sf_b", "B");
        project.Files.Add(first);
        project.Files.Add(second);
        first.Nodes.Add(new DialogueNode("nd_a", "A 노드"));
        second.Nodes.Add(new SetNode("nd_b", "B 노드"));
        project.StartNodeId = "nd_a";

        StoryProject loaded = ProjectJson.Read(ProjectJson.Write(project));

        Assert.Equal(new[] { "sf_a", "sf_b" }, loaded.Files.Select(file => file.Id));
        Assert.Equal(new[] { "nd_a" }, loaded.Files[0].Nodes.Select(node => node.Id));
        Assert.Equal(new[] { "nd_b" }, loaded.Files[1].Nodes.Select(node => node.Id));
    }

    [Fact]
    public void 파일이_달라도_노드_Id가_중복되면_읽지_않는다()
    {
        const string json = """
            {
              "formatVersion": 2,
              "files": [
                { "id": "sf_a", "name": "A", "nodes": [
                  { "id": "nd_same", "kind": "dialogue", "name": "A", "lines": [] }
                ] },
                { "id": "sf_b", "name": "B", "nodes": [
                  { "id": "nd_same", "kind": "set", "name": "B" }
                ] }
              ]
            }
            """;

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ProjectJson.Read(json));
        Assert.Contains("중복", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JSON이_아니면_읽을_수_없다고_알린다()
    {
        Assert.Throws<InvalidDataException>(() => ProjectJson.Read("이건 JSON이 아니다"));
    }

    [Fact]
    public void 사람이_손으로_쓴_최소한의_파일도_읽는다()
    {
        const string json = """
            {
              "formatVersion": 1,
              "title": "손으로 쓴 것",
              "nodes": [
                { "id": "nd_a", "kind": "dialogue", "name": "장면",
                  "lines": [ { "id": "ln_1", "text": "안녕" } ] }
              ]
            }
            """;

        StoryProject project = ProjectJson.Read(json);
        DialogueNode node = Assert.IsType<DialogueNode>(Assert.Single(project.EnumerateNodes()));

        Assert.Equal("장면", node.Name);
        Assert.Equal("안녕", node.Lines[0].Text);
        Assert.Equal(string.Empty, node.Lines[0].Speaker);
        Assert.Null(node.Lines[0].Transition);
    }
}
