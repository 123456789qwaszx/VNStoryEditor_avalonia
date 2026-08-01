using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Services;

/// <summary>
/// 앱이 지금 무엇을 열고 있는지 아는 유일한 객체.
///
/// 편집 자체는 <see cref="Vn.Authoring.Editing.ProjectEditor"/>가 한다. 이 클래스는 그 위에
/// 파일 경로·저장 여부·선택 상태처럼 <em>도구</em>의 관심사만 얹는다.
/// 도메인이 파일 경로를 알 필요는 없고, 알게 되면 콘솔이나 테스트에서 쓰기 어려워진다.
/// </summary>
internal sealed class AuthoringSession
{
    private string _savedSnapshot;

    public AuthoringSession()
    {
        Editor = new ProjectEditor(NewProjectInstance());
        _savedSnapshot = ProjectJson.Write(Editor.Project);
        Editor.Changed += OnEditorChanged;
    }

    public ProjectEditor Editor { get; }

    public StoryProject Project => Editor.Project;

    /// <summary>아직 한 번도 저장하지 않았으면 null이다.</summary>
    public string? ProjectPath { get; private set; }

    public GameDefinition Definition { get; private set; } = GameDefinition.Empty;

    public string? SelectedNodeId { get; private set; }

    public StoryNode? SelectedNode => Project.FindNode(SelectedNodeId);

    public string StatusMessage { get; private set; } = "새 프로젝트입니다. 노드를 추가해 시작하세요.";

    /// <summary>저장한 뒤로 바뀐 것이 있는지. 실제 내용을 비교하므로 되돌리기로 원상복구하면 깨끗해진다.</summary>
    public bool IsDirty => !string.Equals(_savedSnapshot, ProjectJson.Write(Project), StringComparison.Ordinal);

    public event EventHandler<ProjectChangedEventArgs>? Changed;

    public event EventHandler? SelectionChanged;

    public void Select(string? nodeId)
    {
        if (string.Equals(SelectedNodeId, nodeId, StringComparison.Ordinal))
        {
            return;
        }

        SelectedNodeId = nodeId;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // 상태를 모두 맞춘 뒤에 알린다. 알린 다음에 상태를 바꾸면
    // 화면은 방금 지나간 값을 그린 채로 남는다.
    public void NewProject()
    {
        StoryProject project = NewProjectInstance();

        ProjectPath = null;
        Definition = GameDefinition.Empty;
        _savedSnapshot = ProjectJson.Write(project);
        StatusMessage = "새 프로젝트입니다. 노드를 추가해 시작하세요.";

        Editor.Replace(project);
        Select(project.Nodes.FirstOrDefault()?.Id);
    }

    public void Open(string path)
    {
        StoryProject project = ProjectJson.Load(path);

        ProjectPath = Path.GetFullPath(path);
        Definition = GameDefinition.LoadBeside(ProjectPath);
        _savedSnapshot = ProjectJson.Write(project);
        StatusMessage = $"{Path.GetFileName(ProjectPath)} · 노드 {project.Nodes.Count}개";

        AppSettingsService.SaveRecentProject(ProjectPath);

        Editor.Replace(project);
        Select(project.StartNodeId ?? project.Nodes.FirstOrDefault()?.Id);
    }

    public void Save(string? path = null)
    {
        string target = path ?? ProjectPath
            ?? throw new InvalidOperationException("저장할 경로가 없습니다.");

        ProjectJson.Save(target, Project);

        ProjectPath = Path.GetFullPath(target);
        _savedSnapshot = ProjectJson.Write(Project);
        Definition = GameDefinition.LoadBeside(ProjectPath);
        AppSettingsService.SaveRecentProject(ProjectPath);

        StatusMessage = $"{Path.GetFileName(ProjectPath)}에 저장했습니다.";
        Changed?.Invoke(this, new ProjectChangedEventArgs(ProjectChangeKind.Content));
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
        Changed?.Invoke(this, new ProjectChangedEventArgs(ProjectChangeKind.Content));
    }

    private void OnEditorChanged(object? sender, ProjectChangedEventArgs e)
    {
        // 선택하고 있던 노드가 사라졌다면 선택을 놓는다.
        if (SelectedNodeId is not null && Project.FindNode(SelectedNodeId) is null)
        {
            SelectedNodeId = Project.Nodes.FirstOrDefault()?.Id;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        Changed?.Invoke(this, e);
    }

    private static StoryProject NewProjectInstance()
    {
        return new StoryProject { Title = "새 프로젝트" };
    }
}
