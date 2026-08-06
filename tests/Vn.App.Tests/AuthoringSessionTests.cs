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

    // ── 튜닝 관리 (W46) ───────────────────────────────────────────────────

    [Fact]
    public void 기본_튜닝_생성은_규약_폴더에_내장_파일을_쓰고_바로_읽힌다()
    {
        string directory = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Save(Path.Combine(directory, "project.vnproject.json"));

            session.CreateDefaultTuning();

            string tuningRoot = Path.Combine(directory, "ExportedTuning");
            Assert.True(File.Exists(Path.Combine(tuningRoot, "base-resolution.json")));
            Assert.True(File.Exists(Path.Combine(tuningRoot, "rig-schemas.json")));
            Assert.True(File.Exists(Path.Combine(tuningRoot, "presets", "depth.json")));
            Assert.True(session.TuningLibrary.IsLoaded);
            Assert.True(session.TuningLibrary.RigCount > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 기본_튜닝_생성은_저장_전에는_안내만_하고_기존_폴더를_덮지_않는다()
    {
        var unsaved = new AuthoringSession();
        unsaved.CreateDefaultTuning(); // 저장 전 — 예외 없이 안내만
        Assert.Contains("저장", unsaved.StatusMessage);

        string directory = TempDirectory();

        try
        {
            // 이미 튜닝이 있는 폴더에 저장한다 — 첫 저장의 자동 준비(W48)도 불가침을 지킨다.
            string tuningRoot = Path.Combine(directory, "ExportedTuning");
            Directory.CreateDirectory(tuningRoot);
            File.WriteAllText(Path.Combine(tuningRoot, "custom.json"), "{}");

            var session = new AuthoringSession();
            session.Save(Path.Combine(directory, "project.vnproject.json"));

            Assert.False(File.Exists(Path.Combine(tuningRoot, "base-resolution.json")));

            session.CreateDefaultTuning(); // 내용이 있는 폴더 — 불가침

            Assert.False(File.Exists(Path.Combine(tuningRoot, "base-resolution.json")));
            Assert.True(File.Exists(Path.Combine(tuningRoot, "custom.json")));
            Assert.Contains("덮어쓰지 않습니다", session.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 튜닝_폴더_연결은_규약_자리로_복사하고_엉뚱한_폴더는_거른다()
    {
        string directory = TempDirectory();
        string source = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Save(Path.Combine(directory, "project.vnproject.json"));

            session.ConnectTuningFolder(source); // 튜닝 파일이 없는 폴더
            Assert.Contains("찾지 못했습니다", session.StatusMessage);

            File.WriteAllText(
                Path.Combine(source, "base-resolution.json"),
                """{"referenceResolution":{"x":1920,"y":1080}}""");
            Directory.CreateDirectory(Path.Combine(source, "presets"));
            File.WriteAllText(Path.Combine(source, "presets", "depth.json"), "{}");

            session.ConnectTuningFolder(source);

            string tuningRoot = Path.Combine(directory, "ExportedTuning");
            Assert.True(File.Exists(Path.Combine(tuningRoot, "base-resolution.json")));
            Assert.True(File.Exists(Path.Combine(tuningRoot, "presets", "depth.json")));
            Assert.True(session.TuningLibrary.IsLoaded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void 화자_저장은_정의를_다시_읽어_대사_드롭다운_원천에_반영된다()
    {
        string directory = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Save(Path.Combine(directory, "project.vnproject.json"));

            bool saved = session.SaveSpeakers(new[]
            {
                new SpeakerSpec { Name = "새화자", CharacterId = string.Empty } // 매핑 없어도 저장된다
            });

            Assert.True(saved);
            Assert.Contains(session.Definition.Speakers, speaker =>
                string.Equals(speaker.Name, "새화자", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ── 새 프로젝트 자동 준비 + PNG 가져오기 (W48) ────────────────────────

    [Fact]
    public void 첫_저장은_에셋_폴더와_기본_튜닝을_준비한다()
    {
        string directory = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Save(Path.Combine(directory, "project.vnproject.json"));

            Assert.Equal("assets/backgrounds", session.Project.AssetRoots.BackgroundsPath);
            Assert.Equal("assets/portraits", session.Project.AssetRoots.PortraitsPath);
            Assert.True(Directory.Exists(Path.Combine(directory, "assets", "backgrounds")));
            Assert.True(Directory.Exists(Path.Combine(directory, "assets", "portraits")));
            Assert.True(session.TuningLibrary.IsLoaded); // 기본 튜닝이 자동으로 깔려 연결된다
            Assert.False(session.IsDirty); // 준비 과정이 미저장 변경을 남기지 않는다

            // 두 번째 저장은 준비를 반복하지 않는다 — 상태 메시지가 담백하다.
            session.Save();
            Assert.DoesNotContain("기본 튜닝", session.StatusMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 배경_가져오기는_복제하고_같은_이름은_건너뛴다()
    {
        string directory = TempDirectory();
        string source = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Save(Path.Combine(directory, "project.vnproject.json"));

            string png = Path.Combine(source, "room_day.png");
            File.WriteAllBytes(png, [1, 2, 3]);

            Assert.Equal(1, session.ImportBackgrounds([png]));
            Assert.True(File.Exists(Path.Combine(directory, "assets", "backgrounds", "room_day.png")));

            Assert.Equal(0, session.ImportBackgrounds([png])); // 같은 이름 — 덮지 않는다
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void 초상화_가져오기는_규약_자리에_표정_번호를_이어_붙인다()
    {
        string directory = TempDirectory();
        string source = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Save(Path.Combine(directory, "project.vnproject.json"));

            string first = Path.Combine(source, "웃음.png");
            string second = Path.Combine(source, "화남.png");
            File.WriteAllBytes(first, [1]);
            File.WriteAllBytes(second, [2]);

            Assert.Equal(2, session.ImportPortraits("willow", 'a', [first, second]));

            string folder = Path.Combine(directory, "assets", "portraits", "willow", "a");
            Assert.True(File.Exists(Path.Combine(folder, "01.png")));
            Assert.True(File.Exists(Path.Combine(folder, "02.png")));

            Assert.Equal(1, session.ImportPortraits("willow", 'a', [first]));
            Assert.True(File.Exists(Path.Combine(folder, "03.png"))); // 빈 번호를 이어 쓴다
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Session.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
