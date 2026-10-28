using System.Text;
using Vn.App.Services;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

public class AuthoringSessionTests
{
    [Fact]
    public void 새_프로젝트는_기본_StoryFile을_현재_파일로_가진다()
    {
        var session = new AuthoringSession();

        StoryFile file = Assert.Single(session.Project.Files);
        Assert.Equal(file.Id, session.ActiveFileId);
        Assert.Same(file, session.ActiveFile);
        Assert.EndsWith(StoryFileJson.FileExtension, file.RelativePath);
    }

    [Fact]
    public void 프로젝트를_열면_시작_노드가_속한_파일이_현재_파일이_된다()
    {
        var project = new StoryProject { Title = "테스트" };
        var first = new StoryFile("sf_a", "A", "story/a.vnstory.json");
        var second = new StoryFile("sf_b", "B", "story/b.vnstory.json");
        project.Files.Add(first);
        project.Files.Add(second);
        first.Nodes.Add(new DialogueNode("nd_a", "A"));
        second.Nodes.Add(new DialogueNode("nd_b", "B"));
        project.StartNodeId = "nd_b";

        string directory = TempDirectory();
        string path = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);

        try
        {
            ProjectStore.Save(path, project);
            var session = new AuthoringSession();

            session.Open(path);

            Assert.Equal(second.Id, session.ActiveFileId);
            Assert.Equal("nd_b", session.SelectedNodeId);
            Assert.All(session.Project.Files, file => Assert.True(session.IsFileExpanded(file.Id)));
            Assert.Equal(Path.GetFullPath(path), session.ProjectPath);
            Assert.False(session.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void dirty_비교는_디스크가_아니라_ProjectSnapshotCodec을_사용한다()
    {
        var session = new AuthoringSession();
        StoryFile file = session.ActiveFile!;

        Assert.False(session.IsDirty);
        DialogueNode node = session.Editor.AddDialogueNode(file.Id, name: "새 장면");
        Assert.True(session.IsDirty);

        session.Editor.Undo();
        Assert.False(session.IsDirty);

        session.Editor.Redo();
        Assert.True(session.IsDirty);
        Assert.NotNull(session.Project.FindNode(node.Id));
    }

    /// <summary>
    /// 이전 형식은 자동 마이그레이션하지 않고 거부한다. 이 테스트는 그 마이그레이션을
    /// 검증하던 테스트를 대체한다. 열지 못한 뒤에도 세션은 원래 상태로 계속 쓸 수 있어야 한다.
    /// </summary>
    [Fact]
    public void 이전_형식을_열면_거부하고_세션은_그대로_남는다()
    {
        string directory = TempDirectory();
        string legacyPath = Path.Combine(directory, "legacy.vnproject.json");
        File.WriteAllText(legacyPath, """
            {
              "formatVersion": 2,
              "title": "이전",
              "files": []
            }
            """, new UTF8Encoding(false));

        try
        {
            var session = new AuthoringSession();
            string? pathBefore = session.ProjectPath;
            int nodesBefore = session.Project.EnumerateNodes().Count();

            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => session.Open(legacyPath));

            Assert.Contains("더 이상 열 수 없습니다", error.Message, StringComparison.Ordinal);
            Assert.Equal(pathBefore, session.ProjectPath);
            Assert.Equal(nodesBefore, session.Project.EnumerateNodes().Count());
            Assert.False(session.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public void 새_프로젝트의_모든_파일은_기본적으로_그래프에_펼쳐진다()
    {
        var session = new AuthoringSession();
        StoryFile first = Assert.Single(session.Project.Files);

        StoryFile second = session.Editor.AddStoryFile("두 번째");

        Assert.True(session.IsFileExpanded(first.Id));
        Assert.True(session.IsFileExpanded(second.Id));
        Assert.Equal(
            new[] { first.Id, second.Id },
            session.ExpandedFileIds.OrderBy(id => session.Project.Files.FindIndex(file => file.Id == id)));
    }

    [Fact]
    public void 파일_선택은_펼침을_접지_않는다_여러_파일을_함께_본다()
    {
        // W49 (소유자 판단): W43의 "선택=그 파일만 펼침"은 여러 파일을 오가며 함께
        // 보는 것을 막아 철회. 선택과 펼침은 독립이고, 접힌 파일을 고르면 펴 주기만 한다.
        var session = new AuthoringSession();
        StoryFile first = session.ActiveFile!;
        StoryFile second = session.Editor.AddStoryFile("두 번째");
        DialogueNode firstNode = session.Editor.AddDialogueNode(first.Id, name: "첫 파일");
        DialogueNode secondNode = session.Editor.AddDialogueNode(second.Id, name: "둘째 파일");

        session.SelectFile(second.Id);

        Assert.Equal(second.Id, session.ActiveFileId);
        Assert.True(session.IsFileExpanded(first.Id));  // 다른 파일은 접히지 않는다
        Assert.True(session.IsFileExpanded(second.Id));
        Assert.Equal(
            new[] { firstNode.Id, secondNode.Id },
            session.EnumerateExpandedNodes().Select(node => node.Id));

        // 접어 둔 파일을 고르면 펴 주기만 한다 — 고른 파일이 안 보이는 일은 없다.
        session.SetFileExpanded(first.Id, expanded: false);
        session.SelectFile(first.Id);
        Assert.True(session.IsFileExpanded(first.Id));
        Assert.True(session.IsFileExpanded(second.Id));
    }

    [Fact]
    public void 펼침_체크는_프로젝트_dirty와_Undo에_영향을_주지_않는다()
    {
        var session = new AuthoringSession();
        StoryFile file = session.ActiveFile!;

        session.SetFileExpanded(file.Id, expanded: false);

        Assert.False(session.IsDirty);
        Assert.False(session.Editor.CanUndo);
        Assert.False(session.IsFileExpanded(file.Id));
    }

    [Fact]
    public void 새_노드는_ActiveFile의_마지막에_추가된다()
    {
        var session = new AuthoringSession();
        StoryFile first = session.ActiveFile!;
        StoryFile second = session.Editor.AddStoryFile("두 번째");
        SetNode existing = session.Editor.AddSetNode(second.Id, name: "기존");

        session.SelectFile(second.Id);
        DialogueNode added = session.Editor.AddDialogueNode(session.ActiveFileId!, name: "추가");

        Assert.Empty(first.Nodes);
        Assert.Same(added, second.Nodes[^1]);
        Assert.Equal(new[] { existing.Id, added.Id }, second.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void 파일_상태_변경_이벤트는_현재_선택과_펼침_변경을_구분한다()
    {
        var session = new AuthoringSession();
        StoryFile first = session.ActiveFile!;
        StoryFile second = session.Editor.AddStoryFile("두 번째");
        var changes = new List<FileGraphStateChangedEventArgs>();
        session.FileGraphStateChanged += (_, e) => changes.Add(e);

        session.SelectFile(second.Id);
        session.SetFileExpanded(first.Id, expanded: false);

        Assert.Collection(
            changes,
            active =>
            {
                // 이미 펼쳐진 파일의 선택은 펼침을 건드리지 않는다 (W49 — 독립 복원).
                Assert.True(active.ActiveFileChanged);
                Assert.False(active.ExpandedFilesChanged);
                Assert.False(active.FileListChanged);
            },
            expanded =>
            {
                Assert.False(expanded.ActiveFileChanged);
                Assert.True(expanded.ExpandedFilesChanged);
                Assert.False(expanded.FileListChanged);
            });
    }

    [Fact]
    public void 파일_추가와_노드_개수_변경은_파일_목록_갱신으로_알린다()
    {
        var session = new AuthoringSession();
        var changes = new List<FileGraphStateChangedEventArgs>();
        session.FileGraphStateChanged += (_, e) => changes.Add(e);

        StoryFile added = session.Editor.AddStoryFile("추가");
        session.Editor.AddDialogueNode(added.Id, name: "장면");

        Assert.Equal(2, changes.Count);
        Assert.All(changes, change => Assert.True(change.FileListChanged));
        Assert.True(changes[0].ExpandedFilesChanged);
        Assert.False(changes[1].ExpandedFilesChanged);
    }


    [Fact]
    public void 접어_둔_파일은_다른_프로젝트_편집_뒤에도_접힌_상태를_유지한다()
    {
        var session = new AuthoringSession();
        StoryFile first = session.ActiveFile!;
        StoryFile second = session.Editor.AddStoryFile("두 번째");
        DialogueNode node = session.Editor.AddDialogueNode(first.Id, name: "장면");

        session.SetFileExpanded(second.Id, expanded: false);
        session.Editor.RenameNode(node.Id, "수정된 장면");

        Assert.False(session.IsFileExpanded(second.Id));
        Assert.True(session.IsFileExpanded(first.Id));
    }

    [Fact]
    public void Undo로_파일이_사라지면_workspace_상태에서_정리되고_Redo시_다시_펼쳐진다()
    {
        var session = new AuthoringSession();
        StoryFile added = session.Editor.AddStoryFile("추가");

        Assert.True(session.IsFileExpanded(added.Id));

        session.Editor.Undo();

        Assert.Null(session.Project.FindFile(added.Id));
        Assert.DoesNotContain(added.Id, session.ExpandedFileIds);

        session.Editor.Redo();

        Assert.NotNull(session.Project.FindFile(added.Id));
        Assert.Contains(added.Id, session.ExpandedFileIds);
    }

    /// <summary>
    /// "작가가 더한 화자"는 2026-08-23에 폐지됐다 (소유자: 캐릭터는 컨셉·배경이 꼼꼼히
    /// 정해져야 하는 것이라 작가가 임의로 더할 자리가 아니다). 더하는 화면도 고르는
    /// 목록도 사라졌지만, <b>구판 프로젝트의 값은 조용히 날리지 않는다</b> — 이 저장소의
    /// "구판 데이터는 지우지 않고 무시한다" 규칙이다.
    /// </summary>
    [Fact]
    public void 폐지된_작가_화자_데이터는_열고_저장해도_살아남는다()
    {
        string directory = TempDirectory();

        try
        {
            string path = Path.Combine(directory, "project.vnproject.json");
            var session = new AuthoringSession();
            session.Save(path);

            // 구판 프로젝트가 들고 있던 값 — 이제 아무 화면도 이걸 읽지 않는다.
            session.Project.WriterSpeakers.Add(new WriterSpeaker { Name = "구판화자" });
            session.Save(path);

            var reopened = new AuthoringSession();
            reopened.Open(path);
            Assert.Equal("구판화자", Assert.Single(reopened.Project.WriterSpeakers).Name);

            // 정의 파일은 예나 지금이나 손대지 않는다 (2026-08-17 — 기획자 전용).
            Assert.DoesNotContain(reopened.Definition.Speakers, speaker =>
                string.Equals(speaker.Name, "구판화자", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 기획자 어휘가 갈리면 <b>스스로 알린다</b> (2026-08-26 소유자 보고: 챕터 그래프에서
    /// 화자를 지웠는데 연출 그래프가 옛 목록을 계속 보였다 — "바로바로 반영이 됐으면 해").
    ///
    /// 예전에는 <c>SaveSpeakers</c>가 파일을 쓰고 정의를 <b>조용히</b> 다시 읽었고, 이미
    /// 그려진 화면은 그 사실을 알 길이 없어 제 노드를 다시 고를 때까지 낡은 목록을 들었다.
    /// <b>지우기가 특히 나빴다</b>: 없는 이름이 드롭다운에 남아 고를 수 있었다.
    ///
    /// ⚠ 이 시험이 거는 것은 <b>알림이 온다</b>는 사실 하나다 — 누가 무엇을 다시 그리는지는
    /// 듣는 쪽의 사정이고, 알림이 없으면 어느 쪽도 알 수가 없다.
    /// </summary>
    [Fact]
    public void 화자를_지우면_정의가_바뀌었다고_알린다()
    {
        string directory = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Save(Path.Combine(directory, "project.vnproject.json"));

            Assert.True(session.SaveSpeakers([
                new SpeakerSpec { Name = "라루" },
                new SpeakerSpec { Name = "윌로우" }
            ]));

            int notified = 0;
            session.DefinitionChanged += (_, _) => notified++;

            // 지우는 방식 — 남길 것만 넘긴다(통째로 쓰는 것이 규격이다).
            Assert.True(session.SaveSpeakers([new SpeakerSpec { Name = "라루" }]));

            Assert.Equal(1, notified);
            Assert.Equal("라루", Assert.Single(session.Definition.Speakers).Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ── 새 프로젝트 자동 준비 ────────────────────────

    [Fact]
    public void 첫_저장은_정의_파일을_준비한다()
    {
        string directory = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Save(Path.Combine(directory, "project.vnproject.json"));

            Assert.True(File.Exists(Path.Combine(directory, "game.definition.json")));
            Assert.False(session.IsDirty); // 준비 과정이 미저장 변경을 남기지 않는다
            Assert.Contains("정의 파일을 준비했습니다", session.StatusMessage);

            // 에셋 폴더·튜닝은 이 툴의 일이 아니다 (2026-10-28) — 아무것도 안 만든다.
            Assert.False(Directory.Exists(Path.Combine(directory, "assets")));
            Assert.False(Directory.Exists(Path.Combine(directory, "ExportedTuning")));

            // 두 번째 저장은 준비를 반복하지 않는다 — 상태 메시지가 담백하다.
            session.Save();
            Assert.DoesNotContain("준비했습니다", session.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 다른_폴더로_저장하면_챕터_대본_정의가_함께_이사한다()
    {
        // 2026-08-21 소유자 보고: [다른 이름으로…]로 새 폴더에 저장해 "새 프로젝트"를
        // 만들면, 판·작가 화자는 매니페스트에 실려 따라오는데 챕터 워크북·대본·정의는
        // 옛 폴더에 남아 프로젝트가 찢어졌다 — 연출 그래프에는 옛 챕터의 판이 보이는데
        // 챕터 목록은 비어 있었다. 이제 규약 데이터가 함께 복사된다.
        string first = TempDirectory();
        string second = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Save(Path.Combine(first, "project.vnproject.json"));

            Directory.CreateDirectory(Path.Combine(first, "chapters"));
            File.WriteAllText(Path.Combine(first, "chapters", "ch01.xlsx"), "챕터 워크북");
            File.WriteAllText(Path.Combine(first, "chapters", "~$ch01.xlsx"), "엑셀 잠금 임시");
            Directory.CreateDirectory(Path.Combine(first, "episodes", "ch01"));
            File.WriteAllText(Path.Combine(first, "episodes", "ch01", "ep01.xlsx"), "대본");
            File.WriteAllText(Path.Combine(first, "game.definition.json"),
                """{"variables":[{"name":"trust","type":"number"}],"speakers":[]}""");

            // 새 자리에 이미 있는 파일은 불가침이다.
            Directory.CreateDirectory(Path.Combine(second, "chapters"));
            File.WriteAllText(Path.Combine(second, "chapters", "ch01.xlsx"), "선주민");

            session.Save(Path.Combine(second, "moved.vnproject.json"));

            Assert.Equal("선주민", File.ReadAllText(Path.Combine(second, "chapters", "ch01.xlsx")));
            Assert.True(File.Exists(Path.Combine(second, "episodes", "ch01", "ep01.xlsx")));
            Assert.False(File.Exists(Path.Combine(second, "chapters", "~$ch01.xlsx")));

            // 이사해 온 정의를 곧바로 읽는다 — 빈 뼈대가 먼저 깔려 진짜 정의를 막으면 안 된다.
            Assert.Contains(session.Definition.Variables, variable =>
                string.Equals(variable.Name, "trust", StringComparison.Ordinal));
            Assert.Contains("함께 복사했습니다", session.StatusMessage);

            // 복사이지 이동이 아니다 — 옛 매니페스트는 계속 그 폴더에서 산다.
            Assert.True(File.Exists(Path.Combine(first, "chapters", "ch01.xlsx")));

            // 같은 폴더 재저장은 이사가 아니다.
            session.Save();
            Assert.DoesNotContain("함께 복사했습니다", session.StatusMessage);
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Session.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
