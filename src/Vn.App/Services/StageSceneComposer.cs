using Ked.Presentation.Core;
using Vn.Authoring.Flow;

namespace Vn.App.Services;

internal sealed record StageRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

internal sealed record StagePortraitPlacement(
    string SlotKey,
    MiniStageSlot Slot,
    StageRect Rect,
    bool IsSpeaker);

internal enum StageDialogueBoxStyle
{
    /// <summary>하단 박스 + 이름표.</summary>
    Speaker,

    /// <summary>박스 없이 본문만.</summary>
    OnlyText,

    /// <summary>상하 밴드 + 중앙 텍스트.</summary>
    LetterBox
}

/// <summary>
/// 대사창 하나의 배치. <see cref="Approximated"/>가 참이면 원래 boxKind를 그대로
/// 그리지 못해 Speaker 근사로 대신했다는 뜻 — 화면은 종류 뱃지를 함께 단다.
/// </summary>
internal sealed record StageDialogueBoxPlacement(
    StageDialogueBoxStyle Style,
    string BoxKind,
    bool Approximated,
    StageRect TextRect,
    StageRect? BoxRect,
    StageRect? NameRect,
    StageRect? TopBand,
    StageRect? BottomBand);

internal sealed record StageSceneLayout(
    double Width,
    double Height,
    IReadOnlyList<StagePortraitPlacement> Portraits,
    StageDialogueBoxPlacement? DialogueBox,
    string? OffStageSpeakerName);

/// <summary>
/// 기준 해상도(기본 1920×1080) 좌표계 위의 무대 배치를 계산하는 순수 함수.
///
/// "완성된 비주얼노벨이라 가정하고 보는 뷰"의 근사다 — 대사창 종류별 레이아웃과
/// 초상화 나열을 해상도 <b>비율</b>로 고정해 창 크기와 무관하게 같은 그림이 나온다.
/// 런타임 픽셀 재현은 2b의 일이고, 여기는 배치 결정만 있다(그리기는 StageSceneView).
/// 도킹 패널과 프리뷰 창이 이 계산 하나를 같이 쓴다 — 사본 금지.
/// </summary>
internal static class StageSceneComposer
{
    public static StageSceneLayout Compose(
        MiniStageState state,
        string? speakerName,
        string? speakerCharacterId,
        double width,
        double height,
        StageState? coreState = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        bool hasSpeaker = !string.IsNullOrWhiteSpace(speakerName);
        (IReadOnlyList<StagePortraitPlacement> portraits, bool speakerOnStage) =
            coreState is null
                ? PlacePortraits(state, speakerCharacterId, width, height)
                : PlaceCorePortraits(state, coreState, speakerCharacterId, width, height);

        return new StageSceneLayout(
            width,
            height,
            portraits,
            PlaceDialogueBox(state.BoxKindFor(hasSpeaker), width, height),
            hasSpeaker && !speakerOnStage ? speakerName : null);
    }

    /// <summary>
    /// 슬롯 부착(stage/layer)의 그리기 순위 (W27). 캔버스는 나중에 그린 것이 위에 오므로
    /// 뒤(far)부터 앞(close) 순으로 정렬한다. 레이어 어휘와 순서는
    /// <see cref="ArgumentTokenCandidates"/>의 layerKey 후보(far→close)가 원천이다 —
    /// 여기 사본을 두지 않는다. 모르는 레이어 토큰은 기본값 mid 자리로 취급한다.
    /// </summary>
    private static int LayerRank(string? layerKey)
    {
        IReadOnlyList<string> layers = Vn.Authoring.Definition.ArgumentTokenCandidates.For("layerKey");

        for (int index = 0; index < layers.Count; index++)
        {
            if (string.Equals(layers[index], layerKey, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return layers.Count / 2; // 기본 레이어 mid의 자리
    }

    /// <summary>
    /// 코어 확정 상태의 실제 좌표로 배치한다 (W25) — 균등 나열이 아니라 재현이다(H-3).
    ///
    /// rect는 <c>CharacterPortraitSprite_Image</c>의 pivot 기준 네 모서리를 리그 트리로
    /// 루트 공간에 변환한 축 정렬 경계이고, 샷은 적용측 규약(보이는 위치 = 논리 × 배율 + pan)
    /// 그대로 씌운다. 좌표 계산은 전부 코어에서 끝났다 — 여기는 좌표계 변환(중앙 원점·y위 →
    /// 캔버스 좌상 원점·y아래)만 있다(D-core-2).
    ///
    /// 그리기 순서 = 슬롯 부착 (stage, layer, 슬롯 키) 오름차순 (W27) — 슬롯 생성 시 고른
    /// 위치(무대/레이어)가 실제 앞뒤 관계로 보인다. 같은 자리면 슬롯 키 순서(결정적).
    ///
    /// 코어가 모르는 슬롯(보완 폴드의 관대한 생성분·slot_tyrant)은 기존 균등 나열로 함께
    /// 그린다 — 코어에 없다고 화면에서 사라지면 침묵이다.
    /// </summary>
    private static (IReadOnlyList<StagePortraitPlacement> Portraits, bool SpeakerOnStage) PlaceCorePortraits(
        MiniStageState state,
        StageState core,
        string? speakerCharacterId,
        double width,
        double height)
    {
        var portraits = new List<StagePortraitPlacement>();
        var uniformLeftovers = new List<KeyValuePair<string, MiniStageSlot>>();
        bool speakerOnStage = false;

        float cameraScale = ShotIntentMath.EvaluateCameraScale(core.Shot.Zoom);
        Vec2 pan = core.Shot.PanInRigSpace;

        IEnumerable<KeyValuePair<string, MiniStageSlot>> drawOrdered = state.VisibleSlots
            .OrderBy(entry => core.TryGetAttachment(entry.Key, out SlotAttachment attachment)
                ? attachment.StageKey ?? "stage00"
                : "stage00", StringComparer.Ordinal)
            .ThenBy(entry => core.TryGetAttachment(entry.Key, out SlotAttachment attachment)
                ? LayerRank(attachment.LayerKey)
                : LayerRank(null))
            .ThenBy(entry => entry.Key, StringComparer.Ordinal);

        foreach ((string slotKey, MiniStageSlot slot) in drawOrdered)
        {
            string imageKey = StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Image");

            if (!core.HasSlot(slotKey) || !core.Nodes.Contains(imageKey))
            {
                uniformLeftovers.Add(new KeyValuePair<string, MiniStageSlot>(slotKey, slot));
                continue;
            }

            RectNodeState imageState = core.Nodes.GetState(imageKey);
            Vec2 size = core.Nodes.GetRectSize(imageKey);

            // 로컬 rect의 네 모서리 (원점 = pivot). 회전·스케일이 섞여도 경계는 보존된다.
            Span<Vec3> corners =
            [
                new Vec3(-imageState.Pivot.X * size.X, -imageState.Pivot.Y * size.Y, 0f),
                new Vec3((1f - imageState.Pivot.X) * size.X, -imageState.Pivot.Y * size.Y, 0f),
                new Vec3(-imageState.Pivot.X * size.X, (1f - imageState.Pivot.Y) * size.Y, 0f),
                new Vec3((1f - imageState.Pivot.X) * size.X, (1f - imageState.Pivot.Y) * size.Y, 0f),
            ];

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (Vec3 corner in corners)
            {
                Vec3 root = core.Nodes.TransformPoint(imageKey, corner);

                // 샷: 보이는 위치 = 논리 위치 × 배율 + pan (ShotIntentMath 규약).
                double viewX = root.X * cameraScale + pan.X;
                double viewY = root.Y * cameraScale + pan.Y;

                minX = Math.Min(minX, viewX);
                maxX = Math.Max(maxX, viewX);
                minY = Math.Min(minY, viewY);
                maxY = Math.Max(maxY, viewY);
            }

            // 루트 공간(중앙 원점·y 위) → 캔버스(좌상 원점·y 아래).
            var rect = new StageRect(
                width / 2 + minX,
                height / 2 - maxY,
                maxX - minX,
                maxY - minY);

            bool isSpeaker = speakerCharacterId is not null &&
                string.Equals(slot.CharacterId, speakerCharacterId, StringComparison.Ordinal);
            speakerOnStage |= isSpeaker;

            portraits.Add(new StagePortraitPlacement(slotKey, slot, rect, isSpeaker));
        }

        if (uniformLeftovers.Count > 0)
        {
            (IReadOnlyList<StagePortraitPlacement> uniform, bool uniformSpeaker) =
                PlaceUniform(uniformLeftovers.ToArray(), speakerCharacterId, width, height);
            portraits.AddRange(uniform);
            speakerOnStage |= uniformSpeaker;
        }

        return (portraits, speakerOnStage);
    }

    /// <summary>
    /// visible 슬롯을 슬롯 키 순서로 하단에 균등 나열한다. 좌표 재현이 아니라 나열이다.
    /// 슬롯이 많아 폭이 넘치면 간격부터 줄이고, 그래도 넘치면 슬롯 폭을 줄인다 —
    /// 무대 밖으로 잘려서 안 보이는 캐릭터를 만들지 않는다.
    /// </summary>
    private static (IReadOnlyList<StagePortraitPlacement> Portraits, bool SpeakerOnStage) PlacePortraits(
        MiniStageState state,
        string? speakerCharacterId,
        double width,
        double height)
    {
        return PlaceUniform(state.VisibleSlots.ToArray(), speakerCharacterId, width, height);
    }

    private static (IReadOnlyList<StagePortraitPlacement> Portraits, bool SpeakerOnStage) PlaceUniform(
        KeyValuePair<string, MiniStageSlot>[] slots,
        string? speakerCharacterId,
        double width,
        double height)
    {
        if (slots.Length == 0)
        {
            return (Array.Empty<StagePortraitPlacement>(), false);
        }

        double slotHeight = height * 0.62;
        double slotWidth = height * 0.32;
        double gap = width * 0.03;
        double total = slots.Length * slotWidth + (slots.Length - 1) * gap;

        if (total > width)
        {
            gap = 0;
            total = slots.Length * slotWidth;

            if (total > width)
            {
                slotWidth = width / slots.Length;
                total = width;
            }
        }

        double x = (width - total) / 2;
        double bottom = height * 0.80;
        var portraits = new List<StagePortraitPlacement>(slots.Length);
        bool speakerOnStage = false;

        foreach ((string slotKey, MiniStageSlot slot) in slots)
        {
            bool isSpeaker = speakerCharacterId is not null &&
                string.Equals(slot.CharacterId, speakerCharacterId, StringComparison.Ordinal);
            speakerOnStage |= isSpeaker;

            portraits.Add(new StagePortraitPlacement(
                slotKey,
                slot,
                new StageRect(x, bottom - slotHeight, slotWidth, slotHeight),
                isSpeaker));
            x += slotWidth + gap;
        }

        return (portraits, speakerOnStage);
    }

    /// <summary>
    /// boxKind별 레이아웃 근사 — Speaker(하단 박스+이름표), OnlyText(무테 본문),
    /// LetterBox(상하 밴드+중앙 텍스트). Portrait/Surface/BlackBook과 미지의 종류는
    /// v1에선 Speaker 근사 + <see cref="StageDialogueBoxPlacement.Approximated"/> 표시다.
    /// </summary>
    private static StageDialogueBoxPlacement PlaceDialogueBox(string boxKind, double width, double height)
    {
        switch (boxKind)
        {
            case "OnlyText":
            {
                StageRect text = new(width * 0.10, height * 0.76, width * 0.80, height * 0.18);
                return new StageDialogueBoxPlacement(
                    StageDialogueBoxStyle.OnlyText, boxKind, Approximated: false,
                    text, BoxRect: null, NameRect: null, TopBand: null, BottomBand: null);
            }

            case "LetterBox":
            {
                double bandHeight = height * 0.12;
                return new StageDialogueBoxPlacement(
                    StageDialogueBoxStyle.LetterBox, boxKind, Approximated: false,
                    new StageRect(width * 0.15, height * 0.40, width * 0.70, height * 0.20),
                    BoxRect: null,
                    NameRect: null,
                    new StageRect(0, 0, width, bandHeight),
                    new StageRect(0, height - bandHeight, width, bandHeight));
            }

            case "Speaker":
                return SpeakerBox(boxKind, approximated: false, width, height);

            default:
                return SpeakerBox(boxKind, approximated: true, width, height);
        }
    }

    private static StageDialogueBoxPlacement SpeakerBox(
        string boxKind,
        bool approximated,
        double width,
        double height)
    {
        StageRect box = new(width * 0.055, height * 0.74, width * 0.89, height * 0.22);

        return new StageDialogueBoxPlacement(
            StageDialogueBoxStyle.Speaker,
            boxKind,
            approximated,
            new StageRect(box.X + width * 0.02, box.Y + height * 0.045, box.Width - width * 0.04, box.Height - height * 0.07),
            box,
            new StageRect(box.X + width * 0.015, box.Y - height * 0.05, width * 0.18, height * 0.055),
            TopBand: null,
            BottomBand: null);
    }
}
