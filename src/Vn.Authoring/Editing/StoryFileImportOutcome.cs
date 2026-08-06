using Vn.Authoring.Model;

namespace Vn.Authoring.Editing;

/// <summary>
/// 스토리 파일 가져오기(W51)의 결과 — 무엇이 들어왔고 무엇을 못 찾았는가.
/// 경고는 UI가 그대로 보여 준다(조용히 버리지 않는다 — 규칙 14).
/// </summary>
public sealed record StoryFileImportOutcome(
    StoryFile File,
    int ImportedScripts,
    IReadOnlyList<string> Warnings);
