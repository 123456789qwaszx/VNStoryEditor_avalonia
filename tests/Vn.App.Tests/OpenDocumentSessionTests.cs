using System.Text;
using Vn.App.Services;

namespace Vn.App.Tests;

public class OpenDocumentSessionTests
{
    [Fact]
    public void 원문_수정_뒤_다른_박스_수정을_해도_두_변경이_모두_남는다()
    {
        InTemporaryFile(
            "title: T\r\n---\r\nAnn: 원문.\r\nBob: 박스.\r\n===\r\n",
            path =>
            {
                OpenDocumentSession document = OpenDocumentSession.Open(path);

                document.ReplaceText(
                    document.WorkingText.Replace("Ann: 원문.", "Ann: 원문에서 고침.", StringComparison.Ordinal));

                bool applied = document.TryApplyLineEdit(
                    new StoryLineEdit(4, "Bob", "박스에서 고침.", "Bob: 박스."));

                Assert.True(applied);
                Assert.Contains("Ann: 원문에서 고침.", document.WorkingText, StringComparison.Ordinal);
                Assert.Contains("Bob: 박스에서 고침.", document.WorkingText, StringComparison.Ordinal);

                Assert.Equal(DocumentSaveStatus.Saved, document.Save().Status);

                string saved = File.ReadAllText(path, Encoding.UTF8);
                Assert.Contains("Ann: 원문에서 고침.", saved, StringComparison.Ordinal);
                Assert.Contains("Bob: 박스에서 고침.", saved, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void 원문_수정_뒤에도_같은_박스에서_연속_입력할_수_있다()
    {
        InTemporaryFile(
            "title: T\n---\nAnn: 원문.\nBob: 박스.\n===\n",
            path =>
            {
                OpenDocumentSession document = OpenDocumentSession.Open(path);
                document.ReplaceText(
                    document.WorkingText.Replace("Ann: 원문.", "Ann: 원문에서 고침.", StringComparison.Ordinal));

                Assert.True(document.TryApplyLineEdit(
                    new StoryLineEdit(4, "Bob", "박스에서 첫 입력.", "Bob: 박스.")));

                // UI의 같은 카드가 연속 TextChanged를 내면 최초 원문 문자열을 계속 들고 있다.
                // 두 번째 입력도 같은 물리적 줄에 적용되어야 한다.
                Assert.True(document.TryApplyLineEdit(
                    new StoryLineEdit(4, "Bob", "박스에서 두 번째 입력.", "Bob: 박스.")));

                Assert.Contains("Ann: 원문에서 고침.", document.WorkingText, StringComparison.Ordinal);
                Assert.Contains("Bob: 박스에서 두 번째 입력.", document.WorkingText, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void 원문에서_줄이_밀렸으면_오래된_박스_편집을_거부한다()
    {
        InTemporaryFile(
            "title: T\n---\nAnn: 첫째.\nBob: 둘째.\n===\n",
            path =>
            {
                OpenDocumentSession document = OpenDocumentSession.Open(path);
                document.ReplaceText(document.WorkingText.Replace("---\n", "---\n새 줄\n", StringComparison.Ordinal));

                bool applied = document.TryApplyLineEdit(
                    new StoryLineEdit(4, "Bob", "고침.", "Bob: 둘째."));

                Assert.False(applied);
                Assert.DoesNotContain("Bob: 고침.", document.WorkingText, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void 외부_변경이_있으면_조용히_덮어쓰지_않는다()
    {
        InTemporaryFile(
            "title: T\n---\nAnn: 원본.\n===\n",
            path =>
            {
                OpenDocumentSession document = OpenDocumentSession.Open(path);
                document.ReplaceText(document.WorkingText.Replace("원본", "내 변경", StringComparison.Ordinal));

                File.WriteAllText(path, "title: T\n---\nAnn: 외부 변경.\n===\n", new UTF8Encoding(false));

                DocumentSaveResult conflict = document.Save();

                Assert.Equal(DocumentSaveStatus.ExternalConflict, conflict.Status);
                Assert.Contains("외부 변경", File.ReadAllText(path), StringComparison.Ordinal);
                Assert.True(document.IsDirty);

                Assert.Equal(
                    DocumentSaveStatus.Saved,
                    document.Save(overwriteExternalChanges: true).Status);
                Assert.Contains("내 변경", File.ReadAllText(path), StringComparison.Ordinal);
            });
    }

    private static void InTemporaryFile(string text, Action<string> test)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"VnTool.OpenDocumentSessionTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Story.yarn");
        File.WriteAllText(path, text, new UTF8Encoding(false));

        try
        {
            test(path);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
