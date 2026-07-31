using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Core.Story;

namespace Vn.App.Views;

public partial class AnalysisView : UserControl
{
    public AnalysisView()
    {
        InitializeComponent();
    }

    // 분석은 Yarn 전체를 컴파일하므로 프로젝트가 커지면 눈에 띄게 오래 걸린다.
    // UI 스레드에서 돌리면 그동안 창이 통째로 멈춘다.
    //
    // Vn.Core에는 비동기 API를 두지 않는다. Core는 UI를 모르는 동기 라이브러리로 두고,
    // 어느 스레드에서 부를지는 부르는 쪽이 정한다. 그래서 감싸는 일은 여기서만 한다.
    private async void OnAnalyzeClick(object? sender, RoutedEventArgs e)
    {
        string projectPath = ProjectPathBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            StatusText.Text = "경로를 입력하세요.";
            return;
        }

        // 버튼을 끄는 것은 안내이기도 하고 재진입 방지이기도 하다.
        // 분석 중에 또 누르면 두 번째 분석이 첫 번째 결과를 덮어쓴다.
        AnalyzeButton.IsEnabled = false;
        StatusText.Text = "분석 중...";
        ClearResults();

        try
        {
            string fullPath = Path.GetFullPath(projectPath.Trim('"'));

            string schemaPath = Path.Combine(
                Path.GetDirectoryName(fullPath) ?? ".",
                "game.schema.json");

            AnalysisReport report = await Task.Run(
                () => new VnProjectAnalyzer().Analyze(fullPath, schemaPath));

            ShowResults(report);
        }
        catch (Exception exception)
        {
            // async void에서 예외가 새어나가면 앱이 그대로 죽는다.
            // 잡지 못한 예외가 없도록 여기서 전부 받는다.
            StatusText.Text = $"[{exception.GetType().Name}] {exception.Message}";
        }
        finally
        {
            // 성공이든 실패든 버튼은 반드시 돌아와야 한다.
            // 여기가 비면 한 번 실패한 앱은 다시 분석할 수 없는 상태로 남는다.
            AnalyzeButton.IsEnabled = true;
        }
    }

    // 노드 목록과 진단 목록의 선택은 서로 독립이다.
    // 한쪽을 고를 때 다른 쪽 선택을 지우지 않는다. 마지막에 고른 것이 편집기를 채운다.
    private void OnNodeSelected(object? sender, SelectionChangedEventArgs e)
    {
        // 목록을 비울 때도 이 이벤트가 온다. 그때는 보여줄 노드가 없다.
        if (NodeList.SelectedItem is not NodeItem item)
        {
            return;
        }

        StoryNode node = item.Node;

        ShowFile(node.FilePath, node.HeaderLine, 1);
    }

    private void OnDiagnosticSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DiagnosticList.SelectedItem is not DiagnosticItem item)
        {
            return;
        }

        VnDiagnostic diagnostic = item.Diagnostic;

        // 모든 진단이 파일의 한 지점을 가리키지는 않는다.
        //   - Yarn은 파일에 매이지 않은 진단에 "(External)" 같은 의사 이름을 쓴다.
        //   - 스키마 진단처럼 파일 전체를 두고 하는 말은 Line이 0이다.
        // 이런 것에 파일을 열려고 들면 엉뚱한 실패가 되므로, 진단 내용만 보여준다.
        if (!HasFilePosition(diagnostic))
        {
            FileBox.Text = Describe(diagnostic);
            return;
        }

        ShowFile(diagnostic.FilePath, diagnostic.Line, diagnostic.Column);
    }

    private static bool HasFilePosition(VnDiagnostic diagnostic)
    {
        return diagnostic.Line > 0 &&
               !string.IsNullOrWhiteSpace(diagnostic.FilePath) &&
               Path.IsPathRooted(diagnostic.FilePath);
    }

    private static string Describe(VnDiagnostic diagnostic)
    {
        string location = string.IsNullOrWhiteSpace(diagnostic.FilePath)
            ? "(위치 없음)"
            : diagnostic.FilePath;

        return
            $"[{diagnostic.Severity}] {diagnostic.Code}{Environment.NewLine}" +
            $"{location}{Environment.NewLine}{Environment.NewLine}" +
            $"{diagnostic.Message}{Environment.NewLine}{Environment.NewLine}" +
            "이 진단은 파일의 특정 위치를 가리키지 않습니다.";
    }

    private void ShowFile(string filePath, int line, int column)
    {
        try
        {
            // 매번 디스크에서 다시 읽는다. 캐시해두면 밖에서 파일을 고쳤을 때
            // 화면과 실제 파일이 어긋나고, 작가는 어긋난 줄 모른 채 읽게 된다.
            string text = File.ReadAllText(filePath);

            FileBox.Text = text;
            MoveCaretTo(text, line, column);
        }
        catch (Exception exception)
        {
            // 분석 뒤에 파일이 지워지거나 잠길 수 있다. 그래도 앱은 살아 있어야 한다.
            // 오류를 편집기 자리에 그대로 띄운다. 분석 요약을 덮어쓰지 않기 위해서다.
            FileBox.Text =
                $"파일을 열지 못했습니다.{Environment.NewLine}" +
                $"{filePath}{Environment.NewLine}{Environment.NewLine}" +
                $"[{exception.GetType().Name}] {exception.Message}";
        }
    }

    private void MoveCaretTo(string text, int oneBasedLine, int oneBasedColumn)
    {
        FileBox.CaretIndex = GetCaretIndex(text, oneBasedLine, oneBasedColumn);

        // ScrollToLine은 0부터 세고, 줄 수를 넘으면 예외를 던진다.
        int lineIndex = Math.Max(0, oneBasedLine - 1);

        // 스크롤은 새 Text로 레이아웃이 한 번 돌아간 뒤라야 줄 위치를 안다.
        // 여기서 바로 부르면 아직 만들어지지 않은 줄을 찾게 되므로 레이아웃 뒤로 미룬다.
        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    FileBox.ScrollToLine(lineIndex);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // 분석 이후 파일이 짧아졌다. 스크롤만 포기하고 내용은 그대로 둔다.
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 1부터 세는 줄·열을 <see cref="TextBox.CaretIndex"/>가 쓰는 문자 위치로 바꾼다.
    /// 열이 그 줄의 길이를 넘으면 줄 끝에 둔다. 다음 줄로 넘어가지 않는다.
    /// </summary>
    private static int GetCaretIndex(string text, int oneBasedLine, int oneBasedColumn)
    {
        int lineStart = GetLineStartIndex(text, oneBasedLine);
        int lineEnd = GetLineEndIndex(text, lineStart);
        int offset = Math.Max(0, oneBasedColumn - 1);

        return Math.Min(lineStart + offset, lineEnd);
    }

    /// <summary>
    /// 줄이 끝나는 문자 위치. CR을 줄 끝으로 보므로 CRLF 사이에 캐럿이 끼지 않는다.
    /// </summary>
    private static int GetLineEndIndex(string text, int lineStart)
    {
        for (int index = lineStart; index < text.Length; index++)
        {
            if (text[index] is '\r' or '\n')
            {
                return index;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// 1부터 세는 줄 번호를 <see cref="TextBox.CaretIndex"/>가 쓰는 문자 위치로 바꾼다.
    /// 화면에 올린 문자열을 직접 세므로 줄바꿈이 CRLF든 LF든 결과가 같다.
    /// 파일이 그만큼 길지 않으면 마지막으로 찾은 줄에 둔다.
    /// </summary>
    private static int GetLineStartIndex(string text, int oneBasedLine)
    {
        int target = Math.Max(1, oneBasedLine);
        int index = 0;

        for (int line = 1; line < target; line++)
        {
            int next = text.IndexOf('\n', index);

            if (next < 0)
            {
                break;
            }

            index = next + 1;
        }

        return Math.Min(index, text.Length);
    }

    // 목록에 담는 것은 Core가 준 순서 그대로다. 여기서 다시 정렬하거나 거르지 않는다.
    // AnalysisReport.Diagnostics는 이미 정렬되어 있고, 그 순서가 CLI 출력·골든 픽스처와
    // 같은 순서다. 뷰가 순서를 바꾸면 화면과 픽스처가 서로 다른 것을 말하게 된다.
    private void ShowResults(AnalysisReport report)
    {
        StatusText.Text =
            $"소스 파일 {report.SourceFiles.Count}개, " +
            $"노드 {report.Nodes.Count}개, " +
            $"진단 {report.Diagnostics.Count}개";

        SourceFileList.ItemsSource = report.SourceFiles;

        NodeList.ItemsSource = report.Nodes
            .Select(node => new NodeItem(node))
            .ToList();

        DiagnosticList.ItemsSource = report.Diagnostics
            .Select(diagnostic => new DiagnosticItem(diagnostic))
            .ToList();
    }

    private void ClearResults()
    {
        SourceFileList.ItemsSource = null;
        NodeList.ItemsSource = null;
        DiagnosticList.ItemsSource = null;
        FileBox.Text = string.Empty;
    }

    /// <summary>
    /// 목록에 보일 문장과 원본 <see cref="StoryNode"/>를 같이 들고 있는다.
    /// 목록에 문자열만 넣으면 선택했을 때 파일 경로와 줄 번호를 다시 알 길이 없다.
    /// ListBox는 항목을 그릴 때 ToString()을 쓰므로 별도 DataTemplate이 필요 없다.
    /// </summary>
    private sealed class NodeItem
    {
        public NodeItem(StoryNode node)
        {
            Node = node;
        }

        public StoryNode Node { get; }

        public override string ToString()
        {
            return $"{Node.Title}  ({Node.FilePath}:{Node.HeaderLine})";
        }
    }

    /// <summary>
    /// <see cref="NodeItem"/>과 같은 이유로 원본 진단을 들고 있는다.
    /// </summary>
    private sealed class DiagnosticItem
    {
        public DiagnosticItem(VnDiagnostic diagnostic)
        {
            Diagnostic = diagnostic;
        }

        public VnDiagnostic Diagnostic { get; }

        public override string ToString()
        {
            return
                $"[{Diagnostic.Severity}] {Diagnostic.Code}  " +
                $"{Diagnostic.FilePath}:{Diagnostic.Line}:{Diagnostic.Column}  " +
                $"{Diagnostic.Message}";
        }
    }
}
