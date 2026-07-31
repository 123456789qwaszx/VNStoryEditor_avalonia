using System;
using System.IO;
using System.Text;

namespace Vn.App.Services;

/// <summary>
/// Yarn 파일을 읽고 쓴다.
///
/// 이 도구가 저장한 파일을 Unity가 그대로 읽는다. 그래서 저장의 첫 번째 요구는
/// "고치지 않은 곳은 한 바이트도 달라지지 않는다"이다. BOM이 사라지거나 줄바꿈이
/// CRLF에서 LF로 바뀌면 작가가 건드리지도 않은 줄까지 diff에 뜨고,
/// 그 diff 속에서 진짜 변경을 찾는 일은 작가의 몫이 된다.
///
/// 그래서 읽을 때 본 형태를 그대로 들고 다니다가 쓸 때 복원한다.
/// 텍스트의 줄바꿈은 <em>정규화하지 않는다</em>. 읽은 그대로 들고 있다가 그대로 쓴다.
/// 정규화했다가 되돌리는 방식은 한 파일에 CRLF와 LF가 섞여 있으면 반드시 깨진다.
/// </summary>
public static class StoryFileService
{
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly byte[] Utf16LeBom = { 0xFF, 0xFE };
    private static readonly byte[] Utf16BeBom = { 0xFE, 0xFF };

    /// <summary>
    /// 파일을 읽고, 원래 형태를 복원하는 데 필요한 것을 함께 돌려준다.
    /// 인코딩을 해석할 수 없는 바이트가 있으면 예외를 던진다.
    /// 조용히 대체 문자로 바꾸면 그 파일은 저장하는 순간 망가진다.
    /// </summary>
    public static StoryFile Read(string path)
    {
        string fullPath = Path.GetFullPath(path);
        byte[] bytes = File.ReadAllBytes(fullPath);

        byte[] bom = DetectByteOrderMark(bytes);
        Encoding encoding = GetEncoding(bom);

        string text = encoding.GetString(bytes, bom.Length, bytes.Length - bom.Length);

        return new StoryFile(
            fullPath,
            text,
            encoding,
            bom,
            DetectLineEndings(text));
    }

    /// <summary>
    /// <paramref name="original"/>이 기억한 인코딩과 BOM으로 복원해서 쓴다.
    ///
    /// 먼저 임시 파일에 다 쓰고 나서 교체한다. 대상 파일에 바로 쓰면
    /// 도중에 디스크가 차거나 프로세스가 죽었을 때 원고가 반만 남은 파일이 된다.
    /// 임시 파일은 같은 폴더에 만든다. 볼륨이 다르면 교체가 원자적이지 않다.
    /// </summary>
    public static void Write(string path, string text, StoryFile original)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(original);

        string fullPath = Path.GetFullPath(path);

        byte[] body = original.Encoding.GetBytes(text);
        byte[] bom = original.ByteOrderMark;

        byte[] bytes = new byte[bom.Length + body.Length];
        bom.CopyTo(bytes, 0);
        body.CopyTo(bytes, bom.Length);

        string directory = Path.GetDirectoryName(fullPath) ?? ".";
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 임시 파일이 남는 것보다 원래 예외를 그대로 올리는 쪽이 중요하다.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static byte[] DetectByteOrderMark(byte[] bytes)
    {
        if (StartsWith(bytes, Utf8Bom))
        {
            return Utf8Bom;
        }

        if (StartsWith(bytes, Utf16LeBom))
        {
            return Utf16LeBom;
        }

        if (StartsWith(bytes, Utf16BeBom))
        {
            return Utf16BeBom;
        }

        return Array.Empty<byte>();
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (int index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// BOM이 없으면 UTF-8로 본다. Yarn 파일은 UTF-8이 전제다.
    /// 모든 인코딩은 BOM을 스스로 붙이지 않게 만든다. BOM은 <see cref="Write"/>가 직접 붙인다.
    /// 어느 쪽이 BOM을 책임지는지 한 곳으로 정해두지 않으면 BOM이 두 번 붙는 실수가 난다.
    /// </summary>
    private static Encoding GetEncoding(byte[] bom)
    {
        if (bom == Utf16LeBom)
        {
            return new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: false,
                throwOnInvalidBytes: true);
        }

        if (bom == Utf16BeBom)
        {
            return new UnicodeEncoding(
                bigEndian: true,
                byteOrderMark: false,
                throwOnInvalidBytes: true);
        }

        return new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
    }

    private static LineEndingStyle DetectLineEndings(string text)
    {
        bool crlf = false;
        bool lf = false;
        bool cr = false;

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    crlf = true;
                    index++;
                }
                else
                {
                    cr = true;
                }
            }
            else if (text[index] == '\n')
            {
                lf = true;
            }
        }

        int kinds = (crlf ? 1 : 0) + (lf ? 1 : 0) + (cr ? 1 : 0);

        if (kinds == 0)
        {
            return LineEndingStyle.None;
        }

        if (kinds > 1)
        {
            return LineEndingStyle.Mixed;
        }

        return crlf
            ? LineEndingStyle.CrLf
            : lf
                ? LineEndingStyle.Lf
                : LineEndingStyle.Cr;
    }
}

/// <summary>
/// 읽은 파일의 내용과, 그 파일을 원래 형태로 되돌리는 데 필요한 정보.
/// </summary>
public sealed class StoryFile
{
    internal StoryFile(
        string path,
        string text,
        Encoding encoding,
        byte[] byteOrderMark,
        LineEndingStyle lineEndings)
    {
        Path = path;
        Text = text;
        Encoding = encoding;
        ByteOrderMark = byteOrderMark;
        LineEndings = lineEndings;
    }

    public string Path { get; }

    /// <summary>읽은 그대로의 텍스트. 줄바꿈을 정규화하지 않았다.</summary>
    public string Text { get; }

    public Encoding Encoding { get; }

    public bool HasByteOrderMark => ByteOrderMark.Length > 0;

    /// <summary>
    /// 이 파일이 쓰는 줄바꿈. 지금은 복원에 쓰이지 않는다 — 텍스트가 이미 원래 줄바꿈을
    /// 그대로 들고 있기 때문이다. 나중에 앱이 <em>새 줄을 넣을 때</em> 무엇을 쓸지
    /// 정하는 근거로 필요하다.
    /// </summary>
    public LineEndingStyle LineEndings { get; }

    internal byte[] ByteOrderMark { get; }
}

public enum LineEndingStyle
{
    /// <summary>줄바꿈이 하나도 없다.</summary>
    None,

    Lf,
    CrLf,
    Cr,

    /// <summary>한 파일 안에 두 가지 이상이 섞여 있다.</summary>
    Mixed
}
