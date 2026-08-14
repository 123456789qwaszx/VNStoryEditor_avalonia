using Avalonia.Controls;
using Avalonia.Media;
using Vn.Authoring.Chapters;

namespace Vn.App.Views;

/// <summary>
/// 챕터 그래프 화면 전용 우측 패널 — 챕터 엑셀의 다섯 시트(에피소드·간선·조건·스탯·픽스처)를
/// 읽기 전용으로 세운다. 대사 편집기·발행·무대 프리뷰는 시나리오 그래프의 것이라 여기서는
/// 소음이었다(소유자 보고). 원천은 챕터 그래프 뷰가 방금 읽은 모델 하나다 — 따로 읽지 않는다.
/// </summary>
public partial class ChapterDataView : UserControl
{
    public ChapterDataView()
    {
        InitializeComponent();
        Show(null);
    }

    internal void Show(ChapterEntry? entry)
    {
        Sections.Children.Clear();

        if (entry?.Model is not { } model)
        {
            Sections.Children.Add(new TextBlock
            {
                Text = "챕터를 만들거나 고르면 챕터 엑셀의 내용(에피소드·간선·조건·스탯·픽스처)이 여기 보입니다.",
                FontSize = 11,
                Opacity = 0.55,
                TextWrapping = TextWrapping.Wrap
            });

            return;
        }

        Sections.Children.Add(new TextBlock
        {
            Text = $"챕터 {model.ChapterId}",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13
        });
        Sections.Children.Add(new TextBlock
        {
            Text = "챕터 엑셀 내용 · 읽기 전용 — 고치는 곳은 엑셀 워크북 또는 챕터 그래프의 패널입니다.",
            FontSize = 10,
            Opacity = 0.55,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 4)
        });

        Section($"에피소드 ({model.Episodes.Count})");

        foreach (ChapterEpisode episode in model.Episodes)
        {
            Line(episode.EpisodeId, bold: true);

            string facts = string.Join(" · ", new[]
            {
                episode.Title.Length > 0 ? $"제목 {episode.Title}" : null,
                episode.Kind.Length > 0 ? $"종류 {episode.Kind}" : null,
                string.IsNullOrWhiteSpace(episode.VisibleConditionLabel) ? null : $"표시 {episode.VisibleConditionLabel}",
                string.IsNullOrWhiteSpace(episode.UnlockConditionLabel) ? null : $"해금 {episode.UnlockConditionLabel}",
                string.IsNullOrWhiteSpace(episode.EndingKey) ? null : $"엔딩키 {episode.EndingKey}",
                episode.DialogueEntry == episode.EpisodeId ? null : $"대사엔트리 {episode.DialogueEntry}",
                string.IsNullOrWhiteSpace(episode.Memo) ? null : $"메모 {episode.Memo}"
            }.Where(part => part is not null));

            if (facts.Length > 0)
            {
                Line($"    {facts}", dim: true);
            }
        }

        Section($"간선 ({model.Edges.Count})");

        foreach (ChapterEdge edge in model.Edges)
        {
            Line($"{edge.FromEpisodeId} → {edge.ToEpisodeId}", bold: true);

            string facts = string.Join(" · ", new[]
            {
                edge.IsPlainAdvance ? null : $"선택지 '{edge.OptionLabel}'",
                string.IsNullOrWhiteSpace(edge.ConditionLabel) ? null : $"조건 {edge.ConditionLabel}",
                edge.HideWhenLocked ? "잠기면 숨김" : null,
                string.IsNullOrWhiteSpace(edge.LockedMessage) ? null : $"잠금 안내 '{edge.LockedMessage}'"
            }.Where(part => part is not null));

            if (facts.Length > 0)
            {
                Line($"    {facts}", dim: true);
            }
        }

        Section($"조건 ({model.Conditions.Count})");

        foreach (ChapterCondition condition in model.Conditions)
        {
            Line($"{condition.Label}  =  {condition.Expression}" +
                (string.IsNullOrWhiteSpace(condition.Description) ? "" : $"   — {condition.Description}"));
        }

        Section($"스탯 ({model.Stats.Count})");

        foreach (ChapterStat stat in model.Stats)
        {
            string display = stat.DisplayName.Length > 0 && stat.DisplayName != stat.Key
                ? $"{stat.Key} ({stat.DisplayName})"
                : stat.Key;
            Line($"{display}  초기 {stat.Initial} · 범위 {stat.Minimum}~{stat.Maximum}");
        }

        Section($"픽스처 ({model.Fixtures.Count})");

        foreach (ChapterFixture fixture in model.Fixtures)
        {
            Line($"{fixture.Name}{(fixture.IsActive ? "  (활성)" : "")}", bold: fixture.IsActive);

            string facts = string.Join(" · ", fixture.Stats
                .Select(pair => $"{pair.Key} {pair.Value}")
                .Concat(fixture.Choices.Select(choice => $"고정 {choice.From}→{choice.To}")));

            if (facts.Length > 0)
            {
                Line($"    {facts}", dim: true);
            }
        }
    }

    private void Section(string title)
    {
        Sections.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Avalonia.Thickness(0, 10, 0, 2)
        });
    }

    private void Line(string text, bool bold = false, bool dim = false)
    {
        Sections.Children.Add(new SelectableTextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            Opacity = dim ? 0.65 : 1,
            TextWrapping = TextWrapping.Wrap
        });
    }
}
