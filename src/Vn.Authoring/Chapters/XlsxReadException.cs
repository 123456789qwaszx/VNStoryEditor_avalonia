namespace Vn.Authoring.Chapters;

/// <summary>
/// 워크북 <b>파일 자체</b>를 열지 못했을 때. 데이터 규격 위반이 아니라 접근 실패다 —
/// 그건 <see cref="ChapterDiagnostic"/>로 보고되고 모델은 그래도 만들어진다.
///
/// 어느 파일인지 반드시 담는다. 챕터 폴더에 워크북이 여럿일 때 "열지 못했습니다"만으로는
/// 사람이 어느 파일을 봐야 할지 알 수 없다.
/// </summary>
public sealed class XlsxReadException : Exception
{
    public XlsxReadException(string path, string message, Exception? inner = null)
        : base(message, inner)
    {
        Path = path;
    }

    public string Path { get; }
}
