namespace Vn.Authoring.Chapters;

/// <summary>
/// `chapters/` 폴더를 지켜보다 워크북이 저장되면 알린다 (Gate A "엑셀 저장 → 뷰 즉시 갱신").
///
/// <b>왜 뷰 밖에 있는가</b> — 감시·디바운스는 화면이 아니라 파일의 일이고, 뷰 안에 두면
/// 창을 띄우지 않고는 시험할 수 없다. 여기 있으면 임시 폴더 하나로 끝난다.
///
/// <b>왜 디바운스가 필요한가</b> — 엑셀은 저장 한 번에 임시 파일 생성·이름 변경·원본 삭제를
/// 잇달아 일으켜 이벤트가 여러 번 온다. 그대로 받으면 한 번의 저장에 워크북을 네다섯 번 읽는다.
///
/// <b>알림은 워커 스레드에서 온다.</b> UI가 쓰려면 스스로 UI 스레드로 옮겨야 한다 —
/// 이 클래스는 화면을 모른다.
/// </summary>
public sealed class ChapterFolderWatcher : IDisposable
{
    /// <summary>엑셀의 저장 한 번이 내는 이벤트 무리를 덮을 만큼 짧고, 사람이 못 느낄 만큼 짧다.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private readonly TimeSpan _delay;
    private readonly Action _onChanged;
    private readonly Lock _gate = new();

    private bool _disposed;

    /// <param name="onChanged">디바운스가 끝난 뒤 한 번 호출된다. 워커 스레드에서 온다.</param>
    public ChapterFolderWatcher(string folder, Action onChanged, TimeSpan? debounce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(onChanged);

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"챕터 폴더가 없습니다: {folder}");
        }

        Folder = folder;
        _onChanged = onChanged;
        _delay = debounce ?? DefaultDebounce;
        _debounce = new Timer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _watcher = new FileSystemWatcher(folder, "*.xlsx")
        {
            // 엑셀은 임시 파일로 쓰고 이름을 바꾼다 — 이름·크기·쓴 시각을 모두 봐야 저장을 놓치지 않는다.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Changed += OnTouched;
        _watcher.Created += OnTouched;
        _watcher.Deleted += OnTouched;
        _watcher.Renamed += OnTouched;
        _watcher.EnableRaisingEvents = true;
    }

    public string Folder { get; }

    private void OnTouched(object sender, FileSystemEventArgs e)
    {
        // 엑셀이 저장 중에 열어 둔 잠금 파일(~$…)은 저장 사건이 아니다.
        if (Path.GetFileName(e.Name ?? string.Empty).StartsWith("~$", StringComparison.Ordinal))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _debounce.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        _onChanged();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnTouched;
        _watcher.Created -= OnTouched;
        _watcher.Deleted -= OnTouched;
        _watcher.Renamed -= OnTouched;
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
