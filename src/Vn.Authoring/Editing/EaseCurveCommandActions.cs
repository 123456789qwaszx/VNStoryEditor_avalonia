using Ked.Presentation.Core;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Editing;

/// <summary>
/// 커맨드 소유 곡선(customEase)의 흐름 (2026-08-20 소유자 결정) — UI가 아니라 여기가
/// 규칙이다:
///
/// - <b>곡선 편집을 열면</b> 지금 골라져 있던 이징(Linear·OutCubic…)이 <b>복사되어
///   그 커맨드만의 곡선</b>이 된다(<see cref="EnsureOwned"/>). 이름은 커맨드 Id에서
///   파생한 자동 이름이라 커맨드당 하나로 안정된다.
/// - <b>보관함</b>과는 복사로만 오간다: 커맨드의 곡선을 이름 붙여 보관함에 저장
///   (<see cref="SaveToLibrary"/>)하거나, 보관함의 것을 커맨드 곡선으로 복사
///   (<see cref="CopyFromLibrary"/>)한다. 참조 공유가 없으므로 한 커맨드를 만져도
///   다른 커맨드·보관함이 변하지 않는다.
/// - 표준 이징으로 되돌리면 소유 곡선은 지운다(<see cref="DiscardOwned"/>) —
///   소유 곡선의 존재 이유가 그 커맨드 하나이기 때문이다.
///
/// 텍스트·런타임 계약은 그대로다: 다섯째 인자 <c>@자동이름</c> + curves.json.
/// </summary>
public static class EaseCurveCommandActions
{
    /// <summary>
    /// 커맨드의 소유 곡선을 보장한다 — 없으면 지금 이징을 구워 만들고 인자를 <c>@이름</c>으로
    /// 바꾼다(편집 둘 = undo 두 단계). 이미 있으면 그대로 돌려준다.
    /// </summary>
    public static (string Name, IReadOnlyList<CurveKey> Keys, EaseKind BakedFrom) EnsureOwned(
        ProjectEditor editor,
        string presentationNodeId,
        string commandId,
        string easeParameterName,
        string? currentEase)
    {
        ArgumentNullException.ThrowIfNull(editor);

        string ownedName = EaseCurve.OwnedNameFor(commandId);

        EaseCurve? existing = editor.Project.EaseCurves.FirstOrDefault(curve =>
            string.Equals(curve.Name, ownedName, StringComparison.Ordinal));

        EaseKind source = Enum.TryParse(
            currentEase, ignoreCase: true, out EaseKind parsed) ? parsed : EaseKind.OutCubic;

        if (existing is not null)
        {
            return (ownedName, existing.Keys, source);
        }

        CurveKey[] baked = EaseCurveBaker.Bake(source);
        editor.SetEaseCurve(ownedName, baked, ownerCommandId: commandId);
        editor.SetPresentationCommandArgument(
            presentationNodeId, commandId, easeParameterName, "@" + ownedName);

        return (ownedName, baked, source);
    }

    /// <summary>보관함 곡선을 커맨드 소유 곡선으로 <b>복사</b>한다 — 이후 편집은 이 커맨드만의 것이다.</summary>
    public static void CopyFromLibrary(
        ProjectEditor editor,
        string presentationNodeId,
        string commandId,
        string easeParameterName,
        EaseCurve libraryCurve)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(libraryCurve);

        string ownedName = EaseCurve.OwnedNameFor(commandId);
        editor.SetEaseCurve(ownedName, libraryCurve.Keys.ToArray(), ownerCommandId: commandId);
        editor.SetPresentationCommandArgument(
            presentationNodeId, commandId, easeParameterName, "@" + ownedName);
    }

    /// <summary>지금 곡선을 이름 붙여 보관함에 <b>복사</b>해 둔다 — 커맨드 쪽 곡선은 그대로다.</summary>
    public static void SaveToLibrary(
        ProjectEditor editor, string name, IReadOnlyList<CurveKey> keys)
    {
        ArgumentNullException.ThrowIfNull(editor);
        editor.SetEaseCurve(name, keys, ownerCommandId: null);
    }

    /// <summary>
    /// 커맨드를 표준 이징으로 되돌린다 — 인자를 enum 이름(기본값이면 null = 생략)으로 바꾸고
    /// 소유 곡선을 지운다. 보관함에 저장해 둔 사본은 남는다.
    /// </summary>
    public static void DiscardOwned(
        ProjectEditor editor,
        string presentationNodeId,
        string commandId,
        string easeParameterName,
        string? easeTokenOrNull)
    {
        ArgumentNullException.ThrowIfNull(editor);

        editor.SetPresentationCommandArgument(
            presentationNodeId, commandId, easeParameterName, easeTokenOrNull);
        editor.RemoveEaseCurve(EaseCurve.OwnedNameFor(commandId));
    }
}
