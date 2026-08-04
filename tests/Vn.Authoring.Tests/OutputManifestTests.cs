using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// K1 ②안 — 노드를 지우거나 이름을 바꾸면 출력 폴더에 옛 .yarn이 남는다. VnTool은
/// 그것을 <b>지우지 않고 보여 준다.</b> 여기서 고정하는 것은 두 가지다:
/// 목록이 실제 폴더 내용과 일치한다는 것, 그리고 어떤 경로로도 파일을 지우지 않는다는 것.
/// </summary>
public class OutputManifestTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 노드_이름을_바꾸면_옛_파일이_고아로_보인다()
    {
        string directory = TempDirectory();

        try
        {
            var sample = new Sample();
            string line = sample.Line("첫 줄");
            sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "첫 줄");

            WriteLive(sample, directory);
            Assert.Contains("Story_본문.yarn", Directory.GetFiles(directory).Select(Path.GetFileName));

            // 아직은 고아가 없다.
            Assert.Empty(Scan(sample, directory).Orphans);

            sample.Editor.RenameNode(sample.Dialogue.Id, "본문개정");
            WriteLive(sample, directory);

            OrphanOutputScan scan = Scan(sample, directory);

            Assert.Equal(["Story_본문.yarn"], scan.Orphans.Select(orphan => orphan.FileName));
            Assert.Equal(OrphanOutputSource.Recorded, scan.Orphans[0].Source);
            Assert.Null(scan.Note);

            // 새 이름의 파일은 살아 있고, 옛 파일도 지워지지 않았다.
            string[] onDisk = Directory.GetFiles(directory).Select(Path.GetFileName).OfType<string>().ToArray();
            Assert.Contains("Story_본문개정.yarn", onDisk);
            Assert.Contains("Story_본문.yarn", onDisk);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 노드를_지우면_그_노드의_파일이_전부_고아가_된다()
    {
        string directory = TempDirectory();

        try
        {
            var sample = new Sample();
            string line = sample.Line("첫 줄");
            sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "첫 줄");
            WriteLive(sample, directory);

            sample.Editor.RemoveNode(sample.Dialogue.Id);

            OrphanOutputScan scan = Scan(sample, directory);

            // 지운 노드의 파일만 고아다 — 남아 있는 다른 노드의 파일은 건드리지 않는다.
            Assert.Equal(["Story_본문.yarn"], scan.Orphans.Select(orphan => orphan.FileName));
            Assert.All(scan.Orphans, orphan => Assert.Equal(OrphanOutputSource.Recorded, orphan.Source));

            // 목록의 모든 항목이 실제로 폴더에 있는 파일이다. 그리고 지워지지 않았다.
            Assert.All(
                scan.Orphans,
                orphan => Assert.True(File.Exists(Path.Combine(directory, orphan.FileName))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 산출_기대_목록은_아직_안_쓰인_이름까지_포함한다()
    {
        // 판정 기준은 "이번에 쓴 파일"이 아니라 "지금 프로젝트가 만들 수 있는 파일"이다.
        // 그래서 연출이 없어 이번엔 안 나온 Set·Pres도, 막혀서 못 쓴 노드의 파일도
        // 고아로 몰리지 않는다.
        var sample = new Sample();
        string line = sample.Line("첫 줄");
        sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "첫 줄");

        Assert.Contains(
            "Story_본문.yarn",
            OutputManifest.ExpectedFileNames(sample.Project));

        Assert.Contains(
            "Set_본문.yarn",
            OutputManifest.ExpectedFileNames(sample.Project));

        Assert.Contains(
            YarnBundleEmitter.DeclarationsFileName,
            OutputManifest.ExpectedFileNames(sample.Project));
    }

    [Fact]
    public void 남의_파일은_고아_목록에_넣지_않는다()
    {
        string directory = TempDirectory();

        try
        {
            var sample = new Sample();
            string line = sample.Line("첫 줄");
            sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "첫 줄");
            WriteLive(sample, directory);

            File.WriteAllText(Path.Combine(directory, "메모.txt"), "사용자 파일");
            File.WriteAllText(Path.Combine(directory, "Dialogue.yarn"), "// 손으로 쓴 것");

            Assert.Empty(Scan(sample, directory).Orphans);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 기록이_없어도_이름_형식으로_낡은_파일을_찾는다()
    {
        // 매니페스트 이전에 쓴 폴더 — 기록은 없지만 옛 산출물은 그대로 있다.
        string directory = TempDirectory();

        try
        {
            var sample = new Sample();
            File.WriteAllText(Path.Combine(directory, "Story_없어진노드.yarn"), "title: Story_없어진노드\n---\n===\n");

            OrphanOutputScan scan = Scan(sample, directory);

            OrphanOutput orphan = Assert.Single(scan.Orphans);
            Assert.Equal("Story_없어진노드.yarn", orphan.FileName);
            Assert.Equal(OrphanOutputSource.NameShape, orphan.Source);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 기록이_깨졌으면_사유를_숨기지_않는다()
    {
        // 규칙 14 — 판정을 못 한 사실을 조용히 삼키지 않는다.
        string directory = TempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(directory, OutputManifest.FileName), "{ 이건 JSON이 아니다");
            File.WriteAllText(Path.Combine(directory, "Story_옛것.yarn"), "title: Story_옛것\n---\n===\n");

            OrphanOutputScan scan = OutputManifest.Scan(directory, [YarnBundleEmitter.DeclarationsFileName]);

            Assert.NotNull(scan.Note);
            Assert.Contains(OutputManifest.FileName, scan.Note!, StringComparison.Ordinal);
            Assert.Equal(["Story_옛것.yarn"], scan.Orphans.Select(orphan => orphan.FileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 기록은_아직_폴더에_있는_옛_산출물을_기억한다()
    {
        // 한 번 재저장했다고 "VnTool이 썼다"는 사실이 잊히면 안 된다 — 그러면 고아가
        // 다음 판정에서 '이름 형식만 같은 파일'로 강등된다.
        string directory = TempDirectory();

        try
        {
            OutputManifest.Record(directory, ["Story_A.yarn", "declarations.yarn"]);
            File.WriteAllText(Path.Combine(directory, "Story_A.yarn"), "x");
            File.WriteAllText(Path.Combine(directory, "declarations.yarn"), "x");

            OutputManifest.Record(directory, ["Story_B.yarn", "declarations.yarn"]);
            File.WriteAllText(Path.Combine(directory, "Story_B.yarn"), "x");

            Assert.Equal(
                ["Story_A.yarn", "Story_B.yarn", "declarations.yarn"],
                OutputManifest.Read(directory).Order(StringComparer.Ordinal));

            // 사라진 파일은 기록에서도 빠진다 — 기록이 유령을 모으지 않는다.
            File.Delete(Path.Combine(directory, "Story_A.yarn"));
            OutputManifest.Record(directory, ["Story_B.yarn"]);

            Assert.DoesNotContain("Story_A.yarn", OutputManifest.Read(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 기록_파일은_Yarn_컴파일_대상이_아니다()
    {
        Assert.StartsWith(".", OutputManifest.FileName, StringComparison.Ordinal);
        Assert.EndsWith(".json", OutputManifest.FileName, StringComparison.Ordinal);
    }

    private static OrphanOutputScan Scan(Sample sample, string directory) =>
        OutputManifest.Scan(directory, OutputManifest.ExpectedFileNames(sample.Project));

    /// <summary>라이브 출력 한 번 — LiveOutputService가 하는 일과 같은 순서다.</summary>
    private static void WriteLive(Sample sample, string directory)
    {
        var bundles = new List<YarnBundle>();

        foreach (DialogueNode node in sample.Project.EnumerateNodes().OfType<DialogueNode>())
        {
            LiveComposition composition = LiveNodeComposer.Compose(
                sample.Project, node.Id, GameDefinition.Empty, Now);

            if (composition.CanWrite)
            {
                bundles.Add(composition.Bundle!);
            }
        }

        if (bundles.Count > 0)
        {
            OutputManifest.Record(directory, YarnBundleEmitter.WriteBundles(bundles, directory));
        }
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Orphan.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
