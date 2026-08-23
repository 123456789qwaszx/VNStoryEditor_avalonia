using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 챕터 판 보장 — 2026-08-23에 `AuthoringSession`에서 `ProjectEditor`로 내려온 규칙.
///
/// 내려온 이유는 이 파일이 존재한다는 사실 자체다: 그 함수는 <b>세션 상태를 하나도 안
/// 봤는데</b> 셸에 살아서 UI 없이는 붙들 수 없었다. 그래서 에피소드 동기화를 화면 밖으로
/// 꺼내려던 시도가 여기서 막혔다 — 도메인이 셸의 메서드를 불러야 했기 때문이다.
/// </summary>
public sealed class ChapterBoardEditorTests
{
    [Fact]
    public void 없으면_판을_만들고_설정_노드까지_세운다()
    {
        var editor = new ProjectEditor(new StoryProject { Title = "판" });

        string fileId = editor.EnsureChapterBoard("ch01");

        StoryFile board = editor.Project.FindFile(fileId)!;
        Assert.Equal("ch01", board.Name);

        // 설정 노드는 챕터에 딸린 자리다 — 작가가 만들고 지우는 것이 아니다.
        SetNode settings = Assert.Single(board.Nodes.OfType<SetNode>());
        Assert.Equal(ProjectEditor.ChapterSettingsNodeName("ch01"), settings.Name);
    }

    [Fact]
    public void 이미_있으면_그대로_돌려준다()
    {
        // 왼쪽 챕터 목록 클릭과 에피소드 동기화가 같은 규칙 하나를 쓴다 — 두 번째
        // 부름이 판을 또 만들면 같은 챕터가 판 둘을 갖는다.
        var editor = new ProjectEditor(new StoryProject { Title = "판" });

        string first = editor.EnsureChapterBoard("ch01");
        string second = editor.EnsureChapterBoard("ch01");

        Assert.Equal(first, second);
        Assert.Single(editor.Project.Files);
        Assert.Single(editor.Project.FindFile(first)!.Nodes.OfType<SetNode>());
    }

    [Fact]
    public void 챕터마다_판이_따로다()
    {
        var editor = new ProjectEditor(new StoryProject { Title = "판" });

        string ch01 = editor.EnsureChapterBoard("ch01");
        string ch02 = editor.EnsureChapterBoard("ch02");

        Assert.NotEqual(ch01, ch02);
        Assert.Equal(2, editor.Project.Files.Count);
    }

    [Fact]
    public void 사람이_만든_같은_이름의_판을_다시_쓴다()
    {
        // 판을 먼저 만들어 두고 챕터를 뒤에 더하는 순서가 실재한다. 그때 판을 하나 더
        // 만들면 작가가 이미 적은 노드들이 안 보이는 판에 남는다.
        var editor = new ProjectEditor(new StoryProject { Title = "판" });
        StoryFile made = editor.AddStoryFile("ch01");

        Assert.Equal(made.Id, editor.EnsureChapterBoard("ch01"));
        Assert.Single(editor.Project.Files);
    }
}
