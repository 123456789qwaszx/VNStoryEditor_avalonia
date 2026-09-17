using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>「그 에피소드의 카드」를 찾는 자리는 하나다</b> (2026-09-18 소유자: *"괜히 복제해서
/// 쓰면서 버그 일으키는 지점들을 점검해서"*).
///
/// ⛔ <b>네 벌이었고 서로 답이 달랐다.</b> 대본 탭·개명·삭제는 <i>표식이 없으면 이름</i>을
/// 뒷길로 썼는데 <b>무대 프리뷰만 표식으로만</b> 찾았다 — 표식 없는 카드에서 저쪽 셋은
/// 찾고 프리뷰만 못 찾아 <i>"아직 판에 노드가 없습니다"</i>로 멈췄다.
///
/// ⚠ 「판 찾기」도 <b>아홉 곳</b>에 복사돼 있었다. 한 줄짜리라 복사가 쉬웠고, 쉬운 만큼
/// 갈릴 자리였다.
/// </summary>
public sealed class EpisodeNamingLookupTests
{
    [Fact]
    public void 표식이_있으면_이름이_달라도_찾는다()
    {
        // 임포터가 세우는 모양 — 카드 이름은 `대사엔트리`이고 표식이 EpisodeId를 진다.
        (ProjectEditor editor, StoryFile board) = World();

        var card = new DialogueNode(name: "복도에서") { MarkedEpisodeId = "ep01" };
        board.Nodes.Add(card);

        Assert.Same(card, EpisodeNaming.CardFor(editor.Project, "ch01", "ep01"));
    }

    [Fact]
    public void 표식이_없으면_이름을_뒷길로_쓴다()
    {
        // ⛔ <b>프리뷰만 이 갈래를 안 탔다.</b> 표식은 구판 프로젝트에 없을 수 있고,
        //    그때 이름이 유일한 단서다.
        (ProjectEditor editor, StoryFile board) = World();

        var card = new DialogueNode(name: "ep01");
        board.Nodes.Add(card);

        Assert.Null(card.MarkedEpisodeId);
        Assert.Same(card, EpisodeNaming.CardFor(editor.Project, "ch01", "ep01"));
    }

    [Fact]
    public void 다른_챕터의_같은_Id는_집지_않는다()
    {
        // EpisodeId는 챕터 안에서만 유일하다 — 프로젝트 전체를 훑으면 남의 카드를 건드린다.
        (ProjectEditor editor, StoryFile board) = World();

        editor.EnsureChapter("ch02");
        string otherId = editor.EnsureChapterBoard("ch02");
        StoryFile other = editor.Project.FindFile(otherId)!;

        var mine = new DialogueNode(name: "ep01") { MarkedEpisodeId = "ep01" };
        var theirs = new DialogueNode(name: "ep01") { MarkedEpisodeId = "ep01" };

        board.Nodes.Add(mine);
        other.Nodes.Add(theirs);

        Assert.Same(mine, EpisodeNaming.CardFor(editor.Project, "ch01", "ep01"));
        Assert.Same(theirs, EpisodeNaming.CardFor(editor.Project, "ch02", "ep01"));
    }

    [Fact]
    public void 떼어_낸_카드는_더_이상_안_잡힌다()
    {
        // 떼기는 표식을 비우고 <b>이름까지</b> 바꾼다 — 둘 다여야 뒷길로도 안 잡힌다.
        (ProjectEditor editor, StoryFile board) = World();

        var card = new DialogueNode(name: "ep01") { MarkedEpisodeId = "ep01" };
        board.Nodes.Add(card);

        editor.DetachEpisodeMark(card.Id);

        Assert.Null(EpisodeNaming.CardFor(editor.Project, "ch01", "ep01"));
    }

    [Fact]
    public void 챕터와_판은_같은_한_줄로_서로를_찾는다()
    {
        (ProjectEditor editor, StoryFile board) = World();

        Assert.Same(board, EpisodeNaming.BoardOf(editor.Project, "ch01"));
        Assert.Equal("ch01", EpisodeNaming.ChapterOfBoard(editor.Project, board)!.ChapterId);

        // 챕터 아닌 판은 양쪽 다 null이다 — 새 프로젝트의 `기본 파일`이 그렇다.
        StoryFile scratch = editor.AddStoryFile("낙서");

        Assert.Null(EpisodeNaming.ChapterOfBoard(editor.Project, scratch));
        Assert.Null(EpisodeNaming.BoardOf(editor.Project, "없는챕터"));
    }

    private static (ProjectEditor Editor, StoryFile Board) World()
    {
        var editor = new ProjectEditor(new StoryProject());
        editor.EnsureChapter("ch01");

        return (editor, editor.Project.FindFile(editor.EnsureChapterBoard("ch01"))!);
    }
}
