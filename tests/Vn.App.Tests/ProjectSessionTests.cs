using Vn.App.Services;

namespace Vn.App.Tests;

public class ProjectSessionTests
{
    [Fact]
    public async Task 같은_파일의_다른_노드로_이동해도_편집_상태가_유지된다()
    {
        string project = Path.GetFullPath("../../../../../samples/Valid/Demo.yarnproject");
        var session = new ProjectSession();

        await session.OpenProjectAsync(project);

        Assert.NotNull(session.Document);
        session.SetWorkingText(
            session.Document.WorkingText.Replace("어서 오세요.", "다시 오셨군요.", StringComparison.Ordinal));

        Assert.True(session.HasUnsavedChanges);
        Assert.True(session.SelectNode("Ending"));
        Assert.True(session.HasUnsavedChanges);
        Assert.Contains("다시 오셨군요.", session.Document.WorkingText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 분석_뒤_최근_노드와_문서를_함께_선택한다()
    {
        string project = Path.GetFullPath("../../../../../samples/Valid/Demo.yarnproject");
        var session = new ProjectSession();

        await session.OpenProjectAsync(project);

        Assert.NotNull(session.Report);
        Assert.NotNull(session.SelectedNode);
        Assert.NotNull(session.Document);
        Assert.Equal(session.SelectedNode.FilePath, session.Document.Path);
    }
}
