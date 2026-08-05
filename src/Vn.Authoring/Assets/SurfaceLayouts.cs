namespace Vn.Authoring.Assets;

/// <summary>
/// 대사창 surface 레이아웃 프리셋 하나 (런타임 DialogueSurfaceLayoutPresetDB 덤프의 요약).
/// 좌표는 화면 비율(0..1) 앵커이고 y는 유니티 규약(아래→위)이다 — 캔버스 변환은 컴포저의 일.
/// 폰트·여백 등 세부 타이포는 담지 않는다: 프리뷰가 쓰는 것은 자리(rect)뿐이다.
/// </summary>
public sealed record SurfaceLayoutPreset(
    string Key,
    double LineMinX,
    double LineMinY,
    double LineMaxX,
    double LineMaxY,
    bool UseName,
    double NameMinX,
    double NameMinY,
    double NameMaxX,
    double NameMaxY);

/// <summary>surface 레이아웃 프리셋 집합. 키 정확 일치 조회만 있다 — 비슷한 키로 잇지 않는다.</summary>
public sealed class SurfaceLayoutSet
{
    private readonly Dictionary<string, SurfaceLayoutPreset> _presets;

    public SurfaceLayoutSet(IEnumerable<SurfaceLayoutPreset> presets)
    {
        _presets = presets.ToDictionary(preset => preset.Key, StringComparer.Ordinal);
    }

    public int Count => _presets.Count;

    public bool TryGet(string key, out SurfaceLayoutPreset preset)
    {
        if (_presets.TryGetValue(key, out SurfaceLayoutPreset? found))
        {
            preset = found;
            return true;
        }

        preset = null!;
        return false;
    }
}
