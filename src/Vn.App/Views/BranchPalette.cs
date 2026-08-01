using Avalonia.Media;

namespace Vn.App.Views;

/// <summary>
/// 조건 갈래를 구분하는 색.
///
/// 색은 데이터가 아니다. <see cref="Vn.Authoring.Flow.ConditionBranch.PaletteIndex"/>에서
/// 계산해서 칠할 뿐이고, 반대로 색을 보고 조건을 되짚지 않는다.
/// 그래서 이 표를 통째로 바꿔도 저장된 원고는 한 글자도 달라지지 않는다.
///
/// <b>색만으로 정보를 전달하지 않는다.</b> 갈래에는 언제나 조건 이름이 함께 붙는다.
/// 색은 같은 갈래를 눈으로 빠르게 묶어 주는 보조 수단이다.
/// </summary>
internal static class BranchPalette
{
    private static readonly Color[] Accents =
    {
        Color.FromRgb(0x3B, 0x82, 0xF6), // 파랑
        Color.FromRgb(0x8B, 0x5C, 0xF6), // 보라
        Color.FromRgb(0x0E, 0x9F, 0x6E), // 초록
        Color.FromRgb(0xD9, 0x77, 0x06), // 주황
        Color.FromRgb(0xDB, 0x27, 0x77), // 자홍
        Color.FromRgb(0x08, 0x91, 0xB2)  // 청록
    };

    /// <summary>갈래의 강조색. 왼쪽 테두리와 라벨에 쓴다.</summary>
    public static IBrush Accent(int paletteIndex)
    {
        return new SolidColorBrush(AccentColor(paletteIndex));
    }

    /// <summary>갈래 카드의 배경. 강조색을 아주 옅게 깐 것이다.</summary>
    public static IBrush Background(int paletteIndex)
    {
        Color color = AccentColor(paletteIndex);
        return new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B));
    }

    /// <summary>
    /// 조건 출구 카드의 배경. 갈래 기본색보다 확실히 진하다.
    ///
    /// 선택 상태를 뜻하는 색과 구분되어야 하므로 채도를 올리는 대신 불투명도를 크게 올린다.
    /// 이 카드는 왼쪽 테두리로 자기 갈래 색을 계속 보여 주므로,
    /// "어느 갈래인가"와 "출구인가"를 동시에 알 수 있다.
    /// </summary>
    public static IBrush ExitBackground(int paletteIndex)
    {
        Color color = AccentColor(paletteIndex);
        return new SolidColorBrush(Color.FromArgb(92, color.R, color.G, color.B));
    }

    private static Color AccentColor(int paletteIndex)
    {
        if (paletteIndex < 0)
        {
            return Color.FromRgb(0x6B, 0x72, 0x80);
        }

        return Accents[paletteIndex % Accents.Length];
    }
}
