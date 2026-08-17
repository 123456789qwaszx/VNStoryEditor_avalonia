using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Vn.App.Services;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>
/// 설정 노드를 편집한다. 조건을 만드는 유일한 자리다.
///
/// 변수 이름 후보는 <see cref="GameDefinition"/>이 공급한다. 이 화면은 게임의 변수를
/// 하나도 알지 못하고, 정의 파일이 없으면 후보 없이 작가가 직접 적는다.
/// 그래야 같은 도구를 다음 게임에 그대로 가져다 쓸 수 있다.
/// </summary>
public partial class SetNodeEditor : UserControl
{
    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;

    public SetNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) =>
        {
            if (!_building && _session is not null && _nodeId is not null)
            {
                _session.Editor.RenameNode(_nodeId, NameBox.Text ?? string.Empty);
            }
        };

        AddConditionButton.Click += (_, _) =>
        {
            if (_session is not null && _nodeId is not null)
            {
                // 이름은 빈칸으로 시작한다 (W47) — "새 조건"이 미리 채워져 있으면
                // 지우는 일부터 시켜야 한다. 자리 안내는 PlaceholderText가 한다.
                _session.Editor.AddCondition(_nodeId, string.Empty, string.Empty);
            }
        };

        AddAssignmentButton.Click += (_, _) => AddAssignment();

        AddSpeakerButton.Click += (_, _) => UiGuard.Run(_session, "화자 추가", () =>
        {
            // 편집 중 목록에 붙인다 — 저장본에서 다시 시작하면 방금 타이핑한(아직 저장 못 한)
            // 행이 통째로 사라져 "추가가 안 된다"로 보인다.
            EditingSpeakers().Add(new SpeakerSpec());

            // 빈 항목은 파일에 저장되지 않으므로 행만 즉시 보여 준다.
            RebuildSpeakers();
        });
    }

    /// <summary>
    /// 편집 중 화자 목록 — 저장 전(이름이 비어 파일에 못 들어간) 행을 포함한다.
    /// 화자는 노드가 아니라 게임 정의에 속하므로 <b>노드를 옮겨도 유지하고</b>,
    /// 다른 프로젝트를 열었을 때만 버린다.
    /// </summary>
    private List<SpeakerSpec>? _pendingSpeakers;

    /// <summary>편집 중 목록이 속한 프로젝트 경로.</summary>
    private string? _pendingSpeakersProject;

    /// <summary>화자 행을 다시 만드는 중 — 사라지는 칸의 LostFocus가 낡은 위치로 커밋하지 못하게.</summary>
    private bool _rebuildingSpeakers;

    internal void Attach(AuthoringSession session) => _session = session;

    internal void Show(string? nodeId)
    {
        _nodeId = nodeId;
        SyncPendingSpeakerScope();
        Rebuild();
    }

    /// <summary>
    /// 편집 중 화자 목록의 유효 범위를 맞춘다. 저장 전 프로젝트(null)가 경로를 얻는 것은
    /// 같은 프로젝트를 저장한 것이므로 유지하고, <b>다른 파일을 열었을 때만</b> 버린다.
    /// </summary>
    private void SyncPendingSpeakerScope()
    {
        string? path = _session?.ProjectPath;

        if (_pendingSpeakersProject is not null &&
            !string.Equals(_pendingSpeakersProject, path, StringComparison.Ordinal))
        {
            _pendingSpeakers = null;
        }

        _pendingSpeakersProject = path;
    }

    /// <summary>화면이 보여 주는 화자 목록. 없으면 저장본에서 한 번 만든다.</summary>
    private List<SpeakerSpec> EditingSpeakers() =>
        _pendingSpeakers ??= _session?.Definition.Speakers.ToList() ?? [];

    internal string? NodeId => _nodeId;

    internal void Rebuild()
    {
        if (_session is null || _session.Project.FindNode(_nodeId) is not SetNode node)
        {
            ConditionHost.Children.Clear();
            AssignmentHost.Children.Clear();
            return;
        }

        _building = true;

        try
        {
            NameBox.Text = node.Name;

            ConditionHost.Children.Clear();

            foreach (ConditionDefinition condition in node.Conditions)
            {
                ConditionHost.Children.Add(BuildConditionRow(condition));
            }

            AssignmentHost.Children.Clear();

            for (int index = 0; index < node.Assignments.Count; index++)
            {
                AssignmentHost.Children.Add(BuildAssignmentRow(node, index));
            }

            List<string> writerVariables = WriterVariableNames();
            int chapterOwned = _session.Definition.Variables.Count - writerVariables.Count;

            VariableHintText.Text = writerVariables.Count == 0
                ? $"{GameDefinition.FileName}이 없으면 변수 이름을 직접 적습니다."
                : chapterOwned > 0
                    // 뺀 사실을 말한다 — 조용히 사라지면 "왜 trust가 없지?"가 된다.
                    ? $"{GameDefinition.FileName}이 제안하는 변수 {writerVariables.Count}개를 쓸 수 있습니다. " +
                      $"챕터 스탯 {chapterOwned}개는 기획자(A계층) 것이라 여기서 바꾸지 않습니다 — " +
                      "스탯이 변하는 자리는 챕터 그래프의 간선뿐입니다."
                    : $"{GameDefinition.FileName}이 제안하는 변수 {writerVariables.Count}개를 쓸 수 있습니다.";

            RebuildSpeakers();
        }
        finally
        {
            _building = false;
        }
    }

    /// <summary>
    /// 작가가 쓸 수 있는 변수 이름 — 정의 파일의 variables에서 <b>A계층 스탯을 뺀 것</b>
    /// (2026-08-17 소유자: "둘은 서로 완전히 다른 계층이야"). 두 계층이 한 목록에 살기
    /// 때문에 가르는 일은 화면이 한다.
    /// </summary>
    private List<string> WriterVariableNames() => _session!.Definition.Variables
        .Select(item => item.Name)
        .Where(name => !_session.ChapterStatKeys.Contains(name))
        .ToList();

    // ── 화자 등록 (X5) — 저장은 언제나 game.definition.json이다 (D-4) ─────

    private void RebuildSpeakers()
    {
        List<SpeakerSpec> speakers = EditingSpeakers();
        _rebuildingSpeakers = true;

        try
        {
            SpeakerHost.Children.Clear();

            for (int index = 0; index < speakers.Count; index++)
            {
                SpeakerHost.Children.Add(BuildSpeakerRow(index));
            }

            if (speakers.Count == 0)
            {
                SpeakerHost.Children.Add(new TextBlock
                {
                    Text = "화자를 등록하면 대사 노드에서 드롭다운으로 고를 수 있습니다.",
                    FontSize = 11,
                    Opacity = 0.6
                });
            }
        }
        finally
        {
            _rebuildingSpeakers = false;
        }
    }

    private Control BuildSpeakerRow(int index)
    {
        SpeakerSpec speaker = EditingSpeakers()[index];

        var name = new TextBox
        {
            Text = speaker.Name,
            PlaceholderText = "대본에 적히는 화자명",
            FontSize = 12
        };

        var characterId = new TextBox
        {
            Text = speaker.CharacterId,
            PlaceholderText = "초상화 characterId",
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12
        };

        // 저작 중 "이 표정이 필요하다"의 진입점 — 표정을 정의하고, 없으면 이미지를 골라
        // 규약 경로로 복제해 즉시 등록한다.
        var expressions = new Button { Content = "표정…", FontSize = 10, Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(
            expressions,
            "이 캐릭터의 표정을 확인하고, 없는 표정은 이미지를 골라 추가합니다. characterId가 있어야 누를 수 있습니다.");

        // 활성 여부는 타이핑 즉시 따라온다. 행을 다시 만들 때까지 잠겨 있으면
        // 방금 적은 characterId가 무시된 것처럼 보인다.
        void SyncExpressionsEnabled() =>
            expressions.IsEnabled = !string.IsNullOrWhiteSpace(characterId.Text);

        SyncExpressionsEnabled();
        characterId.TextChanged += (_, _) => SyncExpressionsEnabled();

        expressions.Click += (_, _) => UiGuard.Run(_session, "표정 관리", () =>
            ShowExpressionsFlyout(expressions, (characterId.Text ?? string.Empty).Trim()));

        void Commit()
        {
            // 행이 사라지는 중이면 이 위치는 이미 낡았다 — 엉뚱한 행을 덮어쓰지 않는다.
            if (_building || _rebuildingSpeakers)
            {
                return;
            }

            UiGuard.Run(_session, "화자 저장", () =>
            {
                List<SpeakerSpec> editing = EditingSpeakers();

                if (index >= editing.Count)
                {
                    return;
                }

                var updated = new SpeakerSpec
                {
                    Name = (name.Text ?? string.Empty).Trim(),
                    CharacterId = (characterId.Text ?? string.Empty).Trim()
                };

                if (string.Equals(updated.Name, editing[index].Name, StringComparison.Ordinal) &&
                    string.Equals(updated.CharacterId, editing[index].CharacterId, StringComparison.Ordinal))
                {
                    return; // 바뀐 게 없으면 파일을 건드리지 않는다
                }

                // 편집 중 목록을 먼저 갱신한다 — 저장에 실패해도(프로젝트 미저장)
                // 타이핑이 화면과 목록에 남아 노드를 옮겼다 와도 그대로다.
                editing[index] = updated;

                // 저장에 성공해도 행을 다시 만들지 않는다. 다시 만들면 지금 쓰던 칸이
                // 사라져 이름 → characterId 탭 이동이 끊긴다(입력이 씹히는 것처럼 보인다).
                _session!.SaveSpeakers(editing);
            });
        }

        void CommitOnEnter(object? sender, Avalonia.Input.KeyEventArgs args)
        {
            if (args.Key == Avalonia.Input.Key.Enter)
            {
                Commit();
                args.Handled = true;
            }
        }

        // 타이핑 즉시 커밋한다 (W56) — 그래프의 노드 카드는 포커스를 가져가지 않아,
        // "이름 입력 → 곧장 노드 클릭" 흐름에서 LostFocus가 울리지 않고 저장이 새는
        // 버그가 있었다. 변경 없음 검사가 있어 중복 저장은 걸러진다.
        name.TextChanged += (_, _) => Commit();
        characterId.TextChanged += (_, _) => Commit();
        name.LostFocus += (_, _) => Commit();
        characterId.LostFocus += (_, _) => Commit();
        name.KeyDown += CommitOnEnter;
        characterId.KeyDown += CommitOnEnter;

        var remove = new Button { Content = "✕", FontSize = 10, Margin = new Thickness(6, 0, 0, 0) };

        remove.Click += (_, _) => UiGuard.Run(_session, "화자 삭제", () =>
        {
            List<SpeakerSpec> editing = EditingSpeakers();

            if (index >= editing.Count)
            {
                return;
            }

            editing.RemoveAt(index);
            _session!.SaveSpeakers(editing);

            // 행 개수가 바뀌었으니 여기서는 다시 그려야 한다.
            RebuildSpeakers();
        });

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto,Auto") };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(characterId, 1);
        Grid.SetColumn(expressions, 2);
        Grid.SetColumn(remove, 3);
        row.Children.Add(name);
        row.Children.Add(characterId);
        row.Children.Add(expressions);
        row.Children.Add(remove);

        return row;
    }

    // ── 표정 정의 + 스프라이트 복제 ─────────────────────────────────────────
    //
    // 초상화 폴더는 참고용이 아니라 복제본을 모으는 자리다: 여기서 이미지를 고르면
    // {root}/{char}/{variant}/{emotion}.png로 복제되고, 연결 권위가 폴더 규약이므로
    // (W-asset-02) 복제된 순간 등록도 끝난다. 이미 있으면 그냥 쓴다.

    private void ShowExpressionsFlyout(Control anchor, string characterId)
    {
        if (_session is null)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 6, MinWidth = 250, MaxWidth = 300 };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };

        panel.Children.Add(new TextBlock
        {
            Text = $"{characterId}의 표정",
            FontSize = 11,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });

        // 지금 있는 표정 — 폴더 규약 스캔 결과 그대로.
        Vn.Authoring.Assets.PortraitAssetEntry[] existing = _session.AssetLibrary.PortraitEntries
            .Where(entry => string.Equals(entry.Key.CharacterId, characterId, StringComparison.Ordinal) &&
                entry.FileExists)
            .OrderBy(entry => entry.Key.VariantKey, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.EmotionKey, StringComparer.Ordinal)
            .ToArray();

        if (existing.Length > 0)
        {
            foreach (var variantGroup in existing.GroupBy(entry => entry.Key.VariantKey, StringComparer.Ordinal))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"variant {variantGroup.Key}: " +
                        string.Join(" ", variantGroup.Select(entry => entry.Key.EmotionKey)),
                    FontSize = 10,
                    Opacity = 0.7,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
            }
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "아직 등록된 표정이 없습니다.",
                FontSize = 10,
                Opacity = 0.6
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "표정 추가 — 없는 번호를 적고 이미지를 고르면 규약 경로로 복제됩니다.",
            FontSize = 10,
            Opacity = 0.6,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var variantBox = new TextBox
        {
            Text = Vn.Authoring.Assets.PortraitKey.DefaultVariantKey,
            FontSize = 11,
            MinHeight = 24,
            Width = 60
        };
        ToolTip.SetTip(variantBox, "variant (a, b, c …)");

        var emotionBox = new TextBox
        {
            Text = Vn.Authoring.Assets.PortraitSpriteImporter.NextFreeEmotionKey(
                _session.AssetLibrary.PortraitKeys,
                characterId,
                Vn.Authoring.Assets.PortraitKey.DefaultVariantKey),
            FontSize = 11,
            MinHeight = 24,
            Width = 60,
            Margin = new Thickness(4, 0, 0, 0)
        };
        ToolTip.SetTip(emotionBox, "표정 번호 (01, 02 …) — 한 자리는 두 자리로 정규화됩니다.");

        var inputRow = new StackPanel { Orientation = Orientation.Horizontal };
        inputRow.Children.Add(new TextBlock
        {
            Text = "variant",
            FontSize = 10,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });
        inputRow.Children.Add(variantBox);
        inputRow.Children.Add(new TextBlock
        {
            Text = "표정",
            FontSize = 10,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0)
        });
        inputRow.Children.Add(emotionBox);
        panel.Children.Add(inputRow);

        var statusText = new TextBlock
        {
            FontSize = 10,
            Opacity = 0.75,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        panel.Children.Add(statusText);

        var pickButton = new Button { Content = "이미지 선택해 추가…", FontSize = 10, Padding = new Thickness(7, 2) };
        panel.Children.Add(pickButton);

        // variant를 바꾸면 다음 빈 번호도 그 variant 기준으로 따라온다.
        variantBox.LostFocus += (_, _) =>
        {
            emotionBox.Text = Vn.Authoring.Assets.PortraitSpriteImporter.NextFreeEmotionKey(
                _session.AssetLibrary.PortraitKeys, characterId, variantBox.Text);
            UpdateStatus();
        };
        emotionBox.LostFocus += (_, _) => UpdateStatus();

        void UpdateStatus()
        {
            if (_session.PortraitsRoot is not { } root)
            {
                statusText.Text = "초상화 폴더가 아직 없습니다. 복제본을 모을 폴더를 먼저 지정하세요.";
                pickButton.Content = "초상화 폴더 지정…";
                return;
            }

            var key = Vn.Authoring.Assets.PortraitKey.Normalize(
                characterId, variantBox.Text, emotionBox.Text);
            string target = Vn.Authoring.Assets.PortraitSpriteImporter.TargetPathFor(root, key);

            if (File.Exists(target))
            {
                statusText.Text = $"'{key}'는 이미 있습니다 — 그대로 쓰면 됩니다.";
                pickButton.Content = "이미 있는 표정입니다";
                pickButton.IsEnabled = false;
            }
            else
            {
                statusText.Text = $"'{key.ToRelativePath()}'가 아직 없습니다. 이미지를 고르면 이 이름으로 복제됩니다.";
                pickButton.Content = "이미지 선택해 추가…";
                pickButton.IsEnabled = true;
            }
        }

        pickButton.Click += async (_, _) => await UiGuard.RunAsync(_session, "표정 스프라이트 추가", async () =>
        {
            if (_session.PortraitsRoot is not { } root)
            {
                // 폴더부터 — 지정되면 같은 팝오버를 계속 쓸 수 있게 상태만 다시 계산한다.
                if (await AssetRootPicker.PickAsync(this, _session, backgrounds: false))
                {
                    _session.RefreshAssets();
                    UpdateStatus();
                }

                return;
            }

            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                _session.SetStatus("이 환경에서는 파일 선택 창을 열 수 없습니다.");
                return;
            }

            IReadOnlyList<Avalonia.Platform.Storage.IStorageFile> files = await storage.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "복제할 초상화 이미지 (PNG)",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new Avalonia.Platform.Storage.FilePickerFileType("PNG 이미지") { Patterns = ["*.png"] }
                    ]
                });

            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } sourcePath)
            {
                return;
            }

            Vn.Authoring.Assets.PortraitSpriteImporter.Imported imported =
                Vn.Authoring.Assets.PortraitSpriteImporter.Import(
                    root, sourcePath, characterId, variantBox.Text, emotionBox.Text);

            _session.RefreshAssets();
            _session.SetStatus(
                $"'{Path.GetFileName(sourcePath)}'를 '{imported.Key.ToRelativePath()}'로 복제해 등록했습니다.");
            flyout.Hide();
        });

        UpdateStatus();
        flyout.ShowAt(anchor);
    }

    private Control BuildConditionRow(ConditionDefinition condition)
    {
        var name = new TextBox { Text = condition.Name, PlaceholderText = "작가가 읽을 이름", FontSize = 12 };

        var expression = new TextBox
        {
            Text = condition.Expression,
            PlaceholderText = "게임이 평가할 식",
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono,Consolas")
        };

        void Commit()
        {
            if (!_building)
            {
                _session!.Editor.UpdateCondition(
                    condition.Id,
                    name.Text ?? string.Empty,
                    expression.Text ?? string.Empty);
            }
        }

        name.LostFocus += (_, _) => Commit();
        expression.LostFocus += (_, _) => Commit();

        var remove = new Button { Content = "✕", FontSize = 10, Margin = new Thickness(6, 0, 0, 0) };
        remove.Click += (_, _) => _session!.Editor.RemoveCondition(condition.Id);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto") };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(expression, 1);
        Grid.SetColumn(remove, 2);
        row.Children.Add(name);
        row.Children.Add(expression);
        row.Children.Add(remove);

        return row;
    }

    /// <summary>타입 드롭다운 항목 — X2가 만든 배열에 X7이 bool 한 줄을 더했다.</summary>
    private static readonly (string Type, string Label)[] VariableTypes =
    [
        (VariableAssignment.FloatType, "float (숫자)"),
        (VariableAssignment.BoolType, "bool (플래그)")
    ];

    private Control BuildAssignmentRow(SetNode node, int index)
    {
        VariableAssignment assignment = node.Assignments[index];

        // 값보다 타입이 먼저다 — 무엇을 담는 변수인지부터 정한다.
        var type = new ComboBox
        {
            ItemsSource = VariableTypes.Select(item => item.Label).ToList(),
            SelectedIndex = Math.Max(
                0,
                Array.FindIndex(VariableTypes, item =>
                    string.Equals(item.Type, assignment.Type, StringComparison.Ordinal))),
            FontSize = 11,
            MinWidth = 96,
            VerticalAlignment = VerticalAlignment.Center
        };

        var variable = new AutoCompleteBox
        {
            Text = assignment.Variable,
            PlaceholderText = "변수",
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0),
            // A계층 스탯은 후보에서 뺀다 (2026-08-17 소유자) — 정의 파일의 variables는 두
            // 계층을 한 목록에 담고 있어서, 그대로 쓰면 작가가 trust에 set을 걸 수 있다.
            // 스탯이 변하는 자리는 간선뿐이므로 화면이 그 규칙을 지킨다.
            ItemsSource = WriterVariableNames(),
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0
        };

        // Bool 플래그의 값은 수치가 아니라 On/Off다 (X7). 저장 값은 Yarn 문법 그대로
        // true/false 문자열이라 출력은 바뀌지 않는다.
        bool isBool = assignment.IsBool;

        var boolValue = new CheckBox
        {
            IsChecked = string.Equals(assignment.Value, "true", StringComparison.OrdinalIgnoreCase),
            Content = string.Equals(assignment.Value, "true", StringComparison.OrdinalIgnoreCase) ? "On" : "Off",
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            IsVisible = isBool,
            VerticalAlignment = VerticalAlignment.Center
        };

        var value = new TextBox
        {
            Text = assignment.Value,
            PlaceholderText = "초기값",
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            IsVisible = !isBool
        };

        // Set 편집 슬라이더의 변수별 범위 (X6). 비우면 기본 -5~+5다.
        var sliderMin = new TextBox
        {
            Text = assignment.SliderMin?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PlaceholderText = VariableAssignment.DefaultSliderMin.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 11,
            Width = 44
        };
        ToolTip.SetTip(sliderMin, "슬라이더 최솟값 — 편의 범위이며 직접 입력은 범위 밖도 됩니다.");

        var sliderMax = new TextBox
        {
            Text = assignment.SliderMax?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PlaceholderText = "+" + VariableAssignment.DefaultSliderMax.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Margin = new Thickness(2, 0, 0, 0),
            FontSize = 11,
            Width = 44
        };
        ToolTip.SetTip(sliderMax, "슬라이더 최댓값");

        static double? ParseRange(string? text) =>
            double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed)
                ? parsed
                : null;

        void Commit()
        {
            if (_building)
            {
                return;
            }

            string nextType = VariableTypes[Math.Max(0, type.SelectedIndex)].Type;
            bool nextIsBool = string.Equals(nextType, VariableAssignment.BoolType, StringComparison.Ordinal);

            List<VariableAssignment> next = node.Assignments.Select(item => item.Clone()).ToList();
            next[index] = new VariableAssignment
            {
                Variable = variable.Text ?? string.Empty,
                Value = nextIsBool
                    ? (boolValue.IsChecked == true ? "true" : "false")
                    : value.Text ?? string.Empty,
                Type = nextType,
                SliderMin = ParseRange(sliderMin.Text),
                SliderMax = ParseRange(sliderMax.Text)
            };

            _session!.Editor.SetAssignments(node.Id, next);
        }

        type.SelectionChanged += (_, _) =>
        {
            Commit();

            // 타입 전환은 행의 모양이 바뀌는 일이다 (W47) — On/Off 토글·초기값 칸이 즉시
            // 갈아끼워져야 하고, 조건 행의 편집 형태도 타입을 따른다. 콤보 이벤트 안에서
            // 자기 자신을 지우지 않도록 다음 UI 턴에 다시 만든다.
            if (!_building)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(Rebuild);
            }
        };
        variable.LostFocus += (_, _) => Commit();
        value.LostFocus += (_, _) => Commit();
        boolValue.IsCheckedChanged += (_, _) =>
        {
            boolValue.Content = boolValue.IsChecked == true ? "On" : "Off";
            Commit();
        };
        sliderMin.LostFocus += (_, _) => Commit();
        sliderMax.LostFocus += (_, _) => Commit();

        var remove = new Button { Content = "✕", FontSize = 10, Margin = new Thickness(6, 0, 0, 0) };

        remove.Click += (_, _) =>
        {
            List<VariableAssignment> next = node.Assignments.Select(item => item.Clone()).ToList();
            next.RemoveAt(index);
            _session!.Editor.SetAssignments(node.Id, next);
        };

        // Bool 플래그에는 슬라이더 범위가 의미 없다.
        sliderMin.IsVisible = !isBool;
        sliderMax.IsVisible = !isBool;

        var valueHost = new Panel();
        valueHost.Children.Add(value);
        valueHost.Children.Add(boolValue);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*,Auto,Auto,Auto") };
        Grid.SetColumn(type, 0);
        Grid.SetColumn(variable, 1);
        Grid.SetColumn(valueHost, 2);
        Grid.SetColumn(sliderMin, 3);
        Grid.SetColumn(sliderMax, 4);
        Grid.SetColumn(remove, 5);
        row.Children.Add(type);
        row.Children.Add(variable);
        row.Children.Add(valueHost);
        row.Children.Add(sliderMin);
        row.Children.Add(sliderMax);
        row.Children.Add(remove);

        return row;
    }

    private void AddAssignment()
    {
        if (_session?.Project.FindNode(_nodeId) is not SetNode node)
        {
            return;
        }

        List<VariableAssignment> next = node.Assignments.Select(item => item.Clone()).ToList();
        next.Add(new VariableAssignment());
        _session.Editor.SetAssignments(node.Id, next);
    }

}
