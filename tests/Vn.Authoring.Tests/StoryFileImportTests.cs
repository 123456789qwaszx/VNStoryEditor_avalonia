using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// W51 — 다른 프로젝트의 .vnstory.json 하나를 판으로 들여온다.
/// 딸린 대본은 관례 위치에서 함께 오고, 못 찾으면 경고로 남으며,
/// 같은 Id의 재수입은 명확히 거부된다(Id 재발급은 연결·LineId 계약을 끊는다).
/// </summary>
public class StoryFileImportTests
{
    private static (string Directory, StoryFile File, string ScriptId) SaveSourceProject()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Import.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var source = new ProjectEditor(new StoryProject { Title = "원본" });
        StoryFile file = source.AddStoryFile("1장");
        DialogueNode node = source.AddDialogueNode(file.Id, name: "장면");
        ProjectStore.Save(Path.Combine(directory, "project.vnproject.json"), source.Project);

        return (directory, file, node.ScriptId!);
    }

    [Fact]
    public void 스토리_파일을_대본과_함께_들여오고_재수입은_거부한다()
    {
        (string directory, StoryFile sourceFile, string scriptId) = SaveSourceProject();

        try
        {
            var editor = new ProjectEditor(new StoryProject());
            string storyPath = Path.Combine(
                directory, "story", $"{sourceFile.Id}{StoryFileJson.FileExtension}");

            StoryFileImportOutcome outcome = editor.ImportStoryFile(storyPath);

            Assert.Equal(sourceFile.Id, outcome.File.Id);
            Assert.Equal(1, outcome.ImportedScripts);
            Assert.Empty(outcome.Warnings);
            Assert.NotNull(editor.Project.FindFile(sourceFile.Id));
            Assert.NotNull(editor.Project.FindScript(scriptId)); // 대사 본문이 함께 왔다
            Assert.Equal(
                $"story/{sourceFile.Id}{StoryFileJson.FileExtension}",
                editor.Project.FindFile(sourceFile.Id)!.RelativePath); // 이 프로젝트의 관례 자리

            Assert.Throws<InvalidOperationException>(() => editor.ImportStoryFile(storyPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 대본을_못_찾으면_경고로_남기고_파일은_들어온다()
    {
        (string directory, StoryFile sourceFile, string scriptId) = SaveSourceProject();

        try
        {
            File.Delete(Path.Combine(
                directory, "script", $"{scriptId}{ScriptDocumentJson.FileExtension}"));

            var editor = new ProjectEditor(new StoryProject());
            StoryFileImportOutcome outcome = editor.ImportStoryFile(Path.Combine(
                directory, "story", $"{sourceFile.Id}{StoryFileJson.FileExtension}"));

            Assert.Equal(0, outcome.ImportedScripts);
            string warning = Assert.Single(outcome.Warnings);
            Assert.Contains(scriptId, warning); // 무엇을 못 찾았는지 이름이 남는다
            Assert.NotNull(editor.Project.FindFile(sourceFile.Id));
            Assert.Null(editor.Project.FindScript(scriptId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
