using Avalonia.Threading;
using NAudio.Vorbis;
using NAudio.Wave;

namespace Vn.App.Services;

/// <summary>
/// 툴 안 오디오 재생 (W62) — 미리 듣기(에셋 탐색기·오디오 탭)와 프리뷰 재생 연동이
/// 같은 출구 하나를 쓴다. 게임 런타임 오디오가 아니라 저작 확인용 근사다:
/// 페이드·볼륨 커브 없음, BGM은 단순 루프, 일시정지 후 재개는 곡 처음부터.
///
/// 소리 자리는 셋이다:
///   미리 듣기 1자리(같은 파일 다시 클릭 = 정지), BGM 1자리(루프), 효과음 n자리(원샷).
/// 장치·코덱 실패는 삼키지 않고 <see cref="Problem"/>으로 보고한다 (규칙 14).
/// </summary>
internal static class AudioPreview
{
    /// <summary>재생 실패·미지원 보고 출구 — MainWindow가 상태줄로 잇는다.</summary>
    public static Action<string>? Problem;

    private static IWavePlayer? _auditionOut;
    private static WaveStream? _auditionStream;
    private static string? _auditionPath;

    private static IWavePlayer? _bgmOut;
    private static WaveStream? _bgmStream;

    private static readonly List<(IWavePlayer Out, WaveStream Stream)> _oneShots = new();

    /// <summary>미리 듣기 — 한 번 재생. 같은 파일을 다시 부르면 정지한다(토글).</summary>
    public static void ToggleAudition(string path)
    {
        bool wasThis = string.Equals(_auditionPath, path, StringComparison.OrdinalIgnoreCase);
        StopAudition();

        if (wasThis)
        {
            return;
        }

        if (Start(path, loop: false) is { } started)
        {
            (_auditionOut, _auditionStream) = started;
            _auditionPath = path;
            started.Out.PlaybackStopped += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_auditionOut, started.Out))
                {
                    StopAudition();
                }
            });
        }
    }

    /// <summary>BGM — 루프 재생. 새 곡은 이전 곡을 대체한다.</summary>
    public static void PlayBgm(string path)
    {
        StopBgm();

        if (Start(path, loop: true) is { } started)
        {
            (_bgmOut, _bgmStream) = started;
        }
    }

    /// <summary>효과음 — 원샷. 겹쳐 울릴 수 있고 끝나면 스스로 정리된다.</summary>
    public static void PlayOneShot(string path)
    {
        if (Start(path, loop: false) is not { } started)
        {
            return;
        }

        _oneShots.Add(started);
        started.Out.PlaybackStopped += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_oneShots.Remove(started))
            {
                Discard(started.Out, started.Stream);
            }
        });
    }

    public static void StopAudition()
    {
        Discard(_auditionOut, _auditionStream);
        _auditionOut = null;
        _auditionStream = null;
        _auditionPath = null;
    }

    public static void StopBgm()
    {
        Discard(_bgmOut, _bgmStream);
        _bgmOut = null;
        _bgmStream = null;
    }

    public static void StopOneShots()
    {
        foreach ((IWavePlayer output, WaveStream stream) in _oneShots)
        {
            Discard(output, stream);
        }

        _oneShots.Clear();
    }

    public static void StopAll()
    {
        StopAudition();
        StopBgm();
        StopOneShots();
    }

    private static (IWavePlayer Out, WaveStream Stream)? Start(string path, bool loop)
    {
        WaveStream? stream = null;
        IWavePlayer? output = null;

        try
        {
            stream = OpenStream(path);

            if (loop)
            {
                stream = new LoopStream(stream);
            }

            output = new WaveOutEvent();
            output.Init(stream);
            output.Play();
            return (output, stream);
        }
        catch (Exception exception)
        {
            Discard(output, stream);
            Problem?.Invoke(
                $"오디오 재생 실패 — {Path.GetFileName(path)}: {exception.Message}");
            return null;
        }
    }

    /// <summary>확장자 규약(AuthoringSession.AudioExtensions)과 같은 셋: mp3·wav는 NAudio, ogg는 Vorbis.</summary>
    private static WaveStream OpenStream(string path)
    {
        return Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(path)
            : new AudioFileReader(path);
    }

    private static void Discard(IWavePlayer? output, WaveStream? stream)
    {
        try
        {
            output?.Stop();
            output?.Dispose();
            stream?.Dispose();
        }
        catch
        {
            // 정리 중 장치 예외는 더 보고할 곳이 없다 — 재생 실패는 Start에서 이미 보고된다.
        }
    }

    /// <summary>끝에 닿으면 처음으로 되감는 단순 루프 — BGM 근사용.</summary>
    private sealed class LoopStream(WaveStream source) : WaveStream
    {
        public override WaveFormat WaveFormat => source.WaveFormat;

        public override long Length => source.Length;

        public override long Position
        {
            get => source.Position;
            set => source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;

            while (total < count)
            {
                int read = source.Read(buffer, offset + total, count - total);

                if (read == 0)
                {
                    if (source.Position == 0)
                    {
                        break; // 빈 원본 — 무한 루프를 만들지 않는다
                    }

                    source.Position = 0;
                    continue;
                }

                total += read;
            }

            return total;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                source.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
