using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// W62 — clipKey → 파일 경로 역해석. 파일명(확장자 제외)=키 규약의 역방향이며,
/// 미리 듣기와 재생 연동이 이 하나를 쓴다.
/// </summary>
public class AudioClipResolutionTests
{
    [Fact]
    public void clipKey는_확장자와_대소문자를_넘어_파일을_되찾는다()
    {
        string root = Path.Combine(Path.GetTempPath(), $"VnTool.Audio.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllBytes(Path.Combine(root, "Main-Theme.mp3"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(root, "노트.txt"), "오디오 아님");

            var session = new AuthoringSession();

            Assert.Equal(
                Path.Combine(root, "Main-Theme.mp3"),
                session.ResolveAudioClipPath(root, "main-theme")); // 대소문자 무시 — 윈도우 파일계 규약
            Assert.Null(session.ResolveAudioClipPath(root, "노트")); // 확장자 규약 밖은 후보가 아니다
            Assert.Null(session.ResolveAudioClipPath(root, "없는키"));
            Assert.Null(session.ResolveAudioClipPath(null, "main-theme"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
