using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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

    /// <summary>
    /// 조건 표의 대상 칸(아이템·능력 드롭다운)을 제자리에서 다시 읽는 손잡이들.
    /// 개명 전파가 조건식을 갈았을 때 행을 다시 만들지 않고 값만 맞추는 데 쓴다.
    /// </summary>
    private readonly List<Action> _conditionTargets = new();

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

        // ⚠ 추가 단추가 종류별로 갈렸다 (2026-08-23 소유자: "조건 추가와 변수 추가할 시에
        // 현재는 bool과 int 타입이 서로 오고가고 있는데 … 오히려 직관적이지 않아서 불편하다").
        // 종류는 <b>태어날 때</b> 정해지고 그 뒤로 바뀌지 않는다 — 행이 모양을 갈아입지
        // 않으므로 값 칸이 초기화되는 일도, "지금 이게 무슨 종류였지"도 없다.
        // 잘못 만들었으면 지우고 다시 만든다(한 줄짜리 일이다).
        AddItemConditionButton.Click += (_, _) => AddCondition(ability: false);
        AddAbilityConditionButton.Click += (_, _) => AddCondition(ability: true);
        AddItemButton.Click += (_, _) => AddAssignment(ability: false);
        AddAbilityButton.Click += (_, _) => AddAssignment(ability: true);
    }

    // 화자 편집 장치는 전부 폐지됐다 (2026-08-23 소유자) — 편집 중 목록(_pendingSpeakers,
    // 2026-08-17 폐지)에 이어 재진입 빗장(_rebuildingSpeakers)까지 사라졌다. 고칠 수 없는
    // 목록에는 "고치는 중"이라는 상태가 없다.

    internal void Attach(AuthoringSession session)
    {
        _session = session;

        // 기획자의 화자 목록이 바뀌면 <b>그 자리에서</b> 다시 선다 (2026-08-26 소유자:
        // "바로바로 반영이 됐으면 해"). 예전에는 이 노드를 다시 골라야 새 목록이 왔다.
        //
        // ⚠ 여기서 다시 세우는 것은 <b>읽기 전용 표 하나</b>다(등록 화자는 회색 고정) —
        //   사람이 타이핑하던 칸을 파괴하는 그 위험(2026-08-24 성능 규칙 ⑤)이 없다.
        //   조건·아이템 표는 안 건드린다: 그쪽은 손이 머무는 자리다.
        session.DefinitionChanged += (_, _) => RebuildSpeakers();
    }

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

            RebuildConditions(node);
            RebuildAssignments(node);

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

    // ── 표 (엑셀처럼) ───────────────────────────────────────────────────────
    //
    // 소유자 지시 (2026-08-23) — "index 순번과 구분을 엑셀처럼 가진채로 잘 정리되어서
    // 가독성 좋게 보여야 한다." 줄이 그냥 쌓여 있으면 어느 칸이 무엇인지 매번 다시 읽어야
    // 한다: 머리글이 그것을 한 번만 말하고, 순번이 "몇 개인지"와 "몇 번째인지"를 준다.
    //
    // ⚠ 머리글과 몸통은 <b>열 정의 문자열 하나</b>를 나눠 쓴다. 둘이 갈리면 칸이 어긋나고,
    // 어긋난 표는 없는 것만 못하다.

    // ⚠⚠ <b>열 정의 규칙 셋</b> — 어기면 화면이 조용히 어긋난다(오류도 경고도 없다).
    //
    // ① <b>`Auto` 열을 쓰지 않는다.</b> 머리글 격자에는 ✕ 단추가 없어 그 `Auto`가 0으로,
    //    줄 격자에서는 32로 풀린다 — 그만큼 별(`*`) 열이 달라져 머리글과 몸통의 칸이 통째로
    //    어긋난다. 별 하나 말고는 전부 픽셀이어야 언제나 같게 풀린다.
    //
    // ② <b>칸은 컨트롤의 최소 폭보다 넓어야 한다.</b> Avalonia는 칸을 넘는 컨트롤을 잘라내지
    //    않는다 — Fluent의 `ComboBox`·`TextBox`는 최소 폭이 64px쯤이라, 52px 칸에 넣으면
    //    이웃 위로 그려진다(2026-08-23 소유자 보고). 그래서 값 칸은 68 아래로 두지 않는다.
    //
    // ③ <b>이름 칸을 뺀 나머지의 합이 표마다 같아야 한다</b> (= <see cref="RestOfRow"/>).
    //    이름이 유일한 별 열이므로, 나머지 합이 같으면 <b>탭을 옮겨도 첫 칸이 제자리에 선다</b>
    //    (2026-08-23 소유자: "뒤쪽은 몰라도 첫번째칸이 어긋나면 가독성이 너무 심하게
    //    훼손된다"). 열을 고칠 때는 이 합을 맞춰 놓고 고친다.

    /// <summary>순번 + 이름 뒤의 모든 열을 더한 값. 표마다 이 값이 같아야 이름 칸이 맞는다.</summary>
    private const double RestOfRow = 314;

    private static readonly IBrush TableLine = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
    private static readonly IBrush TableHeaderFill = new SolidColorBrush(Color.FromArgb(22, 128, 128, 128));
    private static readonly IBrush TableStripe = new SolidColorBrush(Color.FromArgb(12, 128, 128, 128));

    /// <summary>칸들을 열 정의에 순서대로 앉힌다. 이름은 겹침 고정이 이 격자를 찾는 표식이다.</summary>
    internal const string TableRowName = "TableRow";

    private static Grid Cells(string columns, IReadOnlyList<Control> cells)
    {
        var grid = new Grid
        {
            Name = TableRowName,
            ColumnDefinitions = new ColumnDefinitions(columns)
        };

        for (int index = 0; index < cells.Count; index++)
        {
            Grid.SetColumn(cells[index], index);
            grid.Children.Add(cells[index]);
        }

        return grid;
    }

    private static TextBlock HeaderCell(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.7,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>
    /// 위 규칙 셋을 열 정의가 지키는지 본다 — 어기면 <b>그 자리에서</b> 터진다.
    /// 주석으로만 적어 두면 다음 사람이 열 하나를 고치다 조용히 어긋뜨린다.
    /// </summary>
    private static void VerifyColumns(string columns)
    {
        string[] parts = columns.Split(',');
        int stars = 0;
        double fixedSum = 0;

        foreach (string part in parts)
        {
            if (part.Contains('*', StringComparison.Ordinal))
            {
                stars++;
                continue;
            }

            fixedSum += double.Parse(part, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (stars != 1)
        {
            throw new InvalidOperationException(
                $"표의 열 정의 '{columns}'에 별(*) 열이 {stars}개다 — 이름 칸 하나여야 한다(규칙 ①·③).");
        }

        if (Math.Abs(fixedSum - RestOfRow) > 0.01)
        {
            throw new InvalidOperationException(
                $"표의 열 정의 '{columns}'의 고정 열 합이 {fixedSum}다 — {RestOfRow}여야 " +
                "탭을 옮겨도 이름 칸이 제자리에 선다(규칙 ③).");
        }
    }

    /// <summary>순번 — 표 안에서만 뜻이 있는 번호다(저장되는 값이 아니다).</summary>
    private static TextBlock IndexCell(int number) => new()
    {
        Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        FontSize = 10,
        Opacity = 0.45,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Right,
        Margin = new Thickness(0, 0, 6, 0)
    };

    /// <summary>
    /// 표 하나 — 제목 · 머리글 · 줄들. <b>순번은 표가 붙인다</b>(1부터). 줄이 없으면
    /// 머리글도 세우지 않는다: 빈 표는 "무엇이 없는지"만 크게 말할 뿐이다.
    /// </summary>
    private static Control BuildTable(
        string title,
        string columns,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<Control>> rows,
        string emptyNote)
    {
        VerifyColumns(columns);

        var stack = new StackPanel { Spacing = 0 };

        stack.Children.Add(new TextBlock
        {
            Text = rows.Count == 0 ? title : $"{title} ({rows.Count})",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        if (rows.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = emptyNote,
                FontSize = 10,
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap
            });

            return stack;
        }

        var body = new StackPanel { Spacing = 0 };

        body.Children.Add(new Border
        {
            Background = TableHeaderFill,
            BorderBrush = TableLine,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 3),
            Child = Cells(columns, [HeaderCell("#"), .. headers.Select(HeaderCell)])
        });

        for (int index = 0; index < rows.Count; index++)
        {
            body.Children.Add(new Border
            {
                Background = index % 2 == 1 ? TableStripe : Brushes.Transparent,
                BorderBrush = TableLine,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 2),
                Child = Cells(columns, [IndexCell(index + 1), .. rows[index]])
            });
        }

        stack.Children.Add(new Border
        {
            BorderBrush = TableLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = body
        });

        return stack;
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
    /// 이름·캐릭터키의 주인은 <b>챕터 그래프의 [화자] 탭</b>(→ `game.definition.json`)
    /// 하나이고, 여기는 그것을 회색으로 비춘다. 표정만 살아 있다 — 표정의 주인은 에셋 폴더
    /// 규약이라 누구든 더할 수 있다.
    /// </summary>
    private const string SpeakerColumns = "26,*,232,56";

    private void RebuildSpeakers()
    {
        SpeakerHost.Children.Clear();

        SpeakerHost.Children.Add(BuildTable(
            "등록 화자",
            SpeakerColumns,
            ["이름", "캐릭터키", string.Empty],
            PlannerSpeakers().Select(BuildPlannerSpeakerCells).ToList(),
            "등록된 화자가 없습니다 — 챕터 그래프의 [화자] 탭에서 더하면 여기와 대사 노드 " +
            "드롭다운에 함께 섭니다(모든 챕터가 같은 목록입니다). 안 적어도 화자 칸은 " +
            "자유 입력이라 대본은 돕니다."));
    }

    /// <summary>
    /// 기획자가 정한 화자 — <b>`game.definition.json`의 speakers 하나</b> (2026-08-23).
    ///
    /// 챕터 `화자` 시트와 합쳐 보던 길은 시트가 폐지되면서 함께 사라졌다. 이름과 캐릭터키가
    /// 같은 배열에 있으므로 "시트가 먼저냐 정의가 먼저냐"라는 물음도 없다 — 집이 하나다.
    /// </summary>
    private IReadOnlyList<SpeakerSpec> PlannerSpeakers() => _session?.Definition.Speakers ?? [];

    /// <summary>
    /// 기획자 화자 한 줄 — 회색 고정. 이름·캐릭터키는 잠기고 [표정]만 살아 있다.
    /// 고치려면 챕터 그래프 [화자] 탭으로 간다(값의 주인이 거기다).
    /// </summary>
    private IReadOnlyList<Control> BuildPlannerSpeakerCells(SpeakerSpec speaker)
    {
        var name = new TextBox
        {
            Text = speaker.Name,
            FontSize = 12,
            Margin = new Thickness(0, 0, 4, 0),
            IsEnabled = false
        };

        var characterId = new TextBox
        {
            Text = speaker.CharacterId,
            Margin = new Thickness(0, 0, 4, 0),
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
            IsEnabled = hasKey,
            [ToolTip.TipProperty] = hasKey
                ? "표정은 에셋 폴더 규약이 주인이라 누구든 더할 수 있습니다."
                : $"'{speaker.Name}'에 캐릭터키가 없어 표정을 붙일 대상이 없습니다. " +
                  "챕터 그래프 [화자] 탭에서 `캐릭터키`를 채우면 여기서 바로 열립니다."
        };
        expressions.Click += (_, _) => UiGuard.Run(_session, "표정 관리", () =>
            ShowExpressionsFlyout(expressions, speaker.CharacterId));

        return [name, characterId, expressions];
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
                    TextWrapping = TextWrapping.Wrap
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
            TextWrapping = TextWrapping.Wrap
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
            TextWrapping = TextWrapping.Wrap
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

    private const string ConditionColumns = "26,*,112,72,72,32";
    private const string RawConditionColumns = "26,*,256,32";


    /// <summary>
    /// 조건 표 셋 — <b>아이템 조건 · 능력 조건 · 직접 적은 식</b> (2026-08-23).
    ///
    /// 종류를 섞어 한 표에 두면 절반이 빈 칸이 된다(아이템에는 부호·값이, 능력에는 On/Off가
    /// 있다). 갈라 두면 각 표가 자기 열만 갖고, 행이 태어난 뒤 <b>모양을 갈아입지 않는다</b> —
    /// 소유자가 짚은 "bool과 int가 서로 오고가는" 불편의 뿌리가 그 갈아입음이었다.
    ///
    /// <b>직접 적은 식</b>은 칸으로 못 나누는 복합식(<c>and</c>·여러 항)이다. 조용히 뭉개지
    /// 않고 읽기 전용으로 세워 둔다 — 어디서 왔는지 모를 조건이 목록에서 사라지는 것이
    /// 가장 나쁘다.
    /// </summary>
    private void RebuildConditions(SetNode node)
    {
        ConditionHost.Children.Clear();
        _conditionTargets.Clear();

        List<VariableAssignment> items = node.Assignments
            .Where(item => item.Variable.Length > 0)
            .ToList();

        var itemRows = new List<IReadOnlyList<Control>>();
        var abilityRows = new List<IReadOnlyList<Control>>();
        var rawRows = new List<IReadOnlyList<Control>>();

        foreach (ConditionDefinition condition in node.Conditions)
        {
            bool decomposed = Vn.Authoring.Chapters.ConditionExpressionParser.TryDecomposeSingle(
                (condition.Expression ?? string.Empty).Replace("$", string.Empty, StringComparison.Ordinal),
                out string pickedName, out string pickedOperator, out string pickedValue);

            if (!decomposed && (condition.Expression ?? string.Empty).Trim().Length > 0)
            {
                rawRows.Add(BuildRawConditionCells(condition));
                continue;
            }

            // 종류는 가리키는 아이템·능력이 정한다. 그것이 사라졌으면 부호가 말해 준다
            // (`== true`는 능력의 것이다) — 어느 쪽으로도 못 정하면 아이템으로 본다.
            bool ability = items
                    .FirstOrDefault(item =>
                        string.Equals(item.Variable, pickedName, StringComparison.Ordinal))?.IsBool
                ?? (pickedOperator is "true" or "false");

            if (ability)
            {
                abilityRows.Add(BuildAbilityConditionCells(condition, pickedName, pickedOperator));
            }
            else
            {
                itemRows.Add(BuildItemConditionCells(condition, pickedName, pickedOperator, pickedValue));
            }
        }

        ConditionHost.Children.Add(BuildTable(
            "아이템 조건", ConditionColumns, ["이름", "아이템", "부호", "값"], itemRows,
            "없습니다. [＋ 아이템 조건]으로 만듭니다 — 아이템이 하나는 있어야 합니다."));

        ConditionHost.Children.Add(BuildTable(
            "능력 조건", ConditionColumns, ["이름", "능력", "상태", ""], abilityRows,
            "없습니다. [＋ 능력 조건]으로 만듭니다 — 능력이 하나는 있어야 합니다."));

        // 없으면 아예 내놓지 않는다 — 대부분의 판에는 하나도 없는 표다.
        if (rawRows.Count > 0)
        {
            ConditionHost.Children.Add(BuildTable(
                "직접 적은 식 (읽기 전용)", RawConditionColumns, ["이름", "식"], rawRows,
                string.Empty));
        }

        AddItemConditionButton.IsEnabled = items.Any(item => !item.IsBool);
        AddAbilityConditionButton.IsEnabled = items.Any(item => item.IsBool);

        ToolTip.SetTip(AddItemConditionButton, AddItemConditionButton.IsEnabled
            ? "고른 아이템의 개수를 부호와 수치로 견줍니다."
            : "[아이템·능력] 탭에서 아이템을 먼저 만드세요.");
        ToolTip.SetTip(AddAbilityConditionButton, AddAbilityConditionButton.IsEnabled
            ? "고른 능력이 있는지/없는지를 봅니다."
            : "[아이템·능력] 탭에서 능력을 먼저 만드세요.");
    }

    /// <summary>조건 이름 칸 — 작가가 읽을 이름. 세 종류가 같은 것을 쓴다.</summary>
    private static TextBox ConditionNameBox(ConditionDefinition condition, Action commit)
    {
        var name = new TextBox
        {
            Text = condition.Name,
            PlaceholderText = "작가가 읽을 이름",
            FontSize = 12,
            MinWidth = 80,
            Margin = new Thickness(0, 0, 4, 0)
        };

        name.LostFocus += (_, _) => commit();
        return name;
    }

    private Button ConditionRemoveButton(ConditionDefinition condition)
    {
        var remove = new Button
        {
            Content = "✕",
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            [ToolTip.TipProperty] = "이 조건을 지웁니다. 대사 노드에서 쓰고 있었다면 그 줄의 조건이 풀립니다."
        };

        remove.Click += (_, _) => _session!.Editor.RemoveCondition(condition.Id);
        return remove;
    }

    /// <summary>
    /// 대상 드롭다운 — <b>그 종류의 이름만</b> 담는다. 여는 순간 다시 읽는다 (2026-08-23
    /// 소유자: "아이템을 +추가를 한 뒤에 … 다른 노드에 갔다와야 하는게 불편해").
    /// 아이템·능력 편집은 <c>Content</c> 변경이라 이 화면을 다시 만들지 않으므로(타이핑 중
    /// 컨트롤 파괴 방지) 후보가 행을 만들 때의 것으로 굳어 있었다.
    /// </summary>
    private ComboBox ConditionTargetCombo(
        ConditionDefinition condition, bool ability, string picked, Action commit)
    {
        List<string> names = TargetNames(ability);

        var combo = new ComboBox
        {
            ItemsSource = names,
            SelectedItem = names.Contains(picked, StringComparer.Ordinal) ? picked : null,
            PlaceholderText = ability ? "능력" : "아이템",
            FontSize = 11,
            Margin = new Thickness(0, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        combo.DropDownOpened += (_, _) =>
        {
            List<string> fresh = TargetNames(ability);

            // 목록이 그대로면 손대지 않는다 — 갈아 끼우면 선택이 풀렸다 붙으며 조건 식이
            // 한 번 더 써진다.
            if (combo.ItemsSource is IEnumerable<string> current &&
                fresh.SequenceEqual(current, StringComparer.Ordinal))
            {
                return;
            }

            string? kept = combo.SelectedItem as string;
            _building = true;

            try
            {
                combo.ItemsSource = fresh;
                combo.SelectedItem = kept is not null && fresh.Contains(kept, StringComparer.Ordinal)
                    ? kept
                    : null;
            }
            finally
            {
                _building = false;
            }
        };

        combo.SelectionChanged += (_, _) => commit();

        // 개명이 지나가면 이 칸이 가리키던 이름이 달라진다. 행을 다시 만들지 않고
        // (그러면 방금 옮겨 간 초점을 부순다) 이 칸만 새로 읽는다 — 아래 참조.
        _conditionTargets.Add(() => RefreshConditionTarget(condition, ability, combo));

        return combo;
    }

    /// <summary>그 종류(아이템/능력)의 현재 이름들. 드롭다운과 개명 갱신이 같은 하나를 본다.</summary>
    private List<string> TargetNames(bool ability) =>
        _session?.Project.FindNode(_nodeId) is SetNode owner
            ? owner.Assignments
                .Where(item => item.Variable.Length > 0 && item.IsBool == ability)
                .Select(item => item.Variable)
                .ToList()
            : [];

    /// <summary>
    /// 대상 칸 하나를 <b>조건의 현재 식에서</b> 다시 읽는다. 표시를 고칠 뿐 아무것도 쓰지
    /// 않는다(<c>_building</c> 동안은 <c>SelectionChanged</c>가 커밋으로 새지 않는다).
    /// </summary>
    private void RefreshConditionTarget(ConditionDefinition condition, bool ability, ComboBox combo)
    {
        Vn.Authoring.Chapters.ConditionExpressionParser.TryDecomposeSingle(
            (condition.Expression ?? string.Empty).Replace("$", string.Empty, StringComparison.Ordinal),
            out string picked, out _, out _);

        List<string> fresh = TargetNames(ability);

        _building = true;

        try
        {
            combo.ItemsSource = fresh;
            combo.SelectedItem = fresh.Contains(picked, StringComparer.Ordinal) ? picked : null;
        }
        finally
        {
            _building = false;
        }
    }

    /// <summary>
    /// 아이템·능력을 <b>개명</b>한 직후 조건 표의 대상 칸들을 새 이름으로 맞춘다.
    ///
    /// 개명 전파는 조건식을 이미 갈아 끼웠지만(<c>ProjectEditor.SetAssignments</c>),
    /// 그 변경은 <see cref="ProjectChangeKind.Content"/>라 화면을 다시 만들지 않는다 —
    /// 다시 만들면 이름 칸이 초점을 잃는 순간(=커밋하는 순간) 컨트롤이 사라져 <b>다음 클릭이
    /// 먹히지 않는다</b>. 그래서 행은 그대로 두고 칸의 값만 갈아 끼운다.
    /// </summary>
    private void RefreshConditionTargets()
    {
        foreach (Action refresh in _conditionTargets)
        {
            refresh();
        }
    }

    private IReadOnlyList<Control> BuildItemConditionCells(
        ConditionDefinition condition,
        string pickedName,
        string pickedOperator,
        string pickedValue)
    {
        ComboBox target = null!;
        ComboBox comparison = null!;
        TextBox value = null!;
        TextBox name = null!;

        void Commit()
        {
            if (_building || target.SelectedItem is not string picked || picked.Length == 0)
            {
                return;
            }

            _session!.Editor.UpdateCondition(
                condition.Id,
                name.Text ?? string.Empty,
                $"${picked} {comparison.SelectedItem as string ?? ">="} {(value.Text ?? string.Empty).Trim()}");
        }

        name = ConditionNameBox(condition, Commit);
        target = ConditionTargetCombo(condition, ability: false, pickedName, Commit);

        comparison = new ComboBox
        {
            ItemsSource = ConditionOperators,
            SelectedItem = ConditionOperators.Contains(pickedOperator) ? pickedOperator : ">=",
            FontSize = 11,
            Margin = new Thickness(0, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        comparison.SelectionChanged += (_, _) => Commit();

        value = new TextBox
        {
            Text = pickedValue,
            PlaceholderText = "수치",
            FontSize = 12,
            Margin = new Thickness(0, 0, 4, 0)
        };
        value.LostFocus += (_, _) => Commit();

        return [name, target, comparison, value, ConditionRemoveButton(condition)];
    }

    private IReadOnlyList<Control> BuildAbilityConditionCells(
        ConditionDefinition condition,
        string pickedName,
        string pickedOperator)
    {
        ComboBox target = null!;
        CheckBox toggle = null!;
        TextBox name = null!;

        void Commit()
        {
            if (_building || target.SelectedItem is not string picked || picked.Length == 0)
            {
                return;
            }

            _session!.Editor.UpdateCondition(
                condition.Id,
                name.Text ?? string.Empty,
                $"${picked} == {(toggle.IsChecked == true ? "true" : "false")}");
        }

        name = ConditionNameBox(condition, Commit);
        target = ConditionTargetCombo(condition, ability: true, pickedName, Commit);

        // 능력에는 부호가 없다 (소유자) — On/Off뿐이라 `>=` 같은 것이 설 자리가 없다.
        toggle = new CheckBox
        {
            IsChecked = string.Equals(pickedOperator, "true", StringComparison.Ordinal),
            Content = string.Equals(pickedOperator, "true", StringComparison.Ordinal) ? "On" : "Off",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            toggle.Content = toggle.IsChecked == true ? "On" : "Off";
            Commit();
        };

        // 아이템 조건과 <b>같은 열 정의</b>를 쓴다 — 두 표의 이름 칸이 어긋나면 눈이 매번
        // 다시 맞춰야 한다 (2026-08-23 소유자: "둘이 동일하게"). 남는 칸은 빈 자리로 둔다.
        return [name, target, toggle, new Panel(), ConditionRemoveButton(condition)];
    }

    private IReadOnlyList<Control> BuildRawConditionCells(ConditionDefinition condition)
    {
        TextBox name = null!;

        name = ConditionNameBox(condition, () =>
        {
            if (!_building)
            {
                _session!.Editor.UpdateCondition(
                    condition.Id, name.Text ?? string.Empty, condition.Expression ?? string.Empty);
            }
        });

        var raw = new TextBox
        {
            Text = condition.Expression,
            IsReadOnly = true,
            FontSize = 11,
            Margin = new Thickness(0, 0, 4, 0),
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            [ToolTip.TipProperty] = "여러 항을 묶은 식이라 칸으로 나누지 않았습니다. " +
                                    "고치려면 지우고 다시 만드세요."
        };

        return [name, raw, ConditionRemoveButton(condition)];
    }

    /// <summary>
    /// 조건 하나를 <b>완성된 채로</b> 만든다 — 첫 아이템·능력을 가리키는 식으로 태어난다.
    ///
    /// 빈 식으로 태어나면 그 조건이 아이템의 것인지 능력의 것인지 <b>데이터가 말하지 못한다</b>
    /// (<see cref="ConditionDefinition"/>은 이름과 식뿐이다). 그래서 종류별 단추가 각자 첫
    /// 후보를 물려 준다 — 이름만 적으면 바로 쓸 수 있고, 대상은 드롭다운에서 바꾸면 된다.
    /// </summary>
    private void AddCondition(bool ability)
    {
        if (_session?.Project.FindNode(_nodeId) is not SetNode node || _nodeId is null)
        {
            return;
        }

        VariableAssignment? first = node.Assignments
            .FirstOrDefault(item => item.Variable.Length > 0 && item.IsBool == ability);

        if (first is null)
        {
            return; // 단추가 이미 잠겨 있다 — 여기까지 오는 길은 우회 호출뿐이다.
        }

        // 이름은 빈칸으로 시작한다 (W47) — "새 조건"이 미리 채워져 있으면 지우는 일부터
        // 시켜야 한다. 자리 안내는 PlaceholderText가 한다.
        _session.Editor.AddCondition(
            _nodeId,
            string.Empty,
            ability ? $"${first.Variable} == true" : $"${first.Variable} >= 1");
    }

    private const string AssignmentColumns = "26,*,96,80,80,32";


    /// <summary>
    /// 아이템 표와 능력 표 (2026-08-23). <b>둘은 다른 열을 갖는다</b> — 아이템은 개수라
    /// 초기값·슬라이더 범위가 있고, 능력은 있다/없다뿐이라 On/Off 하나면 끝이다.
    ///
    /// ⚠ <b>종류 드롭다운이 사라졌다</b> (소유자: "bool과 int 타입이 서로 오고가고 있는데
    /// 오히려 직관적이지 않아서 불편하다"). 종류는 <see cref="AddAssignment"/>가 정하고 그
    /// 뒤로 안 바뀐다 — 행이 갈아입지 않으니 <b>안 쓰는 칸의 값이 새 종류로 넘어오는 사고</b>
    /// (2026-08-17 소유자 보고: 능력→아이템에서 초기값이 `false`가 되던 것)도 구조적으로
    /// 불가능해졌다. 종류를 잘못 골랐으면 지우고 다시 만든다.
    ///
    /// 저장되는 자료형은 예전 그대로 <c>float</c>/<c>bool</c>이라 옛 프로젝트도 읽힌다.
    /// </summary>
    private void RebuildAssignments(SetNode node)
    {
        AssignmentHost.Children.Clear();

        var itemRows = new List<IReadOnlyList<Control>>();
        var abilityRows = new List<IReadOnlyList<Control>>();

        for (int index = 0; index < node.Assignments.Count; index++)
        {
            if (node.Assignments[index].IsBool)
            {
                abilityRows.Add(BuildAbilityCells(node, index));
            }
            else
            {
                itemRows.Add(BuildItemCells(node, index));
            }
        }

        AssignmentHost.Children.Add(BuildTable(
            "아이템 (개수)", AssignmentColumns, ["이름", "초기값", "최소", "최대"], itemRows,
            "없습니다. [＋ 아이템]으로 만듭니다 — 약초 3개처럼 개수로 세는 것."));

        AssignmentHost.Children.Add(BuildTable(
            "능력 (보유)", AssignmentColumns, ["이름", "초기값", "", ""], abilityRows,
            "없습니다. [＋ 능력]으로 만듭니다 — 자물쇠따기처럼 있다/없다뿐인 것."));
    }

    /// <summary>이름 칸 — 자동완성 후보는 포커스마다 다시 읽는다(다른 행에 방금 적은 이름).</summary>
    private AutoCompleteBox AssignmentNameBox(VariableAssignment assignment, Action commit)
    {
        var variable = new AutoCompleteBox
        {
            Text = assignment.Variable,
            PlaceholderText = "이름",
            FontSize = 12,
            MinWidth = 80,
            Margin = new Thickness(0, 0, 4, 0),
            // A계층 스탯은 후보에서 뺀다 (2026-08-17 소유자) — 정의 파일의 variables는 두
            // 계층을 한 목록에 담고 있어서, 그대로 쓰면 작가가 trust에 set을 걸 수 있다.
            // 스탯이 변하는 자리는 간선뿐이므로 화면이 그 규칙을 지킨다.
            ItemsSource = WriterVariableNames(),
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0
        };

        variable.LostFocus += (_, _) => commit();
        variable.GotFocus += (_, _) => variable.ItemsSource = WriterVariableNames();

        return variable;
    }

    private Button AssignmentRemoveButton(SetNode node, int index)
    {
        var remove = new Button
        {
            Content = "✕",
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            [ToolTip.TipProperty] = "이 줄을 지웁니다. 이것을 쓰던 조건은 대상이 빈 채로 남습니다."
        };

        remove.Click += (_, _) =>
        {
            List<VariableAssignment> next = node.Assignments.Select(item => item.Clone()).ToList();

            if (index < next.Count)
            {
                next.RemoveAt(index);
                _session!.Editor.SetAssignments(node.Id, next);
            }
        };

        return remove;
    }

    /// <summary>아이템의 초기값은 숫자다 — 숫자로 못 읽으면 0에서 시작한다.</summary>
    private static string NumberOrZero(string? text)
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

    private static double? ParseRange(string? text) =>
        double.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : null;

    private IReadOnlyList<Control> BuildItemCells(SetNode node, int index)
    {
        VariableAssignment assignment = node.Assignments[index];

        AutoCompleteBox variable = null!;
        TextBox value = null!;
        TextBox sliderMin = null!;
        TextBox sliderMax = null!;

        void Commit()
        {
            if (_building)
            {
                return;
            }

            List<VariableAssignment> next = node.Assignments.Select(item => item.Clone()).ToList();

            if (index >= next.Count)
            {
                return; // 그 사이 줄이 지워졌다 — 다음 그리기가 정본이다.
            }

            next[index] = new VariableAssignment
            {
                Variable = variable.Text ?? string.Empty,
                Value = NumberOrZero(value.Text),
                Type = VariableAssignment.FloatType,
                SliderMin = ParseRange(sliderMin.Text),
                SliderMax = ParseRange(sliderMax.Text)
            };

            _session!.Editor.SetAssignments(node.Id, next);

            // 이름을 바꿨다면 개명 전파가 조건식을 이미 갈았다 — 조건 표의 대상 칸도
            // 새 이름을 읽게 한다(행은 그대로 둔다).
            RefreshConditionTargets();
        }

        variable = AssignmentNameBox(assignment, Commit);

        value = new TextBox
        {
            Text = assignment.Value,
            PlaceholderText = "0",
            FontSize = 12,
            Margin = new Thickness(0, 0, 4, 0)
        };
        value.LostFocus += (_, _) => Commit();

        // Set 편집 슬라이더의 변수별 범위 (X6). 비우면 기본 -5~+5다.
        sliderMin = new TextBox
        {
            Text = assignment.SliderMin?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PlaceholderText = VariableAssignment.DefaultSliderMin.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 11,
            Margin = new Thickness(0, 0, 2, 0),
            [ToolTip.TipProperty] = "슬라이더 최솟값 — 편의 범위이며 직접 입력은 범위 밖도 됩니다."
        };
        sliderMin.LostFocus += (_, _) => Commit();

        sliderMax = new TextBox
        {
            Text = assignment.SliderMax?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PlaceholderText = "+" + VariableAssignment.DefaultSliderMax.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 11,
            Margin = new Thickness(0, 0, 4, 0),
            [ToolTip.TipProperty] = "슬라이더 최댓값"
        };
        sliderMax.LostFocus += (_, _) => Commit();

        return [variable, value, sliderMin, sliderMax, AssignmentRemoveButton(node, index)];
    }

    private IReadOnlyList<Control> BuildAbilityCells(SetNode node, int index)
    {
        VariableAssignment assignment = node.Assignments[index];

        AutoCompleteBox variable = null!;
        CheckBox boolValue = null!;

        void Commit()
        {
            if (_building)
            {
                return;
            }

            List<VariableAssignment> next = node.Assignments.Select(item => item.Clone()).ToList();

            if (index >= next.Count)
            {
                return;
            }

            next[index] = new VariableAssignment
            {
                Variable = variable.Text ?? string.Empty,
                // 저장 값은 Yarn 문법 그대로 true/false 문자열이라 출력이 바뀌지 않는다 (X7).
                Value = boolValue.IsChecked == true ? "true" : "false",
                Type = VariableAssignment.BoolType

                // 슬라이더 범위는 능력에 뜻이 없다 — 담지 않는다.
            };

            _session!.Editor.SetAssignments(node.Id, next);
            RefreshConditionTargets();
        }

        variable = AssignmentNameBox(assignment, Commit);

        boolValue = new CheckBox
        {
            IsChecked = string.Equals(assignment.Value, "true", StringComparison.OrdinalIgnoreCase),
            Content = string.Equals(assignment.Value, "true", StringComparison.OrdinalIgnoreCase) ? "On" : "Off",
            FontSize = 12,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        boolValue.IsCheckedChanged += (_, _) =>
        {
            boolValue.Content = boolValue.IsChecked == true ? "On" : "Off";
            Commit();
        };

        // 아이템 표와 같은 열 정의 — 이름 칸 길이가 두 표에서 같아야 한다 (소유자).
        return [variable, boolValue, new Panel(), new Panel(), AssignmentRemoveButton(node, index)];
    }

    /// <summary>
    /// 아이템·능력 한 줄을 <b>그 종류로</b> 만든다 — 값 공간이 종류마다 달라서, 태어날 때
    /// 그 종류의 기본값을 갖는다(아이템 0 · 능력 Off).
    /// </summary>
    private void AddAssignment(bool ability)
    {
        if (_session?.Project.FindNode(_nodeId) is not SetNode node)
        {
            return;
        }

        List<VariableAssignment> next = node.Assignments.Select(item => item.Clone()).ToList();

        next.Add(new VariableAssignment
        {
            Variable = string.Empty,
            Value = ability ? "false" : "0",
            Type = ability ? VariableAssignment.BoolType : VariableAssignment.FloatType
        });

        _session.Editor.SetAssignments(node.Id, next);
    }
}
