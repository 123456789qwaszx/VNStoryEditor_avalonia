using Vn.App.Services;
using Vn.Authoring.Model;

namespace Vn.App.Tests;

/// <summary>
/// 챕터 = 판 1:1 (G-1 v2). 왼쪽 챕터 목록 클릭과 에피소드 동기화가 같은 규칙 하나를 쓴다 —
/// 이름이 챕터 Id와 같은 StoryFile이 그 챕터의 판이고, 없으면 만든다.
/// </summary>
public class ChapterBoardTests
{
    [Fact]
    public void 챕터의_판은_이름으로_찾고_없으면_만든다()
    {
        var session = new AuthoringSession();
        int before = session.Project.Files.Count;

        string boardId = session.EnsureChapterBoard("ch05");

        StoryFile board = session.Project.Files.Single(file => file.Id == boardId);
        Assert.Equal("ch05", board.Name);
        Assert.Equal(before + 1, session.Project.Files.Count);

        // 두 번째 호출은 같은 판을 돌려준다 — 판이 늘지 않는다.
        Assert.Equal(boardId, session.EnsureChapterBoard("ch05"));
        Assert.Equal(before + 1, session.Project.Files.Count);

        // 다른 챕터는 다른 판이다.
        Assert.NotEqual(boardId, session.EnsureChapterBoard("ch06"));
    }
}
