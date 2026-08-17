namespace Vn.Authoring.Model;

/// <summary>
/// 작가가 직접 더한 화자 (2026-08-17 소유자) — <b>`game.definition.json`에 넣지 않는다</b>.
///
/// 정의 파일은 기획자 전용이다: 스탯·전역 조건·초상화 매핑·연출 카탈로그가 산다. 작가가
/// 자기 씬에서 쓰려고 만든 이름까지 거기 섞이면 두 사람의 자료가 한 파일에서 엉키고,
/// 기획자가 "이 화자는 뭐지" 하는 줄이 늘어난다. 그래서 작가의 것은 프로젝트(작가 소유)에 산다.
///
/// <b>없어도 대본은 돈다</b> — 화자 칸은 자유 입력이고 이건 드롭다운 재료일 뿐이다.
/// 초상화가 필요하면 <see cref="CharacterId"/>를 적는다(표정은 에셋 폴더 규약이 소유).
/// </summary>
public sealed class WriterSpeaker
{
    /// <summary>대본에 적히는 화자명 그대로.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>초상화 매니페스트의 characterId. 비워 둘 수 있다(이름만 쓰는 화자).</summary>
    public string CharacterId { get; set; } = string.Empty;

    public WriterSpeaker Clone() => new() { Name = Name, CharacterId = CharacterId };
}
