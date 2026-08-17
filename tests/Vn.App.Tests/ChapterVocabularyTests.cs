using Vn.App.Services;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;

namespace Vn.App.Tests;

/// <summary>
/// A계층 어휘가 작가 화면으로 넘어가는 경로 (2026-08-17 소유자 — 계층 분리).
/// 챕터를 읽은 목록 하나에서 스탯 키·등록 화자가 세션으로 오고, 작가의 변수 후보는
/// 그 스탯을 뺀 것이다("스탯이 변하는 자리는 간선뿐").
/// </summary>
public sealed class ChapterVocabularyTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-layer", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 챕터_스탯과_등록_화자가_세션의_A계층_어휘가_된다()
    {
        Directory.CreateDirectory(_directory);
        ChapterWorkbookWriter.EnsureChapterWorkbook(
            _directory, "ch01", [("trust", "신뢰"), ("anger", "분노")]);
        string path = Path.Combine(_directory, "ch01.xlsx");
        ChapterWorkbookWriter.AddSpeaker(path, "라루", "laru", null);

        var session = new AuthoringSession();
        session.SupplyChapterVocabulary(ChapterLibrary.Load(_directory));

        Assert.Equal(["anger", "trust"], session.ChapterStatKeys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Contains("라루", session.ChapterSpeakerNames);
    }

    [Fact]
    public void 챕터_판을_열면_설정_노드가_함께_선다()
    {
        // 2026-08-17 소유자 — 설정노드는 만들고 지우는 것이 아니라 챕터에 딸린 자리다.
        var session = new AuthoringSession();
        string fileId = session.EnsureChapterBoard("ch01");

        SetNode settings = Assert.Single(
            session.Project.FindFile(fileId)!.Nodes.OfType<SetNode>());
        Assert.Equal("ch01 설정", settings.Name);

        // 같은 챕터를 다시 열어도 하나뿐이다.
        session.EnsureChapterBoard("ch01");
        Assert.Single(session.Project.FindFile(fileId)!.Nodes.OfType<SetNode>());
    }

    [Fact]
    public void 챕터가_없으면_A계층_어휘도_비어_있다()
    {
        // 작가 혼자 쓰는 판에서 변수 후보가 통째로 사라지면 안 된다 — 뺄 것이 없으면 안 뺀다.
        var session = new AuthoringSession();
        session.SupplyChapterVocabulary([]);

        Assert.Empty(session.ChapterStatKeys);
        Assert.Empty(session.ChapterSpeakerNames);
    }
}
