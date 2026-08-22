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
/// <b>챕터의 설정 노드</b> — 그 챕터에서 작가가 쓰는 것들이 여기 모인다 (2026-08-17):
/// <b>조건</b>, <b>아이템·능력</b>, <b>화자</b>. 챕터마다 하나가 자동으로 서고 작가는
/// 그 안을 채울 뿐이다(만들거나 지우지 않는다).
///
/// <b>아이템·능력</b>은 예전에 "변수"라 부르던 것이다 (소유자: "시나리오 작가가 쓰는 건
/// 변수라기보다는 아이템, 혹은 능력이라고 하는 게 맞겠다"). 아이템은 개수로 늘고 줄고,
/// 능력은 있다/없다뿐이다 — 저장되는 자료형은 그대로 <c>float</c>/<c>bool</c>이라
/// 옛 프로젝트도 읽힌다.
///
/// <b>기획자 것은 여기서 고치지 않는다</b>: 챕터 스탯은 이름 후보에서 빠지고(스탯이
/// 변하는 자리는 챕터 간선뿐), 기획자가 정한 화자는 회색 고정으로 선다.
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
    }

    // 화자 편집 장치는 전부 폐지됐다 (2026-08-23 소유자) — 편집 중 목록(_pendingSpeakers,
    // 2026-08-17 폐지)에 이어 재진입 빗장(_rebuildingSpeakers)까지 사라졌다. 고칠 수 없는
    // 목록에는 "고치는 중"이라는 상태가 없다.

    internal void Attach(AuthoringSession session) => _session = session;

    internal void Show(string? nodeId)
    {
        _nodeId = nodeId;
        Rebuild();
    }

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

            // 아이템은 개수로 세고(약초 3개), 능력은 있다/없다뿐이다(자물쇠따기).
            // 기획자 스탯은 아예 말하지 않는다 — 작가에게 노출되어서는 안 되는 자료다.
            VariableHintText.Text =
                "아이템은 개수로 늘고 줄고, 능력은 있다/없다뿐입니다. " +
                "이 챕터 안에서만 살아서 다른 챕터와 섞이지 않습니다.";

            RebuildSpeakers();
        }
        finally
        {
            _building = false;
        }
    }

    /// <summary>
    /// 아이템·능력 이름 후보 — <b>이 챕터에 이미 있는 이름들뿐</b> (2026-08-17 소유자:
    /// "애초에 game.definition.json에서 오는 기획자용 스탯은 시나리오 작가에게 노출되어서는
    /// 안돼").
    ///
    /// 정의 파일에서 후보를 꺼내오던 길은 끊었다 — 거르는 것으로는 부족하다. 아이템·능력은
    /// <b>챕터 단위로만 산다</b>(짧은 스토리 단위를 상정한 설계라, 챕터를 넘어 널브러지면
    /// 곤란하다). 그래서 어휘의 범위도 이 챕터다. 새 이름은 그냥 적으면 된다.
    /// </summary>
    private List<string> WriterVariableNames() =>
        _session?.Project.FindNode(_nodeId) is SetNode node
            ? node.Assignments
                .Select(item => item.Variable)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList()
            : [];

    // ── 화자 (읽기 전용) ────────────────────────────────────────────────────

    /// <summary>
    /// 화자 목록 — <b>읽는 자리다</b> (2026-08-23 소유자: "작가가 더한 화자라는 개념도
    /// 없애는 게 맞을 것 같아 … 이런 캐릭터는 컨셉과 배경이 꼼꼼히 정해져야 하는데
    /// 작가가 임의로 추가한다는 게 좋아보이진 않아서").
    ///
    /// 이름·캐릭터키의 주인은 챕터 `화자` 시트와 `game.definition.json` 하나이고, 여기는
    /// 그것을 회색으로 비춘다. 표정만 살아 있다 — 표정의 주인은 에셋 폴더 규약이라
    /// 누구든 더할 수 있다.
    /// </summary>
    private void RebuildSpeakers()
    {
        SpeakerHost.Children.Clear();

        List<SpeakerSpec> planner = PlannerSpeakers();

        foreach (SpeakerSpec speaker in planner)
        {
            SpeakerHost.Children.Add(BuildPlannerSpeakerRow(speaker));
        }

        if (planner.Count == 0)
        {
            SpeakerHost.Children.Add(new TextBlock
            {
                Text = "등록된 화자가 없습니다 — 챕터 엑셀 `화자` 시트에 적으면 " +
                       "여기와 대사 노드 드롭다운에 함께 섭니다. " +
                       "안 적어도 화자 칸은 자유 입력이라 대본은 돕니다.",
                FontSize = 11,
                Opacity = 0.6,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
    }

    /// <summary>
    /// 기획자가 정한 화자 — <b>챕터 `화자` 시트 + 정의 파일 speakers</b> (2026-08-17 소유자
    /// 보고: "챕터엑셀에 화자목록이 있는데 반영이 안되네"). 등록의 주된 자리는 시트이므로
    /// 시트가 먼저 서고, 같은 이름은 한 번만 센다. 캐릭터키는 정의 파일 쪽이 알고 있으면
    /// 그걸 쓴다 — 초상화 매핑의 주인은 여전히 정의 파일이다.
    /// </summary>
    private List<SpeakerSpec> PlannerSpeakers()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var speakers = new List<SpeakerSpec>();
        IReadOnlyList<SpeakerSpec> defined = _session?.Definition.Speakers ?? [];

        foreach (string name in _session?.ChapterSpeakerNames ?? [])
        {
            if (name.Length > 0 && seen.Add(name))
            {
                // 캐릭터키는 <b>시트에 적힌 것이 먼저</b>다 (2026-08-17 소유자 보고) —
                // 그 칸이 존재하는 이유가 이것인데, 정의 파일이 늘 이기면 칸은 장식이
                // 된다. 시트가 비어 있으면(아무 말도 안 한 칸) 정의 파일 쪽을 쓴다.
                string? fromSheet =
                    _session?.ChapterSpeakerCharacterIds.GetValueOrDefault(name);

                speakers.Add(new SpeakerSpec
                {
                    Name = name,
                    CharacterId = string.IsNullOrWhiteSpace(fromSheet)
                        ? defined
                            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal))
                            ?.CharacterId ?? string.Empty
                        : fromSheet
                });
            }
        }

        foreach (SpeakerSpec speaker in defined)
        {
            if (speaker.Name.Length > 0 && seen.Add(speaker.Name))
            {
                speakers.Add(speaker);
            }
        }

        return speakers;
    }

    /// <summary>
    /// 기획자 화자 한 줄 — 회색 고정. 이름·캐릭터키는 잠기고 [표정]만 살아 있다.
    /// 고치려면 챕터 `화자` 시트나 정의 파일로 간다(값의 주인이 거기다).
    /// </summary>
    private Control BuildPlannerSpeakerRow(SpeakerSpec speaker)
    {
        var name = new TextBox
        {
            Text = speaker.Name,
            FontSize = 12,
            IsEnabled = false
        };

        var characterId = new TextBox
        {
            Text = speaker.CharacterId,
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            IsEnabled = false
        };

        // 캐릭터키가 없으면 표정을 붙일 대상이 없다. 예전에는 단추가 살아 있어서 누르면
        // "characterId가 없다"만 뜨고 아무 일도 안 났다 (2026-08-17 소유자 보고) — 이제
        // 잠그고, <b>어느 칸을 채우면 되는지</b>를 말한다. 작가 화자 줄과 같은 규칙이다.
        bool hasKey = !string.IsNullOrWhiteSpace(speaker.CharacterId);

        var expressions = new Button
        {
            Content = "표정",
            FontSize = 11,
            Margin = new Thickness(6, 0, 0, 0),
            IsEnabled = hasKey,
            [ToolTip.TipProperty] = hasKey
                ? "표정은 에셋 폴더 규약이 주인이라 누구든 더할 수 있습니다."
                : $"'{speaker.Name}'에 캐릭터키가 없어 표정을 붙일 대상이 없습니다. " +
                  "챕터 엑셀 `화자` 시트의 `캐릭터키` 칸을 채우면 여기서 바로 열립니다."
        };
        expressions.Click += (_, _) => UiGuard.Run(_session, "표정 관리", () =>
            ShowExpressionsFlyout(expressions, speaker.CharacterId));

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto,Auto"), Opacity = 0.55 };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(characterId, 1);
        Grid.SetColumn(expressions, 2);
        row.Children.Add(name);
        row.Children.Add(characterId);
        row.Children.Add(expressions);

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

        // 지금 고른 이름이 이미 있는가 — 단추 글자와 실제 쓰기가 같은 판단 하나를 쓴다.
        bool _overwriting = false;
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

            // 이미 있어도 막지 않는다 (2026-08-17 소유자 요청) — 그림을 고쳐 다시 넣는 것은
            // 저작 중 흔한 일이다. 대신 단추 글자가 "바꾼다"고 분명히 말하고, 직전 그림은
            // `.bak`으로 남는다.
            _overwriting = File.Exists(target);

            if (_overwriting)
            {
                statusText.Text = $"'{key}'가 이미 있습니다. 이미지를 고르면 그 자리를 바꿉니다 " +
                    "— 직전 그림은 같은 폴더에 .bak으로 남습니다.";
                pickButton.Content = "이미지 선택해 덮어쓰기…";
            }
            else
            {
                statusText.Text = $"'{key.ToRelativePath()}'가 아직 없습니다. 이미지를 고르면 이 이름으로 복제됩니다.";
                pickButton.Content = "이미지 선택해 추가…";
            }

            pickButton.IsEnabled = true;
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
                    root, sourcePath, characterId, variantBox.Text, emotionBox.Text, _overwriting);

            _session.RefreshAssets();
            _session.SetStatus(imported.Replaced
                ? $"'{imported.Key.ToRelativePath()}'를 '{Path.GetFileName(sourcePath)}'로 바꿨습니다 " +
                  "(직전 그림은 .bak)."
                : $"'{Path.GetFileName(sourcePath)}'를 '{imported.Key.ToRelativePath()}'로 복제해 등록했습니다.");
            flyout.Hide();
        });

        UpdateStatus();
        flyout.ShowAt(anchor);
    }

    /// <summary>비교 연산자 — 아이템(개수)에만 쓴다. 능력은 있다/없다뿐이라 부호가 없다.</summary>
    private static readonly string[] ConditionOperators = [">=", "<=", "==", ">", "<"];

    /// <summary>
    /// 조건 한 줄 — <b>대사노드의 Set과 같은 감각</b>으로 만든다 (2026-08-17 소유자):
    /// 만든 아이템·능력을 고르고, 부호를 고르고, 수치를 적는다. 식을 손으로 쓰던 칸은
    /// 없앴다 — 작가가 Yarn 문법을 알아야 할 이유가 없다.
    ///
    /// <b>능력에는 부호가 없다</b>(소유자 지시) — On/Off뿐이라 <c>&gt;=</c> 같은 것이 설 자리가
    /// 없었다. 값은 토글이고 식은 <c>== true/false</c>로 고정된다.
    ///
    /// 손으로 적어 둔 복합식(<c>and</c>·여러 항)은 <b>분해하지 않고 그대로 보여 준다</b> —
    /// 읽기 전용 칸으로 남겨 두는 편이 조용히 뭉개는 것보다 낫다.
    /// </summary>
    private Control BuildConditionRow(ConditionDefinition condition)
    {
        var name = new TextBox { Text = condition.Name, PlaceholderText = "작가가 읽을 이름", FontSize = 12 };

        var remove = new Button { Content = "✕", FontSize = 10, Margin = new Thickness(6, 0, 0, 0) };
        remove.Click += (_, _) => _session!.Editor.RemoveCondition(condition.Id);

        List<VariableAssignment> items = _session?.Project.FindNode(_nodeId) is SetNode owner
            ? owner.Assignments.Where(item => item.Variable.Length > 0).ToList()
            : [];

        // 식 → 세 칸. 못 나누면(복합식·수기 편집) 원문을 그대로 지킨다.
        bool decomposed = Vn.Authoring.Chapters.ConditionExpressionParser.TryDecomposeSingle(
            (condition.Expression ?? string.Empty).Replace("$", string.Empty, StringComparison.Ordinal),
            out string pickedName, out string pickedOperator, out string pickedValue);

        if (!decomposed && (condition.Expression ?? string.Empty).Trim().Length > 0)
        {
            var raw = new TextBox
            {
                Text = condition.Expression,
                IsReadOnly = true,
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 11,
                FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono,Consolas"),
                [ToolTip.TipProperty] = "여러 항을 묶은 식이라 칸으로 나누지 않았습니다. " +
                                        "고치려면 지우고 다시 만드세요."
            };

            var rawRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto") };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(raw, 1);
            Grid.SetColumn(remove, 2);
            rawRow.Children.Add(name);
            rawRow.Children.Add(raw);
            rawRow.Children.Add(remove);

            name.LostFocus += (_, _) =>
            {
                if (!_building)
                {
                    _session!.Editor.UpdateCondition(
                        condition.Id, name.Text ?? string.Empty, condition.Expression ?? string.Empty);
                }
            };

            return rawRow;
        }

        var target = new ComboBox
        {
            ItemsSource = items.Select(item => item.Variable).ToList(),
            SelectedItem = items.Any(item => item.Variable == pickedName) ? pickedName : null,
            PlaceholderText = items.Count == 0 ? "아이템·능력을 먼저 더하세요" : "아이템·능력",
            FontSize = 11,
            Margin = new Thickness(6, 0, 0, 0),
            MinWidth = 110
        };

        // 부호는 아이템일 때만. 능력은 == 고정이라 보여 줄 것이 없다.
        var comparison = new ComboBox
        {
            ItemsSource = ConditionOperators,
            SelectedItem = ConditionOperators.Contains(pickedOperator) ? pickedOperator : ">=",
            FontSize = 11,
            Margin = new Thickness(6, 0, 0, 0),
            MinWidth = 60
        };

        var value = new TextBox
        {
            Text = pickedValue,
            PlaceholderText = "수치",
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            MinWidth = 60
        };

        // 능력의 값은 On/Off다 — 식은 `== true` / `== false`가 된다.
        var toggle = new CheckBox
        {
            IsChecked = string.Equals(pickedOperator, "true", StringComparison.Ordinal),
            Content = string.Equals(pickedOperator, "true", StringComparison.Ordinal) ? "On" : "Off",
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        bool IsAbility() => items.FirstOrDefault(item =>
            string.Equals(item.Variable, target.SelectedItem as string, StringComparison.Ordinal))?.IsBool == true;

        void ApplyKind()
        {
            bool ability = IsAbility();
            comparison.IsVisible = !ability;
            value.IsVisible = !ability;
            toggle.IsVisible = ability;
        }

        void Commit()
        {
            if (_building || target.SelectedItem is not string picked || picked.Length == 0)
            {
                return;
            }

            string expression = IsAbility()
                ? $"${picked} == {(toggle.IsChecked == true ? "true" : "false")}"
                : $"${picked} {comparison.SelectedItem as string ?? ">="} {(value.Text ?? string.Empty).Trim()}";

            _session!.Editor.UpdateCondition(condition.Id, name.Text ?? string.Empty, expression);
        }

        ApplyKind();

        name.LostFocus += (_, _) => Commit();
        target.SelectionChanged += (_, _) =>
        {
            ApplyKind();
            Commit();
        };
        comparison.SelectionChanged += (_, _) => Commit();
        value.LostFocus += (_, _) => Commit();
        toggle.IsCheckedChanged += (_, _) =>
        {
            toggle.Content = toggle.IsChecked == true ? "On" : "Off";
            Commit();
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto") };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(target, 1);
        Grid.SetColumn(comparison, 2);
        Grid.SetColumn(value, 3);
        Grid.SetColumn(toggle, 3);
        Grid.SetColumn(remove, 4);
        row.Children.Add(name);
        row.Children.Add(target);
        row.Children.Add(comparison);
        row.Children.Add(value);
        row.Children.Add(toggle);
        row.Children.Add(remove);

        return row;
    }

    /// <summary>
    /// 종류 드롭다운 (2026-08-17 소유자: "시나리오 작가가 쓰는 건 변수라기보다는 아이템,
    /// 혹은 능력이라고 하는 게 맞겠다").
    ///
    /// 저장되는 값은 예전 그대로 <c>float</c>/<c>bool</c>이다 — 바뀐 것은 <b>이름이 뜻을
    /// 말하게 된 것</b>뿐이라 옛 프로젝트도 그대로 읽힌다. 숫자/플래그라는 자료형 이름은
    /// 작가에게 아무것도 설명하지 못했다: 무엇을 담는 칸인지가 이제 종류에 적혀 있다.
    /// </summary>
    private static readonly (string Type, string Label)[] VariableTypes =
    [
        (VariableAssignment.FloatType, "아이템 (개수)"),
        (VariableAssignment.BoolType, "능력 (보유)")
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
            PlaceholderText = "아이템·능력 이름",
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

        /// <summary>아이템의 초기값은 숫자다 — 숫자로 못 읽으면 0에서 시작한다.</summary>
        static string NumberOrZero(string? text)
        {
            string trimmed = (text ?? string.Empty).Trim();

            return double.TryParse(
                trimmed,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out _)
                ? trimmed
                : "0";
        }

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
                // 값 공간이 종류마다 다르다 — 능력은 true/false, 아이템은 숫자다.
                // 종류를 바꾸면 안 쓰는 쪽 칸의 값이 그대로 넘어와 아이템 초기값이
                // `false`로 적히던 버그가 있었다 (2026-08-17 소유자 보고). 새 종류가
                // 읽을 수 없는 값은 그 종류의 기본값으로 떨군다.
                Value = nextIsBool
                    ? (boolValue.IsChecked == true ? "true" : "false")
                    : NumberOrZero(value.Text),
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
