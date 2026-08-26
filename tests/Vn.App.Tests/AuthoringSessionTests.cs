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

            // ⛔ <b>기본 튜닝은 로더가 찾는 것을 하나도 빠뜨리지 않는다</b> (2026-08-26).
            //    파일 이름을 몇 개 세는 것으로는 이걸 못 지킨다 — 로더에 축이 하나 늘면
            //    (W64의 `presets/role-anchor.json`이 그랬다) 여기 목록은 조용히 통과하고
            //    새 프로젝트는 <b>열 때마다 경고</b>를 맞는다. 실제로 그렇게 났다.
            //    불평 목록이 비어 있는지를 걸면 다음에 축이 늘 때 이 시험이 먼저 운다.
            Assert.Empty(session.TuningLibrary.Problems);
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
            Assert.Equal("assets/bgm", session.Project.AssetRoots.BgmPath);   // W59
            Assert.Equal("assets/sfx", session.Project.AssetRoots.SfxPath);
            Assert.True(Directory.Exists(Path.Combine(directory, "assets", "backgrounds")));
            Assert.True(Directory.Exists(Path.Combine(directory, "assets", "portraits")));
            Assert.True(Directory.Exists(Path.Combine(directory, "assets", "bgm")));
            Assert.True(Directory.Exists(Path.Combine(directory, "assets", "sfx")));
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
    public void 저장은_없는_폴더를_복구하고_다른_이름_저장은_새_자리에_살림을_차린다()
    {
        // W60: 실수로 지운 폴더는 다음 저장이 되살리고, 다른 이름으로 저장한 새 위치에도
        // 에셋 폴더·기본 튜닝이 준비된다. 내용이 있는 폴더는 불가침이다.
        string first = TempDirectory();
        string second = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Save(Path.Combine(first, "project.vnproject.json"));

            Directory.Delete(Path.Combine(first, "assets", "bgm"), recursive: true);
            session.Save(); // 같은 자리 재저장 — 지워진 폴더가 되살아난다
            Assert.True(Directory.Exists(Path.Combine(first, "assets", "bgm")));

            session.Save(Path.Combine(second, "다른이름.vnproject.json")); // 다른 이름으로 저장
            Assert.True(Directory.Exists(Path.Combine(second, "assets", "backgrounds")));
            Assert.True(Directory.Exists(Path.Combine(second, "assets", "portraits")));
            Assert.True(Directory.Exists(Path.Combine(second, "assets", "bgm")));
            Assert.True(Directory.Exists(Path.Combine(second, "assets", "sfx")));
            Assert.True(File.Exists(Path.Combine(second, "ExportedTuning", "base-resolution.json")));
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void 다른_폴더로_저장하면_챕터_대본_정의_에셋이_함께_이사한다()
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
            File.WriteAllBytes(Path.Combine(first, "assets", "backgrounds", "room.png"), [1]);

            // 새 자리에 이미 있는 파일은 불가침이다.
            Directory.CreateDirectory(Path.Combine(second, "chapters"));
            File.WriteAllText(Path.Combine(second, "chapters", "ch01.xlsx"), "선주민");

            session.Save(Path.Combine(second, "moved.vnproject.json"));

            Assert.Equal("선주민", File.ReadAllText(Path.Combine(second, "chapters", "ch01.xlsx")));
            Assert.True(File.Exists(Path.Combine(second, "episodes", "ch01", "ep01.xlsx")));
            Assert.True(File.Exists(Path.Combine(second, "assets", "backgrounds", "room.png")));
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

    [Fact]
    public void 오디오_가져오기는_복제하고_저장_후_다시_열어도_루트가_남는다()
    {
        string directory = TempDirectory();
        string source = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            string projectPath = Path.Combine(directory, "project.vnproject.json");
            session.Save(projectPath);

            string clip = Path.Combine(source, "main_theme.mp3");
            File.WriteAllBytes(clip, [1, 2]);
            string ignored = Path.Combine(source, "노트.txt");
            File.WriteAllText(ignored, "오디오 아님");

            Assert.Equal(1, session.ImportAudio(bgm: true, [clip, ignored])); // 규약 외 확장자는 걸러진다
            Assert.True(File.Exists(Path.Combine(directory, "assets", "bgm", "main_theme.mp3")));
            Assert.Equal(["main_theme"], session.AudioClipKeys(session.BgmRoot));

            // 오디오 루트는 저장본에 남는다 (W59 직렬화).
            session.Save();
            var reopened = new AuthoringSession();
            reopened.Open(projectPath);
            Assert.Equal("assets/bgm", reopened.Project.AssetRoots.BgmPath);
            Assert.Equal("assets/sfx", reopened.Project.AssetRoots.SfxPath);
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
