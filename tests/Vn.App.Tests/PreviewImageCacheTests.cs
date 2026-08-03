using Vn.App.Services;

namespace Vn.App.Tests;

public class PreviewImageCacheTests
{
    private sealed class FakeImage : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void 같은_경로는_한_번만_로드한다()
    {
        int loads = 0;
        var cache = new PreviewImageCache<FakeImage>(_ =>
        {
            loads++;
            return new FakeImage();
        });

        FakeImage? first = cache.Get(@"C:\assets\office.png");
        FakeImage? second = cache.Get(@"C:\assets\office.png");

        Assert.Same(first, second);
        Assert.Equal(1, loads);
    }

    [Fact]
    public void 경로_키는_Ordinal이다()
    {
        int loads = 0;
        var cache = new PreviewImageCache<FakeImage>(_ =>
        {
            loads++;
            return new FakeImage();
        });

        cache.Get(@"C:\assets\office.png");
        cache.Get(@"C:\assets\Office.png");

        // 에셋 해석이 이미 대소문자 정확 일치로 끝났으므로 캐시도 느슨해지지 않는다.
        Assert.Equal(2, loads);
    }

    [Fact]
    public void 로드_실패도_캐시되어_라인_이동마다_다시_열지_않는다()
    {
        int loads = 0;
        var cache = new PreviewImageCache<FakeImage>(_ =>
        {
            loads++;
            throw new IOException("깨진 파일");
        });

        Assert.Null(cache.Get(@"C:\assets\broken.png"));
        Assert.Null(cache.Get(@"C:\assets\broken.png"));
        Assert.Equal(1, loads);
    }

    [Fact]
    public void Clear가_명시적_새로_고침이다()
    {
        int loads = 0;
        var images = new List<FakeImage>();
        var cache = new PreviewImageCache<FakeImage>(_ =>
        {
            loads++;
            var image = new FakeImage();
            images.Add(image);
            return image;
        });

        cache.Get(@"C:\assets\office.png");
        cache.Clear();
        cache.Get(@"C:\assets\office.png");

        Assert.Equal(2, loads);
        Assert.True(images[0].Disposed); // 버린 비트맵은 해제한다
        Assert.False(images[1].Disposed);
    }
}
