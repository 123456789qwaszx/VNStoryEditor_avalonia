using System.Security.Cryptography;

namespace Vn.App.Services;

/// <summary>
/// 한 번 열린 Yarn 문서의 단일 편집 상태.
/// 텍스트 편집기와 박스 편집기는 이 객체의 <see cref="WorkingText"/>만 수정한다.
/// 서로 다른 변경 목록을 따로 보유하지 않으므로 저장 시 한쪽 수정이 사라질 수 없다.
/// </summary>
internal sealed class OpenDocumentSession
{
    private StoryFile _format;
    private byte[] _diskFingerprint;

    private OpenDocumentSession(StoryFile file, byte[] diskFingerprint)
    {
        _format = file;
        _diskFingerprint = diskFingerprint;
        SavedText = file.Text;
        WorkingText = file.Text;
    }

    public string Path => _format.Path;

    public string SavedText { get; private set; }

    public string WorkingText { get; private set; }

    public LineEndingStyle LineEndings => _format.LineEndings;

    public bool IsDirty =>
        !string.Equals(WorkingText, SavedText, StringComparison.Ordinal);

    /// <summary>
    /// 원문 탭에서 자유 편집을 한 뒤에는 이전 분석의 줄 번호가 더 이상 안전하다고 말할 수 없다.
    /// 저장 후 재분석할 때까지 박스 편집을 잠그는 근거로 사용한다.
    /// </summary>
    public bool StructureIsStale { get; private set; }

    /// <summary>
    /// 원문 편집 전후의 물리적 줄 수가 같으면 분석 당시 줄 번호를 부분 편집에 계속 쓸 수 있다.
    /// 줄이 추가되거나 삭제되면 모든 구조 카드 편집을 재분석 전까지 잠근다.
    /// </summary>
    public bool CanUseAnalyzedLineMap =>
        CountLines(WorkingText) == CountLines(SavedText);

    public static OpenDocumentSession Open(string path)
    {
        StoryFile file = StoryFileService.Read(path);
        return new OpenDocumentSession(file, Fingerprint(file.Path));
    }

    public void ReplaceText(string text, bool structuralSource = true)
    {
        WorkingText = text ?? string.Empty;

        if (structuralSource)
        {
            StructureIsStale = IsDirty;
        }
    }

    /// <summary>
    /// 박스에서 고친 한 줄을 현재 작업 문자열에 즉시 반영한다.
    /// 디스크 원본에 별도의 패치 목록을 나중에 적용하지 않는다.
    /// 따라서 원문 탭에서 앞서 고친 다른 줄도 함께 보존된다.
    /// </summary>
    public bool TryApplyLineEdit(StoryLineEdit edit)
    {
        // 원문 편집 뒤에도 대상 줄이 분석 당시 원문과 정확히 같다면 안전하게 적용할 수 있다.
        // 줄 삽입·삭제로 위치가 밀렸거나 같은 줄을 원문에서 이미 고쳤다면 거부한다.
        if (StructureIsStale && !CanUseAnalyzedLineMap)
        {
            return false;
        }

        // 구조 탭은 현재 WorkingText로 다시 만들어진 카드만 편집할 수 있다.
        // 같은 카드에서 글자를 연속으로 입력하면 첫 TextChanged 이후 원문이 이미 바뀌므로,
        // 매 이벤트마다 최초 문자열과 다시 비교해서는 두 번째 글자부터 잘못 거부하게 된다.
        // 줄 수가 유지되는 동안에는 카드가 가리키는 물리적 줄을 현재 값으로 교체한다.

        string changed = StoryLineEditor.Apply(WorkingText, new[] { edit });

        if (string.Equals(changed, WorkingText, StringComparison.Ordinal))
        {
            return false;
        }

        WorkingText = changed;
        return true;
    }

    public DocumentSaveResult Save(bool overwriteExternalChanges = false)
    {
        if (!IsDirty)
        {
            return DocumentSaveResult.NoChanges();
        }

        if (!File.Exists(Path))
        {
            return DocumentSaveResult.Missing(Path);
        }

        try
        {
            byte[] currentFingerprint = Fingerprint(Path);

            if (!overwriteExternalChanges &&
                !currentFingerprint.AsSpan().SequenceEqual(_diskFingerprint))
            {
                return DocumentSaveResult.Conflict(Path);
            }

            StoryFileService.Write(Path, WorkingText, _format);

            // 실제로 기록된 파일을 다시 읽어 다음 저장의 인코딩·BOM·충돌 기준을 갱신한다.
            _format = StoryFileService.Read(Path);
            _diskFingerprint = Fingerprint(Path);
            SavedText = _format.Text;
            WorkingText = _format.Text;
            StructureIsStale = false;

            return DocumentSaveResult.Saved(Path);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException)
        {
            return DocumentSaveResult.Failed(Path, exception);
        }
    }

    public void DiscardChanges()
    {
        WorkingText = SavedText;
        StructureIsStale = false;
    }

    public void ReloadFromDisk()
    {
        StoryFile file = StoryFileService.Read(Path);
        _format = file;
        _diskFingerprint = Fingerprint(Path);
        SavedText = file.Text;
        WorkingText = file.Text;
        StructureIsStale = false;
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        int lines = 1;

        foreach (char character in text)
        {
            if (character == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static byte[] Fingerprint(string path)
    {
        return SHA256.HashData(File.ReadAllBytes(path));
    }
}

internal enum DocumentSaveStatus
{
    Saved,
    NoChanges,
    ExternalConflict,
    FileMissing,
    Failed
}

internal sealed record DocumentSaveResult(
    DocumentSaveStatus Status,
    string Path,
    Exception? Exception = null)
{
    public static DocumentSaveResult Saved(string path) =>
        new(DocumentSaveStatus.Saved, path);

    public static DocumentSaveResult NoChanges() =>
        new(DocumentSaveStatus.NoChanges, string.Empty);

    public static DocumentSaveResult Conflict(string path) =>
        new(DocumentSaveStatus.ExternalConflict, path);

    public static DocumentSaveResult Missing(string path) =>
        new(DocumentSaveStatus.FileMissing, path);

    public static DocumentSaveResult Failed(string path, Exception exception) =>
        new(DocumentSaveStatus.Failed, path, exception);
}
