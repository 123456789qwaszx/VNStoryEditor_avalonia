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

        // *.xls*로 넓게 듣는다 — 구글 시트가 .xlsx를 저장하며 .xlsm으로 개명하는 실사례가
        // 있어(매크로 없이 선언만 그렇게 쓴다), .xlsx만 들으면 그 저장을 놓친다.
        _watcher = new FileSystemWatcher(folder, "*.xls*")
        {
            // 엑셀은 임시 파일로 쓰고 이름을 바꾼다 — 이름·크기·쓴 시각을 모두 봐야 저장을 놓치지 않는다.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,

            // 대본은 챕터별 하위 폴더에 산다 (2026-08-16) — 하위까지 들어야 저장을 잡는다.
            IncludeSubdirectories = true
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

        try
        {
            _onChanged();
        }
        catch (Exception)
        {
            // <b>파일 알림이 프로세스를 죽여서는 안 된다</b> (2026-08-18).
            //
            // 이 자리는 타이머 스레드다 — 여기서 새어 나간 예외는 잡을 사람이 없어 곧장
            // 프로세스를 내린다. 실제로 그렇게 됐다: 뷰가 감시자를 닫지 않고 사라진 뒤
            // 250ms 디바운스가 깨어나 이미 없어진 디스패처에 Post를 했고,
            // <c>NullReferenceException</c>이 <b>테스트 호스트를 통째로 죽였다.</b>
            // 그래서 그때 돌고 있던 테스트가 매번 다른 이름으로 실패했다.
            //
            // 뿌리는 닫지 않은 감시자이고 그쪽을 고쳤다. 이 catch는 <b>두 번째 방벽</b>이다:
            // 앱을 끄는 중에 저장 사건 하나가 들어오는 것만으로 종료가 크래시가 되는 길을
            // 남겨 둘 이유가 없다. 알림 하나를 놓치는 것과 프로세스가 죽는 것은 값이 다르다.
        }
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
