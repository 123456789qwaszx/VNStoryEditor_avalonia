using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vn.App.Services;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Core.Story;

namespace Vn.App.Views;

public partial class AnalysisView : UserControl
{
    /// <summary>
    /// 지금 편집기에 올라와 있는 파일. 인코딩과 BOM을 여기서 그대로 들고 있다가 저장할 때 돌려준다.
    /// 파일이 아닌 것(진단 설명, 오류 메시지)을 띄웠을 때는 null이다.
    /// null이면 저장은 아무것도 하지 않는다. 설명 문구로 파일을 덮어쓰는 일이 없어야 한다.
    /// </summary>
    private StoryFile? _openFile;

    /// <summary>
    /// 마지막으로 읽거나 저장한 시점의 내용.
    /// 변경 여부를 플래그로 들고 있지 않고 이것과 비교해서 판단한다.
    /// 플래그는 "프로그램이 넣은 글자"와 "사람이 친 글자"를 구별하려고 이벤트 순서에
    /// 기대게 되는데, 그 가정이 틀리면 저장하지 않은 변경을 조용히 놓친다.
    /// </summary>
    private string _savedText = string.Empty;

    public AnalysisView()
    {
        InitializeComponent();

        BoxList.LineSelected += OnBoxLineSelected;
    }

    private bool HasUnsavedChanges =>
        _openFile is not null &&
        !string.Equals(FileBox.Text ?? string.Empty, _savedText, StringComparison.Ordinal);

    private async void OnAnalyzeClick(object? sender, RoutedEventArgs e)
    {
        await RunAnalysisAsync();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await SaveAsync();
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.S || e.KeyModifiers != KeyModifiers.Control)
        {
            return;
        }

        e.Handled = true;
        await SaveAsync();
    }

    private void OnFileTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateFileHeader();
    }

    private async Task SaveAsync()
    {
        // 열린 파일이 없으면 아무것도 하지 않는다.
        if (_openFile is null)
        {
            return;
        }

        string text = FileBox.Text ?? string.Empty;

        try
        {
            // File.WriteAllText를 직접 쓰지 않는다. 그러면 BOM과 인코딩이 조용히 바뀌어
            // 고치지도 않은 줄까지 diff에 뜬다. 읽을 때 본 형태를 그대로 돌려준다.
            StoryFileService.Write(_openFile.Path, text, _openFile);
        }
        catch (Exception exception)
        {
            // 저장에 실패해도 편집기 내용은 건드리지 않는다. 사람이 친 글자가 아직 그 안에 있다.
            StatusText.Text =
                $"저장하지 못했습니다. [{exception.GetType().Name}] {exception.Message}";

            // 실패했는데 다시 분석하면 방금 실패가 성공처럼 보인다.
            return;
        }

        _savedText = text;
        UpdateFileHeader();

        await RunAnalysisAsync();
    }

    private void UpdateFileHeader()
    {
        if (_openFile is null)
        {
            FileHeaderText.Text = "파일";
            return;
        }

        string mark = HasUnsavedChanges
            ? " *"
            : string.Empty;

        FileHeaderText.Text = $"파일 — {Path.GetFileName(_openFile.Path)}{mark}";
    }

    // 분석은 Yarn 전체를 컴파일하므로 프로젝트가 커지면 눈에 띄게 오래 걸린다.
    // UI 스레드에서 돌리면 그동안 창이 통째로 멈춘다.
    //
    // Vn.Core에는 비동기 API를 두지 않는다. Core는 UI를 모르는 동기 라이브러리로 두고,
    // 어느 스레드에서 부를지는 부르는 쪽이 정한다. 그래서 감싸는 일은 여기서만 한다.
    private async Task RunAnalysisAsync()
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
            // 이 메서드를 부르는 곳은 async void 핸들러다.
            // 예외가 거기까지 새어나가면 앱이 그대로 죽으므로 여기서 전부 받는다.
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

        // 두 탭이 같은 노드를 보게 한다.
        // 박스 탭은 평평한 Lines가 아니라 분기 트리를 그린다. 평평한 목록에서는
        // 선택지 갈래 안의 명령이 다음 선택지에 붙어 보인다.
        BoxList.Show(node.Body);

        ShowFile(node.FilePath, node.HeaderLine, 1);
    }

    /// <summary>
    /// 박스를 고르면 텍스트 탭이 그 줄을 가리킨다. 탭을 자동으로 바꾸지는 않는다.
    /// 바꿔버리면 박스를 훑어보는 동안 한 번 누를 때마다 목록에서 튕겨 나간다.
    ///
    /// 같은 파일이 이미 열려 있으면 다시 읽지 않고 캐럿만 옮긴다.
    /// 다시 읽으면 저장하지 않은 편집이 사라진다.
    /// </summary>
    private void OnBoxLineSelected(object? sender, StoryLine line)
    {
        bool sameFile =
            _openFile is not null &&
            string.Equals(_openFile.Path, line.FilePath, StringComparison.OrdinalIgnoreCase);

        if (sameFile)
        {
            MoveCaretTo(FileBox.Text ?? string.Empty, line.Line, line.Column);
            return;
        }

        ShowFile(line.FilePath, line.Line, line.Column);
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
            ShowMessageInEditor(Describe(diagnostic));
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
            StoryFile file = StoryFileService.Read(filePath);

            _openFile = file;
            _savedText = file.Text;

            // 새 줄을 넣을 때 이 파일이 쓰던 줄바꿈을 그대로 쓴다.
            // 기본값은 Environment.NewLine이라, LF 파일에 한 줄 넣으면 그 줄만 CRLF가 되고
            // 고친 적 없는 자리에 diff가 생긴다.
            FileBox.NewLine = NewLineFor(file.LineEndings);
            FileBox.Text = file.Text;

            UpdateFileHeader();
            MoveCaretTo(file.Text, line, column);
        }
        catch (Exception exception)
        {
            // 분석 뒤에 파일이 지워지거나 잠길 수 있다. 그래도 앱은 살아 있어야 한다.
            // 오류를 편집기 자리에 그대로 띄운다. 분석 요약을 덮어쓰지 않기 위해서다.
            ShowMessageInEditor(
                $"파일을 열지 못했습니다.{Environment.NewLine}" +
                $"{filePath}{Environment.NewLine}{Environment.NewLine}" +
                $"[{exception.GetType().Name}] {exception.Message}");
        }
    }

    /// <summary>
    /// 파일이 아닌 것을 편집기 자리에 띄운다. 진단 설명이나 오류 메시지가 그렇다.
    /// 열린 파일을 지우므로 이 상태에서 저장을 눌러도 아무 일도 일어나지 않는다.
    /// 설명 문구가 파일로 저장되면 원고가 사라진다.
    /// </summary>
    private void ShowMessageInEditor(string message)
    {
        _openFile = null;
        _savedText = string.Empty;
        FileBox.Text = message;
        UpdateFileHeader();
    }

    private static string NewLineFor(LineEndingStyle style)
    {
        return style switch
        {
            LineEndingStyle.Lf => "\n",
            LineEndingStyle.CrLf => "\r\n",
            LineEndingStyle.Cr => "\r",

            // 줄바꿈이 없거나 섞여 있으면 근거가 없다. 플랫폼 기본값을 쓴다.
            _ => Environment.NewLine
        };
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
                catch (Exception)
                {
                    // 분석 이후 파일이 짧아졌거나, 텍스트 탭이 아직 화면에 올라오지 않아
                    // 줄 배치가 없는 상태다. 스크롤은 보기 편하자고 하는 것이므로
                    // 여기서 앱을 죽이지 않는다. 캐럿은 이미 옮겨 놓았다.
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

    // 편집기는 비우지 않는다. 저장하면 곧바로 다시 분석하는데, 여기서 비우면
    // 방금 저장한 사람이 보던 파일과 캐럿 위치가 통째로 사라진다.
    private void ClearResults()
    {
        SourceFileList.ItemsSource = null;
        NodeList.ItemsSource = null;
        DiagnosticList.ItemsSource = null;
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
