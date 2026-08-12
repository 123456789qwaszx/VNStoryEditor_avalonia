using System.Text;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

public class SerializationTests
{
    [Fact]
    public void 스냅샷은_프로젝트_전체를_한_문자열로_복원한다()
    {
        var sample = new Sample();
        var (_, l1, _, l3, _, _, _) = sample.BuildSpecExample();
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l3, sample.TargetB.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetDefault.Id);
        sample.Editor.MoveNode(sample.Dialogue.Id, 120, 340);

        StoryProject reloaded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(sample.Project));
        StoryFile reloadedFile = Assert.Single(reloaded.Files);
        DialogueNode dialogue = reloaded.FindDialogue(sample.Dialogue.Id)!;

        Assert.Equal(sample.File.Id, reloadedFile.Id);
        Assert.Equal(sample.File.RelativePath, reloadedFile.RelativePath);
        Assert.Equal(sample.Project.EnumerateNodes().Select(node => node.Id), reloaded.EnumerateNodes().Select(node => node.Id));
        Assert.Equal(DialogueScriptResolver.Resolve(sample.Project, sample.Dialogue).Lines.Select(line => line.LineId), DialogueScriptResolver.Resolve(reloaded, dialogue).Lines.Select(line => line.LineId));
        Assert.Equal(sample.TargetA.Id, dialogue.BranchExits[l1]);
        Assert.Equal(sample.TargetB.Id, dialogue.BranchExits[l3]);
        Assert.Equal(sample.TargetDefault.Id, dialogue.DefaultExitTargetNodeId);
        Assert.Equal(120, dialogue.Layout.X);
        Assert.Equal(340, dialogue.Layout.Y);
        NodeLink settings = Assert.Single(reloaded.Links);
        Assert.Equal(NodeLinkKind.Settings, settings.Kind);
        Assert.Equal(sample.SetNode.Id, settings.SourceNodeId);
        Assert.Equal(sample.Dialogue.Id, settings.TargetNodeId);

        DialogueFlow before = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        DialogueFlow after = ConditionFlowResolver.Resolve(dialogue, reloaded);
        Assert.Equal(
            before.Branches.Select(branch => (branch.OpenLineId, branch.ConditionId, branch.ExitTargetNodeId, branch.LastLineIndex)),
            after.Branches.Select(branch => (branch.OpenLineId, branch.ConditionId, branch.ExitTargetNodeId, branch.LastLineIndex)));
    }

    [Fact]
    public void 엑셀_행_신원_맵이_프로젝트와_함께_왕복한다()
    {
        // v4 — 행 신원(인덱스 → LineId)은 대본 파일이 아니라 프로젝트가 갖는다.
        // 저장을 지나도 그대로여야 다음 동기화가 ID 매칭으로 이어진다.
        var sample = new Sample();
        sample.Dialogue.ExcelLineMap[10] = "ln_0001";
        sample.Dialogue.ExcelLineMap[20] = "ln_0002";
        sample.Dialogue.ExcelLineMap[900] = "ln_0100";

        StoryProject reloaded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(sample.Project));
        DialogueNode dialogue = reloaded.FindDialogue(sample.Dialogue.Id)!;

        Assert.Equal(
            new Dictionary<int, string> { [10] = "ln_0001", [20] = "ln_0002", [900] = "ln_0100" },
            dialogue.ExcelLineMap);
    }

    [Fact]
    public void 줄의_변수_변경은_순서와_연산자를_그대로_왕복한다()
    {
        var sample = new Sample();
        string line = sample.Line("보수를 받는다");
        sample.Editor.SetLineSetOperations(sample.Dialogue.Id, line, new[]
        {
            new SetOperation { Variable = "gold", Operator = SetOperatorKind.Add, Value = "30" },
            new SetOperation { Variable = "trust", Operator = SetOperatorKind.Subtract, Value = "1" },
            new SetOperation { Variable = "route", Operator = SetOperatorKind.Assign, Value = "\"b\"" }
        });

        StoryProject reloaded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(sample.Project));
        DialogueLineExtension extension = reloaded.FindDialogue(sample.Dialogue.Id)!
            .FindExtension(line)!;

        Assert.Equal(
            new[]
            {
                ("gold", SetOperatorKind.Add, "30"),
                ("trust", SetOperatorKind.Subtract, "1"),
                ("route", SetOperatorKind.Assign, "\"b\"")
            },
            extension.SetOperations.Select(operation =>
                (operation.Variable, operation.Operator, operation.Value)));
    }

    [Fact]
    public void 연출_노드의_Setup_커맨드는_순서대로_왕복한다()
    {
        var sample = new Sample();
        PresentationNode presentation = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.AddPresentationSetupCommand(
            presentation.Id,
            "char_rig_cast.slot",
            new Dictionary<string, string> { ["layout"] = "2" });
        sample.Editor.AddPresentationSetupCommand(presentation.Id, "char_rig_cast.cast");

        StoryProject reloaded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(sample.Project));
        PresentationNode reloadedNode = reloaded.FindPresentation(presentation.Id)!;

        Assert.Equal(
            presentation.SetupCommands.Select(command => (command.Id, command.DefinitionId)),
            reloadedNode.SetupCommands.Select(command => (command.Id, command.DefinitionId)));
        Assert.Equal("2", reloadedNode.SetupCommands[0].Arguments["layout"]);
    }

    [Fact]
    public void 스냅샷은_결정적이고_LF이며_한글을_보존한다()
    {
        var sample = new Sample();
        string line = sample.Line("안녕하세요");
        sample.Editor.SetScriptLineText(sample.Script.Id, line, "윌로", "안녕하세요");

        string first = ProjectSnapshotCodec.Encode(sample.Project);
        string second = ProjectSnapshotCodec.Encode(sample.Project);

        Assert.Equal(first, second);
        Assert.Contains('\n', first);
        Assert.DoesNotContain('\r', first);
        Assert.Contains("안녕하세요", first, StringComparison.Ordinal);
        Assert.Contains("윌로", first, StringComparison.Ordinal);
    }

    [Fact]
    public void manifest와_StoryFile은_각각_결정적으로_직렬화된다()
    {
        var sample = new Sample();
        sample.BuildSpecExample();

        Assert.Equal(ProjectManifestJson.Write(sample.Project), ProjectManifestJson.Write(sample.Project));
        Assert.Equal(StoryFileJson.Write(sample.File), StoryFileJson.Write(sample.File));
        Assert.DoesNotContain('\r', ProjectManifestJson.Write(sample.Project));
        Assert.DoesNotContain('\r', StoryFileJson.Write(sample.File));
    }

    [Fact]
    public void 프로젝트를_manifest와_파일별_StoryFile로_저장하고_읽는다()
    {
        string directory = TempDirectory();
        string manifestPath = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);

        try
        {
            var project = new StoryProject { Title = "여러 파일" };
            var first = new StoryFile("sf_a", "A", "story/a.vnstory.json");
            var second = new StoryFile("sf_b", "B", "story/b.vnstory.json");
            project.Files.Add(first);
            project.Files.Add(second);
            first.Nodes.Add(new DialogueNode("nd_a", "A 노드"));
            second.Nodes.Add(new SetNode("nd_b", "B 노드"));
            project.Links.Add(new NodeLink(
                "lk_b_a",
                NodeLinkKind.Settings,
                sourceNodeId: "nd_b",
                targetNodeId: "nd_a"));
            project.StartNodeId = "nd_a";

            ProjectStore.Save(manifestPath, project);

            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(Path.Combine(directory, "story", "a.vnstory.json")));
            Assert.True(File.Exists(Path.Combine(directory, "story", "b.vnstory.json")));
            string manifest = File.ReadAllText(manifestPath);
            string firstStory = File.ReadAllText(Path.Combine(directory, "story", "a.vnstory.json"));
            Assert.DoesNotContain("\"nodes\"", manifest);
            Assert.Contains("\"links\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"nodes\"", firstStory, StringComparison.Ordinal);
            Assert.DoesNotContain("\"links\"", firstStory);

            ProjectLoadResult loaded = ProjectStore.Load(manifestPath);
            Assert.Equal(new[] { "sf_a", "sf_b" }, loaded.Project.Files.Select(file => file.Id));
            Assert.Equal(new[] { "nd_a", "nd_b" }, loaded.Project.EnumerateNodes().Select(node => node.Id));
            Assert.Equal("story/b.vnstory.json", loaded.Project.Files[1].RelativePath);
            NodeLink loadedLink = Assert.Single(loaded.Project.Links);
            Assert.Equal("lk_b_a", loadedLink.Id);
            Assert.Equal("nd_b", loadedLink.SourceNodeId);
            Assert.Equal("nd_a", loadedLink.TargetNodeId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 저장은_BOM이_없고_tmp를_남기지_않는다()
    {
        string directory = TempDirectory();
        string manifestPath = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);
        var sample = new Sample();

        try
        {
            ProjectStore.Save(manifestPath, sample.Project);

            Assert.NotEqual(0xEF, File.ReadAllBytes(manifestPath)[0]);
            string storyPath = Path.Combine(directory, sample.File.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.NotEqual(0xEF, File.ReadAllBytes(storyPath)[0]);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 한_StoryFile만_수정하면_manifest와_다른_StoryFile은_같다()
    {
        string directory = TempDirectory();
        string manifestPath = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);
        var project = new StoryProject { Title = "부분 변경" };
        var firstScript = new ScriptDocument("sc_a", "A 대본");
        firstScript.Lines.Add(new ScriptLine("ln_a"));
        firstScript.RequireLocale(firstScript.PrimaryLocale).Entries["ln_a"] = new LocalizedLine("", "처음");
        var secondScript = new ScriptDocument("sc_b", "B 대본");
        secondScript.Lines.Add(new ScriptLine("ln_b"));
        secondScript.RequireLocale(secondScript.PrimaryLocale).Entries["ln_b"] = new LocalizedLine("", "고정");
        project.Scripts.Add(firstScript);
        project.Scripts.Add(secondScript);

        var first = new StoryFile("sf_a", "A", "story/a.vnstory.json");
        var second = new StoryFile("sf_b", "B", "story/b.vnstory.json");
        first.Nodes.Add(new DialogueNode("nd_a", "A") { ScriptId = "sc_a" });
        second.Nodes.Add(new DialogueNode("nd_b", "B") { ScriptId = "sc_b" });
        project.Files.Add(first);
        project.Files.Add(second);

        try
        {
            ProjectStore.Save(manifestPath, project);
            string manifestBefore = File.ReadAllText(manifestPath);
            string firstPath = Path.Combine(directory, "script", "sc_a.vnscript.json");
            string secondPath = Path.Combine(directory, "script", "sc_b.vnscript.json");
            string firstBefore = File.ReadAllText(firstPath);
            string secondBefore = File.ReadAllText(secondPath);

            firstScript.Locales[0].Entries["ln_a"] = new LocalizedLine("", "수정");
            ProjectStore.Save(manifestPath, project);

            Assert.Equal(manifestBefore, File.ReadAllText(manifestPath));
            Assert.NotEqual(firstBefore, File.ReadAllText(firstPath));
            Assert.Equal(secondBefore, File.ReadAllText(secondPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 버전 2 이하는 화자·대사를 대사 노드가 직접 소유했고 연출이 편집 중인 노드를 실시간으로
    /// 읽었다. 그 데이터를 새 의미로 자동 해석하면 어느 문장이 어느 LineId인지 도구가 임의로
    /// 정하게 된다. <b>덮어써서 원고를 잃는 것보다 열지 않는 편이 낫다.</b>
    ///
    /// 이전에 있던 두 마이그레이션 테스트를 이 테스트로 교체했다.
    /// 그 경로는 이제 존재하지 않으며, 존재하지 않아야 한다는 것이 새 불변 조건이다.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void 이전_형식_프로젝트는_열지_않고_이유를_알린다(int version)
    {
        string json = $$"""
            {
              "formatVersion": {{version}},
              "title": "이전 형식",
              "files": []
            }
            """;

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => ProjectManifestJson.Read(json));

        Assert.Contains("더 이상 열 수 없습니다", error.Message, StringComparison.Ordinal);
        Assert.Contains("대본을 가져오세요", error.Message, StringComparison.Ordinal);
    }

    /// <summary>이전 형식 파일을 열려다 실패해도 그 파일은 그대로 남아 있어야 한다.</summary>
    [Fact]
    public void 이전_형식_파일을_열지_못해도_내용은_그대로다()
    {
        string directory = TempDirectory();
        string legacyPath = Path.Combine(directory, "legacy.vnproject.json");
        const string original = """
            { "formatVersion": 2, "title": "이전 형식", "files": [] }
            """;
        File.WriteAllText(legacyPath, original, new UTF8Encoding(false));

        try
        {
            Assert.Throws<InvalidDataException>(() => ProjectStore.Load(legacyPath));
            Assert.Equal(original, File.ReadAllText(legacyPath, new UTF8Encoding(false)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 개별_대본_파일을_프로젝트로_잘못_열지_않는다()
    {
        string directory = TempDirectory();
        string scriptPath = Path.Combine(directory, "chapter.vnscript.json");
        File.WriteAllText(scriptPath, """
            { "formatVersion": 1, "scriptId": "sc_chapter", "name": "1장", "lines": [], "locales": [] }
            """);

        try
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => ProjectStore.Load(scriptPath));
            Assert.Contains("manifest", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 개별_StoryFile을_프로젝트로_잘못_열지_않는다()
    {
        string directory = TempDirectory();
        string storyPath = Path.Combine(directory, "chapter.vnstory.json");
        File.WriteAllText(storyPath, """
            { "formatVersion": 1, "fileId": "sf_chapter", "name": "1장", "nodes": [] }
            """);

        try
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(() => ProjectStore.Load(storyPath));
            Assert.Contains("manifest", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void manifest의_파일_Id와_실제_StoryFile_Id가_다르면_열지_않는다()
    {
        string directory = TempDirectory();
        Directory.CreateDirectory(Path.Combine(directory, "story"));
        string manifestPath = Path.Combine(directory, "project.vnproject.json");
        File.WriteAllText(manifestPath, """
            { "formatVersion": 3, "scripts": [], "files": [
              { "id": "sf_manifest", "name": "A", "path": "story/a.vnstory.json" }
            ] }
            """);
        File.WriteAllText(Path.Combine(directory, "story", "a.vnstory.json"), """
            { "formatVersion": 1, "fileId": "sf_actual", "name": "A", "nodes": [] }
            """);

        try
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(() => ProjectStore.Load(manifestPath));
            Assert.Contains("다릅니다", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside.vnstory.json")]
    [InlineData("story/../outside.vnstory.json")]
    [InlineData("/root/outside.vnstory.json")]
    public void StoryFile_경로는_프로젝트_밖을_가리킬_수_없다(string relativePath)
    {
        var project = new StoryProject();
        project.Files.Add(new StoryFile("sf_a", "A", relativePath));

        Assert.Throws<InvalidDataException>(() => ProjectManifestJson.Write(project));
    }

    [Fact]
    public void 파일이_달라도_노드_Id가_중복되면_저장하지_않는다()
    {
        var project = new StoryProject();
        var first = new StoryFile("sf_a", "A");
        var second = new StoryFile("sf_b", "B");
        first.Nodes.Add(new DialogueNode("nd_same", "A"));
        second.Nodes.Add(new SetNode("nd_same", "B"));
        project.Files.Add(first);
        project.Files.Add(second);

        Assert.Throws<InvalidDataException>(() => ProjectSnapshotCodec.Encode(project));
        Assert.Throws<InvalidDataException>(() => ProjectManifestJson.Write(project));
    }

    [Fact]
    public void Settings_link의_노드_종류가_잘못되면_저장하지_않는다()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_link", "링크");
        var first = new DialogueNode("nd_first", "첫 장면");
        var second = new DialogueNode("nd_second", "둘째 장면");
        file.Nodes.Add(first);
        file.Nodes.Add(second);
        project.Files.Add(file);
        project.Links.Add(new NodeLink(
            "lk_invalid",
            NodeLinkKind.Settings,
            sourceNodeId: first.Id,
            targetNodeId: second.Id));

        Assert.Throws<InvalidDataException>(() => ProjectSnapshotCodec.Encode(project));
        Assert.Throws<InvalidDataException>(() => ProjectManifestJson.Write(project));
    }

    [Fact]
    public void 같은_Settings_link가_중복되면_저장하지_않는다()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_link", "링크");
        var setNode = new SetNode("nd_set", "설정");
        var dialogue = new DialogueNode("nd_dialogue", "장면");
        file.Nodes.Add(setNode);
        file.Nodes.Add(dialogue);
        project.Files.Add(file);
        project.Links.Add(new NodeLink("lk_one", NodeLinkKind.Settings, setNode.Id, dialogue.Id));
        project.Links.Add(new NodeLink("lk_two", NodeLinkKind.Settings, setNode.Id, dialogue.Id));

        Assert.Throws<InvalidDataException>(() => ProjectSnapshotCodec.Encode(project));
    }

    [Fact]
    public void 앞으로의_manifest_형식_버전은_열지_않는다()
    {
        string json = $$"""{ "formatVersion": {{ProjectManifestJson.CurrentFormatVersion + 1}}, "files": [] }""";
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ProjectManifestJson.Read(json));
        Assert.Contains("형식 버전", error.Message, StringComparison.Ordinal);
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Serialization.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
