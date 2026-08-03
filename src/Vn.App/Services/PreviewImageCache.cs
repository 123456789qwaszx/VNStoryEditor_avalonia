namespace Vn.App.Services;

/// <summary>
/// 프리뷰 이미지의 경로 키 캐시. 라인을 이동할 때마다 같은 PNG를 디스크에서
/// 다시 디코드하지 않기 위한 것이다.
///
/// 키는 절대 경로 문자열이고 Ordinal로 비교한다 — 에셋 해석이 이미 대소문자
/// 정확 일치(런타임 Resources 규약)로 끝났으므로 캐시가 더 느슨해질 이유가 없다.
///
/// 파일 변경 감지는 하지 않는다. 에셋 폴더를 바꿨으면 "새로 고침"이
/// <see cref="Clear"/>를 부른다 — 갱신 시점이 명시적 동작 하나뿐이어야
/// "왜 옛 그림이 보이지"를 물을 일이 없다.
///
/// 로더는 주입한다. 앱은 Avalonia Bitmap 생성자를 넣고, 테스트는 가짜 로더로
/// 캐시 동작만 검증한다. 로드 실패도 캐시한다 — 없는 파일을 라인 이동마다
/// 다시 열어 보는 것은 낭비고, 실패는 어차피 해석 단계에서 이미 가시화됐다.
/// </summary>
public sealed class PreviewImageCache<TImage> where TImage : class
{
    private readonly Func<string, TImage> _load;
    private readonly Dictionary<string, TImage?> _cache = new(StringComparer.Ordinal);

    public PreviewImageCache(Func<string, TImage> load)
    {
        _load = load ?? throw new ArgumentNullException(nameof(load));
    }

    public int Count => _cache.Count;

    /// <summary>캐시에 있으면 그대로, 없으면 로드해서 넣는다. 로드 실패는 null이다.</summary>
    public TImage? Get(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (_cache.TryGetValue(filePath, out TImage? cached))
        {
            return cached;
        }

        TImage? image;

        try
        {
            image = _load(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            image = null;
        }

        _cache[filePath] = image;
        return image;
    }

    public void Clear()
    {
        foreach (TImage? image in _cache.Values)
        {
            (image as IDisposable)?.Dispose();
        }

        _cache.Clear();
    }
}
