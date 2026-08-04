namespace Vn.Authoring.Model;

/// <summary>
/// 내보내기에서 산출할 양식 (X13). 기본은 전부 켬 — 끈 것만 저장된다.
///
/// Yarn 트리오(Story/Set/Pres + 선언)는 런타임이 한 묶음으로 읽으므로 쪼개지 않는다.
/// CSV 세 종은 수신자가 다르므로(대본·검수·연출) 각각 고른다.
/// </summary>
public sealed class ExportFormatSelection
{
    public bool YarnTrio { get; set; } = true;

    public bool ScriptCsv { get; set; } = true;

    public bool ReviewCsv { get; set; } = true;

    public bool DirectionCsv { get; set; } = true;

    /// <summary>전부 기본값이면 저장 파일에 아예 쓰지 않는다 — 기존 프로젝트 불변.</summary>
    public bool IsDefault => YarnTrio && ScriptCsv && ReviewCsv && DirectionCsv;

    public bool AnyCsv => ScriptCsv || ReviewCsv || DirectionCsv;

    public ExportFormatSelection Clone() => new()
    {
        YarnTrio = YarnTrio,
        ScriptCsv = ScriptCsv,
        ReviewCsv = ReviewCsv,
        DirectionCsv = DirectionCsv
    };
}
