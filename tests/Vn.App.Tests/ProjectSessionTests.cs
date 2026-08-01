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

    /// <summary>
    /// 최근 프로젝트 복원은 창이 열린 직후의 async void 핸들러에서 일어난다.
    /// 여기서 예외가 새면 잡아 줄 곳이 없어 창째로 앱이 사라진다.
    /// 설정에 남은 경로가 형식부터 깨져 있어도 세션은 계속 살아 있어야 한다.
    /// </summary>
    [Fact]
    public async Task 경로가_깨져_있어도_세션이_죽지_않는다()
    {
        var session = new ProjectSession();

        await session.OpenProjectAsync("깨진\0경로.yarnproject");

        Assert.False(session.HasProject);
        Assert.NotEmpty(session.StatusMessage);
    }

    [Fact]
    public async Task 복원에_실패한_뒤에도_다른_프로젝트를_열_수_있다()
    {
        var session = new ProjectSession();

        await session.OpenProjectAsync("깨진\0경로.yarnproject");
        await session.OpenProjectAsync(
            Path.GetFullPath("../../../../../samples/Valid/Demo.yarnproject"));

        Assert.True(session.HasProject);
        Assert.NotNull(session.Report);
        Assert.NotNull(session.Document);
    }

    /// <summary>
    /// 분석이 통째로 실패해도 원고는 계속 열려 있어야 한다.
    /// 오래된 분석 결과나 검사 실패가 편집을 막으면 작가는 손쓸 방법이 없다.
    /// </summary>
    [Fact]
    public async Task 분석이_실패해도_원문은_계속_열_수_있다()
    {
        string project = Path.GetFullPath("../../../../../samples/Valid/Demo.yarnproject");

        var session = new ProjectSession(
            (_, _) => throw new InvalidOperationException("컴파일러가 죽었다"));

        await session.OpenProjectAsync(project);

        Assert.True(session.HasProject);
        Assert.Null(session.Report);
        Assert.NotNull(session.Document);
        Assert.Contains("계속 수정할 수 있습니다", session.StatusMessage, StringComparison.Ordinal);
    }
}
