using System.Text;
using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// 읽고 그대로 다시 쓰면 한 바이트도 달라지지 않아야 한다.
///
/// 이 도구가 저장한 파일을 Unity가 그대로 읽고, 작가는 그 파일을 git에 올린다.
/// 손대지 않은 줄이 diff에 뜨는 순간 진짜 변경이 묻힌다.
/// </summary>
public class StoryFileServiceTests
{
    public static TheoryData<string> YarnFiles
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (string path in Directory
                         .EnumerateFiles(SamplesRoot, "*.yarn", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                data.Add(path);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(YarnFiles))]
    public void 샘플을_읽고_그대로_다시_쓰면_바이트가_같다(string samplePath)
    {
        byte[] original = File.ReadAllBytes(samplePath);

        // 원본 샘플은 절대 건드리지 않는다. 복사본에만 쓴다.
        RunInTemporaryDirectory(workDirectory =>
        {
            string copyPath = Path.Combine(workDirectory, Path.GetFileName(samplePath));
            File.WriteAllBytes(copyPath, original);

            StoryFile file = StoryFileService.Read(copyPath);
            StoryFileService.Write(copyPath, file.Text, file);

            Assert.Equal(original, File.ReadAllBytes(copyPath));
        });
    }

    /// <summary>
    /// 위 검사가 원본을 건드리지 않는다는 것 자체를 확인한다.
    /// 왕복 검사가 통과해도 그게 원본을 덮어쓰면서 통과한 것이라면 의미가 없다.
    /// </summary>
    [Fact]
    public void 왕복_검사는_원본_샘플을_바꾸지_않는다()
    {
        Dictionary<string, byte[]> before = SampleBytes();

        foreach (string path in before.Keys)
        {
            샘플을_읽고_그대로_다시_쓰면_바이트가_같다(path);
        }

        Dictionary<string, byte[]> after = SampleBytes();

        Assert.Equal(before.Keys.OrderBy(key => key, StringComparer.Ordinal),
                     after.Keys.OrderBy(key => key, StringComparer.Ordinal));

        foreach ((string path, byte[] bytes) in before)
        {
            Assert.Equal(bytes, after[path]);
        }
    }

    /// <summary>
    /// 샘플은 지금 전부 BOM 없는 UTF-8이다. 그 한 가지만으로는
    /// BOM 복원과 줄바꿈 보존 코드가 한 번도 실행되지 않는다.
    /// 그래서 실제로 만날 만한 형태를 직접 만들어 함께 검사한다.
    /// </summary>
    public static TheoryData<string, byte[]> EncodingCases
    {
        get
        {
            var utf8 = new UTF8Encoding(false);
            var data = new TheoryData<string, byte[]>();

            data.Add("UTF-8 BOM 없음, LF",
                utf8.GetBytes("title: Start\n---\nAnn: 안녕하세요.\n===\n"));

            data.Add("UTF-8 BOM 있음, CRLF",
                Concat(Preamble(Encoding.UTF8),
                    utf8.GetBytes("title: Start\r\n---\r\nAnn: 안녕하세요.\r\n===\r\n")));

            data.Add("UTF-16LE BOM 있음, CRLF",
                Concat(Preamble(Encoding.Unicode),
                    Encoding.Unicode.GetBytes("title: Start\r\n---\r\n===\r\n")));

            data.Add("UTF-16BE BOM 있음, LF",
                Concat(Preamble(Encoding.BigEndianUnicode),
                    Encoding.BigEndianUnicode.GetBytes("title: Start\n---\n===\n")));

            // 한 파일에 두 줄바꿈이 섞인 경우. 정규화했다가 되돌리는 구현은 여기서 깨진다.
            data.Add("줄바꿈 섞임",
                utf8.GetBytes("title: Start\r\n---\nAnn: 섞여 있다.\r\n===\n"));

            data.Add("마지막 줄에 줄바꿈 없음",
                utf8.GetBytes("title: Start\n---\n==="));

            data.Add("빈 파일", Array.Empty<byte>());

            data.Add("BOM만 있는 파일", Preamble(Encoding.UTF8));

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EncodingCases))]
    public void 인코딩과_줄바꿈을_그대로_복원한다(string name, byte[] original)
    {
        Assert.NotNull(name);

        RunInTemporaryDirectory(workDirectory =>
        {
            string path = Path.Combine(workDirectory, "Story.yarn");
            File.WriteAllBytes(path, original);

            StoryFile file = StoryFileService.Read(path);
            StoryFileService.Write(path, file.Text, file);

            Assert.Equal(original, File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void 줄바꿈_종류를_알려준다()
    {
        var utf8 = new UTF8Encoding(false);

        Assert.Equal(LineEndingStyle.Lf, StyleOf(utf8.GetBytes("a\nb\n")));
        Assert.Equal(LineEndingStyle.CrLf, StyleOf(utf8.GetBytes("a\r\nb\r\n")));
        Assert.Equal(LineEndingStyle.Cr, StyleOf(utf8.GetBytes("a\rb\r")));
        Assert.Equal(LineEndingStyle.Mixed, StyleOf(utf8.GetBytes("a\r\nb\n")));
        Assert.Equal(LineEndingStyle.None, StyleOf(utf8.GetBytes("한 줄뿐")));
    }

    [Fact]
    public void BOM_유무를_알려준다()
    {
        var utf8 = new UTF8Encoding(false);

        Assert.False(ReadBytes(utf8.GetBytes("a\n")).HasByteOrderMark);
        Assert.True(ReadBytes(Concat(Preamble(Encoding.UTF8), utf8.GetBytes("a\n"))).HasByteOrderMark);
    }

    /// <summary>
    /// 쓰다가 실패해도 임시 파일을 남기지 않는다.
    /// 대상 경로가 폴더면 교체가 실패한다. 그 자리에서 원래 예외가 올라와야 하고,
    /// 작업 폴더에는 .tmp 찌꺼기가 남으면 안 된다.
    /// </summary>
    [Fact]
    public void 쓰기에_실패하면_임시_파일을_남기지_않는다()
    {
        RunInTemporaryDirectory(workDirectory =>
        {
            string sourcePath = Path.Combine(workDirectory, "Story.yarn");
            File.WriteAllBytes(sourcePath, new UTF8Encoding(false).GetBytes("title: Start\n"));

            StoryFile file = StoryFileService.Read(sourcePath);

            // 폴더를 덮어쓸 수는 없다.
            string blockedPath = Path.Combine(workDirectory, "폴더");
            Directory.CreateDirectory(blockedPath);

            // 어떤 예외인지는 플랫폼이 정한다. Windows는 UnauthorizedAccessException을 던진다.
            // 여기서 확인할 것은 타입이 아니라 "실패를 삼키지 않는다"와 "찌꺼기를 남기지 않는다"이다.
            Exception? thrown = Record.Exception(
                () => StoryFileService.Write(blockedPath, file.Text, file));

            Assert.NotNull(thrown);
            Assert.Empty(Directory.EnumerateFiles(workDirectory, "*.tmp"));
        });
    }

    private static LineEndingStyle StyleOf(byte[] bytes)
    {
        return ReadBytes(bytes).LineEndings;
    }

    private static StoryFile ReadBytes(byte[] bytes)
    {
        StoryFile? result = null;

        RunInTemporaryDirectory(workDirectory =>
        {
            string path = Path.Combine(workDirectory, "Story.yarn");
            File.WriteAllBytes(path, bytes);
            result = StoryFileService.Read(path);
        });

        Assert.NotNull(result);
        return result;
    }

    private static Dictionary<string, byte[]> SampleBytes()
    {
        return Directory
            .EnumerateFiles(SamplesRoot, "*.yarn", SearchOption.AllDirectories)
            .ToDictionary(
                path => path,
                File.ReadAllBytes,
                StringComparer.Ordinal);
    }

    private static void RunInTemporaryDirectory(Action<string> work)
    {
        string workDirectory = Path.Combine(
            Path.GetTempPath(),
            $"VnTool.StoryFileServiceTests.{Guid.NewGuid():N}");

        Directory.CreateDirectory(workDirectory);

        try
        {
            work(workDirectory);
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // 임시 폴더 정리 실패로 테스트를 떨어뜨리지는 않는다.
            }
        }
    }

    private static byte[] Preamble(Encoding encoding)
    {
        return encoding.GetPreamble();
    }

    private static byte[] Concat(byte[] left, byte[] right)
    {
        byte[] result = new byte[left.Length + right.Length];
        left.CopyTo(result, 0);
        right.CopyTo(result, left.Length);
        return result;
    }

    /// <summary>
    /// 테스트는 bin 아래에서 돌기 때문에 저장소 뿌리를 위로 올라가며 찾는다.
    /// samples 경로를 상대 경로로 박아두면 실행 위치가 바뀔 때 조용히 0건을 검사하게 된다.
    /// </summary>
    private static string SamplesRoot { get; } = FindSamplesRoot();

    private static string FindSamplesRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VnTool.sln")))
            {
                string samples = Path.Combine(directory.FullName, "samples");

                if (Directory.Exists(samples))
                {
                    return samples;
                }
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"samples 폴더를 찾지 못했습니다. 시작 위치: {AppContext.BaseDirectory}");
    }
}
