using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using Vn.App.Services;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>
/// 곡선 편집 분리 창 (2026-08-20 소유자: "별도의 그래프로 빼줄래") — 조작 콘솔 팝업
/// 안에서는 곡선 캔버스가 스크롤 껍데기(MaxWidth 340)에 갇히고 팝업 내용 교체와
/// 포인터가 엉켜 동작하지 않았다. 창이면 그 문제가 통째로 사라지고, 캔버스도 창을
/// 따라 커진다.
///
/// 규칙은 플라이아웃 시절 그대로다: 여는 순간 커맨드 소유 곡선을 보장하고
/// (<see cref="EaseCurveCommandActions.EnsureOwned"/>), 편집은 실시간(놓을 때마다
/// 커밋 — 무대 미리보기·재생이 바로 따라온다), 보관함과는 복사로만 오간다.
/// 창 하나가 살아 있는 동안 다른 커맨드의 [곡선 편집…]을 누르면 내용만 갈아탄다.
/// </summary>
internal sealed class EaseCurveWindow : Window
{
    private readonly TextBlock _header = new()
    {
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap
    };

    private readonly TextBlock _hint = new()
    {
        Text = "키 드래그 · 빈 자리 클릭 = 키 추가 · 핸들 = 기울기 (Shift = 한쪽만) — 놓는 순간 미리보기에 반영됩니다.",
        FontSize = 10,
        Opacity = 0.65,
        TextWrapping = TextWrapping.Wrap
    };

    private readonly EaseCurveEditor _editor = new();
    private readonly Button _deleteButton = new() { Content = "키 삭제", FontSize = 11, IsEnabled = false };
    private readonly ComboBox _libraryCombo = new() { FontSize = 11, MinWidth = 130 };
    private readonly Button _importButton = new() { Content = "가져오기", FontSize = 11, IsEnabled = false };
    private readonly TextBox _nameInput = new() { FontSize = 11, MinWidth = 120, PlaceholderText = "이름 (소문자·숫자·_)" };
    private readonly Button _saveButton = new() { Content = "보관함에 저장", FontSize = 11 };
    private readonly TextBlock _problem = new()
    {
        FontSize = 10,
        Foreground = Brushes.OrangeRed,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };

    private AuthoringSession? _session;
    private string _presentationNodeId = string.Empty;
    private string _commandId = string.Empty;
    private string _easeParameter = string.Empty;
    private string _ownedName = string.Empty;
    private Action? _onApplied;
    private IReadOnlyList<EaseCurve> _library = [];

    public EaseCurveWindow()
    {
        Title = "곡선 편집";
        Width = 640;
        Height = 480;
        MinWidth = 420;
        MinHeight = 360;
        ShowInTaskbar = true;

        _editor.CurveChanged += () => _deleteButton.IsEnabled = _editor.CanDeleteSelected;
        _editor.PointerReleased += (_, _) =>
        {
            _deleteButton.IsEnabled = _editor.CanDeleteSelected;
            Commit();
        };
        _deleteButton.Click += (_, _) =>
        {
            _editor.DeleteSelected();
            Commit();
        };

        _libraryCombo.SelectionChanged += (_, _) => _importButton.IsEnabled = _libraryCombo.SelectedIndex >= 0;
        ToolTip.SetTip(_importButton, "보관함 곡선을 이 커맨드의 곡선으로 복사합니다 — 이후 편집은 이 커맨드만의 것입니다.");
        _importButton.Click += (_, _) => ImportFromLibrary();

        ToolTip.SetTip(_saveButton, "지금 곡선의 이름 붙인 사본을 보관함에 남깁니다 — 커맨드 쪽은 그대로입니다.");
        _saveButton.Click += (_, _) => SaveToLibrary();

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _deleteButton, _libraryCombo, _importButton, _nameInput, _saveButton }
        };

        var layout = new DockPanel { Margin = new Thickness(10) };
        var top = new StackPanel { Spacing = 4, Children = { _header, _hint } };
        var bottom = new StackPanel { Spacing = 6, Children = { actions, _problem } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(bottom);
        layout.Children.Add(new Border
        {
            Margin = new Thickness(0, 8),
            Child = _editor
        });

        Content = layout;
    }

    /// <summary>이 창이 지금 편집하는 대상 — 재렌더 후 같은 커맨드로 다시 열렸는지 판별용.</summary>
    public string CommandId => _commandId;

    /// <summary>
    /// 대상 커맨드를 싣는다(처음 열기·다른 커맨드로 갈아타기 공용). 여는 순간 소유 곡선을
    /// 보장하므로 표준 이징이던 커맨드는 여기서 커스텀으로 전환된다.
    /// </summary>
    public void ShowFor(
        AuthoringSession session,
        string presentationNodeId,
        string commandId,
        string? ease,
        string easeParameter,
        Action onApplied,
        CurveKind curveKind = CurveKind.Motion)
    {
        _session = session;
        _presentationNodeId = presentationNodeId;
        _commandId = commandId;
        _easeParameter = easeParameter;
        _onApplied = onApplied;

        bool wasCustom = ease is ['@', ..];
        IReadOnlyList<CurveKey> initial = [];
        EaseKind bakedFrom = EaseKind.OutCubic;

        UiGuard.Run(session, "곡선 편집 열기", () =>
        {
            (_ownedName, initial, bakedFrom) = EaseCurveCommandActions.EnsureOwned(
                session.Editor, presentationNodeId, commandId, easeParameter, ease, curveKind);

            if (!wasCustom)
            {
                onApplied(); // 커맨드 텍스트가 @이름으로 바뀌었다 — 화면에 보여야 한다.
            }
        });

        if (_ownedName.Length == 0)
        {
            return;
        }

        Title = $"곡선 편집 — @{_ownedName}";
        string kindWord = curveKind == CurveKind.Oscillation ? "진동" : "이동";
        _header.Text = wasCustom
            ? $"이 커맨드의 {kindWord} 곡선 (@{_ownedName})"
            : $"이 커맨드의 {kindWord} 곡선 — {bakedFrom}에서 복사해 시작 (@{_ownedName})";
        _problem.IsVisible = false;
        _editor.Load(initial, curveKind);
        RefreshLibrary();
        _nameInput.Text = NextFreeLibraryName();
    }

    private void Commit()
    {
        if (_session is null)
        {
            return;
        }

        if (EaseCurve.ValidateKeys(_editor.Keys) is { } violation)
        {
            _problem.Text = violation;
            _problem.IsVisible = true;
            return;
        }

        _problem.IsVisible = false;
        UiGuard.Run(_session, "곡선 편집", () =>
        {
            _session.Editor.SetEaseCurve(_ownedName, _editor.Keys, ownerCommandId: _commandId);
            _onApplied?.Invoke();
        });
    }

    private void ImportFromLibrary()
    {
        if (_session is null ||
            _libraryCombo.SelectedIndex < 0 || _libraryCombo.SelectedIndex >= _library.Count)
        {
            return;
        }

        EaseCurve source = _library[_libraryCombo.SelectedIndex];
        UiGuard.Run(_session, "보관함에서 가져오기", () =>
        {
            EaseCurveCommandActions.CopyFromLibrary(
                _session.Editor, _presentationNodeId, _commandId, _easeParameter, source);
            _editor.Load(source.Keys);
            _onApplied?.Invoke();
        });
    }

    private void SaveToLibrary()
    {
        if (_session is null)
        {
            return;
        }

        string name = _nameInput.Text?.Trim() ?? string.Empty;

        if (!EaseCurve.IsValidName(name))
        {
            _problem.Text = "보관함 이름은 소문자·숫자·언더스코어만 됩니다.";
            _problem.IsVisible = true;
            return;
        }

        if (EaseCurve.ValidateKeys(_editor.Keys) is { } violation)
        {
            _problem.Text = violation;
            _problem.IsVisible = true;
            return;
        }

        _problem.IsVisible = false;
        UiGuard.Run(_session, "보관함에 저장", () =>
        {
            EaseCurveCommandActions.SaveToLibrary(_session.Editor, name, _editor.Keys);
            RefreshLibrary();
            _onApplied?.Invoke();
        });
    }

    private void RefreshLibrary()
    {
        _library = _session?.Project.EaseCurves.Where(curve => curve.IsLibrary).ToArray() ?? [];
        _libraryCombo.ItemsSource = _library.Select(curve => curve.Name).ToArray();
        _libraryCombo.PlaceholderText = _library.Count == 0 ? "보관함 비어 있음" : "보관함…";
        _libraryCombo.IsEnabled = _library.Count > 0;
        _importButton.IsEnabled = false;
    }

    private string NextFreeLibraryName()
    {
        for (int i = 1; ; i++)
        {
            string candidate = $"curve_{i}";

            if (!_library.Any(curve => string.Equals(curve.Name, candidate, StringComparison.Ordinal)))
            {
                return candidate;
            }
        }
    }
}
