namespace Vn.Authoring.Rendering;

/// <summary>
/// 번역 Preview가 LineId로 외부 번역문을 조회하는 경계.
/// 번역문은 StoryProject에 복사하지 않으며, 제공자가 없으면 빈 번역 칸으로 남긴다.
/// </summary>
public interface ILocalizedLineProvider
{
    string? GetLocalizedText(string lineId);
}
