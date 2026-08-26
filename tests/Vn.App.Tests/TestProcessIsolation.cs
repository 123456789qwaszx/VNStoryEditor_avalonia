using System.Runtime.CompilerServices;
using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// 테스트 프로세스를 <b>이 컴퓨터의 상태</b>에서 떼어 놓는다. 어셈블리가 실리는 순간
/// 한 번 돌므로 어떤 테스트보다 먼저다.
///
/// 떼는 것 둘:
/// - <b>설정 파일</b> — 세션이 프로젝트를 열 때마다 최근 프로젝트를 설정에 적는데, 그
///   경로가 사용자의 진짜 AppData면 테스트가 <b>사용자의 최근 프로젝트를 임시 폴더로
///   덮어쓴다</b>(2026-08-26에 실제 settings.json이 그렇게 돼 있었다). 테스트 전용 파일로
///   돌려세우고, 지난 실행의 잔재가 이번 실행으로 새지 않게 지우고 시작한다.
/// - <b>최근 프로젝트 복원</b> — 창을 띄우면 Opened가 최근 프로젝트를 복원하고 챕터
///   감시자가 붙는다. 그 감시자의 에피소드 동기화가 <b>비동기로</b> 테스트 판을 다시
///   그려(워크북에 없는 대사 노드 솎아내기 포함), 스위트 부하에 따라 매번 다른 테스트가
///   흔들렸다 — PresentationGraphStageJumpTests가 단독으로는 늘 통과하던 이유다(그때는
///   최근 프로젝트가 이미 지워진 임시 폴더라 복원이 조용히 실패했다). 복원을 시험하고
///   싶은 테스트는 <see cref="MainWindow.RestoreRecentProjectOnOpen"/>을 제 스코프에서
///   켜고 끝에 되돌린다.
/// </summary>
internal static class TestProcessIsolation
{
    [ModuleInitializer]
    internal static void Isolate()
    {
        string path = Path.Combine(Path.GetTempPath(), "vntool-tests", "settings.json");

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 다른 테스트 실행이 같은 파일을 쥐고 있다 — 같은 격리 파일이므로 그대로 쓴다.
        }

        AppSettingsService.SettingsPathOverride = path;
        MainWindow.RestoreRecentProjectOnOpen = false;
    }
}
