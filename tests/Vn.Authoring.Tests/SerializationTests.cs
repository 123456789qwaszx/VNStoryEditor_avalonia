using System.Text;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

public class SerializationTests
{
    [Fact]
    public void 스냅샷은_프로젝트_전체를_한_문자열로_복원한다()
    {
        var sample = new Sample();
        var (_, l1, _, l3, _, _, _) = sample.BuildSpecExample();
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1.Id, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l3.Id, sample.TargetB.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetDefault.Id);
        sample.Editor.MoveNode(sample.Dialogue.Id, 120, 340);

        StoryProject reloaded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(sample.Project));
        StoryFile reloadedFile = Assert.Single(reloaded.Files);
        DialogueNode dialogue = reloaded.FindDialogue(sample.Dialogue.Id)!;

        Assert.Equal(sample.File.Id, reloadedFile.Id);
        Assert.Equal(sample.File.RelativePath, reloadedFile.RelativePath);
        Assert.Equal(sample.Project.EnumerateNodes().Select(node => node.Id), reloaded.EnumerateNodes().Select(node => node.Id));
        Assert.Equal(sample.Dialogue.Lines.Select(line => line.Id), dialogue.Lines.Select(line => line.Id));
        Assert.Equal(sample.TargetA.Id, dialogue.BranchExits[l1.Id]);
        Assert.Equal(sample.TargetB.Id, dialogue.BranchExits[l3.Id]);
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
    public void 스냅샷은_결정적이고_LF이며_한글을_보존한다()
    {
        var sample = new Sample();
        LineBox line = sample.Line("안녕하세요");
        sample.Editor.SetLineText(sample.Dialogue.Id, line.Id, "윌로", "안녕하세요");

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
            Assert.False(loaded.WasMigrated);
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
        var first = new StoryFile("sf_a", "A", "story/a.vnstory.json");
        var second = new StoryFile("sf_b", "B", "story/b.vnstory.json");
        var firstNode = new DialogueNode("nd_a", "A");
        var secondNode = new DialogueNode("nd_b", "B");
        firstNode.Lines.Add(new LineBox("ln_a") { Text = "처음" });
        secondNode.Lines.Add(new LineBox("ln_b") { Text = "고정" });
        first.Nodes.Add(firstNode);
        second.Nodes.Add(secondNode);
        project.Files.Add(first);
        project.Files.Add(second);

        try
        {
            ProjectStore.Save(manifestPath, project);
            string manifestBefore = File.ReadAllText(manifestPath);
            string firstPath = Path.Combine(directory, "story", "a.vnstory.json");
            string secondPath = Path.Combine(directory, "story", "b.vnstory.json");
            string firstBefore = File.ReadAllText(firstPath);
            string secondBefore = File.ReadAllText(secondPath);

            firstNode.Lines[0].Text = "수정";
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

    [Fact]
    public void formatVersion_1은_StoryFile_하나로_마이그레이션되고_새_manifest로_저장된다()
    {
        string directory = TempDirectory();
        string legacyPath = Path.Combine(directory, "legacy.vnstory.json");
        File.WriteAllText(legacyPath, """
            {
              "formatVersion": 1,
              "title": "이전 형식",
              "nodes": [
                { "id": "nd_a", "kind": "dialogue", "name": "A", "lines": [] },
                { "id": "nd_b", "kind": "set", "name": "B" }
              ]
            }
            """, new UTF8Encoding(false));

        try
        {
            ProjectLoadResult loaded = ProjectStore.Load(legacyPath);
            StoryFile file = Assert.Single(loaded.Project.Files);

            Assert.True(loaded.WasMigrated);
            Assert.Equal(Path.Combine(directory, "legacy.vnproject.json"), loaded.ManifestPath);
            Assert.Equal(new[] { "nd_a", "nd_b" }, file.Nodes.Select(node => node.Id));

            ProjectStore.Save(loaded.ManifestPath, loaded.Project);
            Assert.True(File.Exists(loaded.ManifestPath));
            Assert.True(File.Exists(Path.Combine(directory, "story", "sf_main.vnstory.json")));
            Assert.Equal(new[] { "nd_a", "nd_b" }, ProjectStore.Load(loaded.ManifestPath).Project.EnumerateNodes().Select(node => node.Id));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 이전_inline_formatVersion_2도_물리_저장으로_마이그레이션된다()
    {
        string directory = TempDirectory();
        string legacyPath = Path.Combine(directory, "inline.vnstory.json");
        File.WriteAllText(legacyPath, """
            {
              "formatVersion": 2,
              "title": "논리 파일 형식",
              "files": [
                { "id": "sf_a", "name": "A", "nodes": [
                  { "id": "nd_a", "kind": "dialogue", "name": "A", "lines": [] }
                ] }
              ]
            }
            """, new UTF8Encoding(false));

        try
        {
            ProjectLoadResult loaded = ProjectStore.Load(legacyPath);
            Assert.True(loaded.WasMigrated);
            Assert.Equal("story/sf_a.vnstory.json", loaded.Project.Files[0].RelativePath);
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
            { "formatVersion": 2, "files": [
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
