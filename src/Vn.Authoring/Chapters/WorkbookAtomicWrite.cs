using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 워크북 한 벌을 <b>통째로 갈아 끼우는 유일한 자리</b> (R-B · 2026-09-15).
///
/// 두 이미터(<see cref="ChapterWorkbookEmitter"/>·<see cref="EpisodeWorkbookEmitter"/>)가 같은
/// 규칙을 두 벌 갖지 않게 여기 모았다. 규칙은 셋이다 — <b>임시 파일을 완성한 뒤 교체</b>하고,
/// 갈아 끼우기 직전에 <c>.bak</c>을 남기고, <b>엑셀이 잡고 있으면 아무것도 하지 않는다</b>.
///
/// ⚠ <b>ClosedXML의 경로 <c>SaveAs</c>는 확장자를 검사한다</b> — `.tmp`로 끝나는 이름을
/// 거부한다(<c>Extension 'tmp' is not supported</c>). 그렇다고 임시 이름을 `.xlsx`로 두면
/// 폴더를 훑는 자리들이 그것을 워크북으로 센다. 그래서 <b>스트림으로 받아 바이트로 쓴다</b> —
/// 스트림 오버로드에는 검사할 확장자가 없어 파일 이름을 우리가 고를 수 있다.
/// 이 우회를 지우려거든 두 이미터의 왕복 테스트를 함께 볼 것: 지운 순간 전부 터진다.
/// </summary>
internal static class WorkbookAtomicWrite
{
    /// <param name="build">메모리에서 워크북 한 벌을 짓는다. 디스크를 몰라야 한다.</param>
    public static ChapterWriteResult Replace(string path, Func<XLWorkbook> build)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(build);

        // 임시 파일은 <b>같은 폴더</b>에 둔다 — File.Move가 볼륨을 넘으면 원자적이지 않다.
        string temporary = path + ".tmp";

        try
        {
            string? folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            byte[] bytes;

            using (var memory = new MemoryStream())
            {
                using (XLWorkbook workbook = build())
                {
                    workbook.SaveAs(memory);
                }

                bytes = memory.ToArray();
            }

            File.WriteAllBytes(temporary, bytes);

            // 백업은 교체 <b>직전</b>에 — 임시 파일이 만들어지지 못한 경우까지 .bak을 굴리면
            // 아무 일도 없었는데 되돌릴 자리만 낡는다.
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }

            File.Move(temporary, path, overwrite: true);

            return ChapterWriteResult.Ok;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(temporary);

            return ChapterWriteResult.Locked(
                $"엑셀이 '{Path.GetFileName(path)}'를 열고 있어 툴이 쓰지 못했습니다 — " +
                "엑셀에서 그 파일을 닫으면 다시 냅니다.");
        }
        catch (Exception exception)
        {
            Discard(temporary);

            return ChapterWriteResult.Locked($"워크북을 내지 못했습니다: {exception.Message}");
        }
    }

    /// <summary>임시 파일을 남기지 않는다. 지우다 실패해도 그것 때문에 결과가 바뀌지는 않는다.</summary>
    private static void Discard(string temporary)
    {
        try
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 원래 실패의 사유를 덮지 않는다.
        }
    }
}
