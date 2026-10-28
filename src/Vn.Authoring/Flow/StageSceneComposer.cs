using Ked.Presentation.Core;
using Vn.Authoring.Assets;
using Vn.Authoring.Flow;

namespace Vn.Authoring.Flow;

public sealed record StageRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

public sealed record StagePortraitPlacement(
    string SlotKey,
    MiniStageSlot Slot,
    StageRect Rect,
    bool IsSpeaker);

public enum StageDialogueBoxStyle
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
public sealed record StageDialogueBoxPlacement(
    StageDialogueBoxStyle Style,
    string BoxKind,
    bool Approximated,
    StageRect TextRect,
    StageRect? BoxRect,
    StageRect? NameRect,
    StageRect? TopBand,
    StageRect? BottomBand);

public sealed record StageSceneLayout(
    double Width,
    double Height,
    IReadOnlyList<StagePortraitPlacement> Portraits,
    StageDialogueBoxPlacement? DialogueBox,
    string? OffStageSpeakerName,
    // 카메라(shot)를 태운 배경의 자리 (2026-08-27) — 코어 상태가 없으면(근사 모드) 무대
    // 전체다. 대사창·안내 같은 UI는 카메라를 안 탄다 — 배경과 초상만 무대의 것이다.
    StageRect? Background = null);

/// <summary>
/// 기준 해상도(기본 1920×1080) 좌표계 위의 무대 배치를 계산하는 순수 함수.
///
/// "완성된 비주얼노벨이라 가정하고 보는 뷰"의 근사다 — 대사창 종류별 레이아웃과
/// 초상화 나열을 해상도 <b>비율</b>로 고정해 창 크기와 무관하게 같은 그림이 나온다.
/// 런타임 픽셀 재현은 2b의 일이고, 여기는 배치 결정만 있다(그리기는 StageSceneView).
/// 도킹 패널과 프리뷰 창이 이 계산 하나를 같이 쓴다 — 사본 금지.
/// </summary>
public static class StageSceneComposer
{
    public static StageSceneLayout Compose(
        MiniStageState state,
        string? speakerName,
        string? speakerCharacterId,
        double width,
        double height,
        StageState? coreState = null,
        SurfaceLayoutSet? surfaceLayouts = null)
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
            PlaceDialogueBox(state.BoxKindFor(hasSpeaker), width, height, surfaceLayouts),
            hasSpeaker && !speakerOnStage ? speakerName : null,
            coreState is null
                ? new StageRect(0, 0, width, height)
                : CameraStageRect(coreState, width, height));
    }

    /// <summary>
    /// 카메라(shot)를 태운 무대 전체 rect (2026-08-27 소유자: "shot_to … 원래라면 bg역시도
    /// 크기를 받아야 하는데, 지금은 오직 캐릭터만") — 초상과 같은 적용측 규약
    /// (보이는 위치 = 논리 × 배율 + pan)을 무대 네 모서리에 그대로 적용한 것이다.
    /// 배경은 무대 크기로 깔리므로 이 rect가 곧 배경의 자리다.
    /// </summary>
    private static StageRect CameraStageRect(StageState core, double width, double height)
    {
        float cameraScale = ShotIntentMath.EvaluateCameraScale(core.Shot.Zoom);
        Vec2 pan = core.Shot.PanInRigSpace;

        double halfWidth = width / 2 * cameraScale;
        double halfHeight = height / 2 * cameraScale;

        // 루트 공간(중앙 원점·y 위) → 캔버스(좌상 원점·y 아래) — PlaceCorePortraits와 같은 변환.
        return new StageRect(
            width / 2 + (-halfWidth + pan.X),
            height / 2 - (halfHeight + pan.Y),
            halfWidth * 2,
            halfHeight * 2);
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

    /// <summary>깊이 스케일 — size 프리셋이 CharSlot_DepthScale에 접은 값. 없으면 1(기본).</summary>
    private static float DepthScaleOf(StageState core, string slotKey)
    {
        return core.Nodes.TryGetState(
            StageState.NodeKeyOf(slotKey, "CharSlot_DepthScale"), out RectNodeState depthState)
            ? depthState.LocalScale.X
            : 1f;
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

        // 숨김·빈 슬롯도 배치한다 — 뷰가 네모 윤곽 + 슬롯명 태그로 자리를 보여 준다(W28).
        // 가시성 판정은 뷰의 일이고, 여기는 자리 계산만 한다.
        // 같은 무대·레이어 안에서는 깊이(가까울수록 큰 DepthScale)가 앞이다 (W30) —
        // close로 당긴 캐릭터가 far 캐릭터에 가려지면 화면이 거짓말을 한다.
        IEnumerable<KeyValuePair<string, MiniStageSlot>> drawOrdered = state.Slots
            .OrderBy(entry => core.TryGetAttachment(entry.Key, out SlotAttachment attachment)
                ? attachment.StageKey ?? "stage00"
                : "stage00", StringComparer.Ordinal)
            .ThenBy(entry => core.TryGetAttachment(entry.Key, out SlotAttachment attachment)
                ? LayerRank(attachment.LayerKey)
                : LayerRank(null))
            .ThenBy(entry => DepthScaleOf(core, entry.Key))
            .ThenBy(entry => entry.Key, StringComparer.Ordinal);

        foreach ((string slotKey, MiniStageSlot slot) in drawOrdered)
        {
            string imageKey = StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Image");

            if (!core.HasSlot(slotKey) || !core.Nodes.Contains(imageKey))
            {
                // 코어가 모르는 슬롯은 자리를 계산할 수 없다 — 보이는 것만 균등 나열로 넘긴다.
                if (slot.Visible)
                {
                    uniformLeftovers.Add(new KeyValuePair<string, MiniStageSlot>(slotKey, slot));
                }

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

            // 화자 강조는 보이는 초상의 것이다 — 숨김 슬롯의 윤곽에는 달지 않는다.
            bool isSpeaker = slot.Visible && speakerCharacterId is not null &&
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
    /// boxKind → surface 레이아웃 덤프 키. <b>이름이 사실상 같은 셋만</b> 잇는다 —
    /// 정책 DB(화자→대사창 매핑)는 덤프에 없으므로 그 밖의 종류를 추측으로 잇지 않는다
    /// (원칙 §2.3). 나머지는 기존 근사 + "(근사)" 뱃지가 정직하게 남는다.
    /// </summary>
    private static readonly Dictionary<string, string> SurfaceKeyByBoxKind = new(StringComparer.Ordinal)
    {
        ["Speaker"] = "bottom",
        ["LetterBox"] = "letterbox_bottom",
        ["BlackBook"] = "blackbook_page",
    };

    /// <summary>유니티 비율 앵커(y 아래→위) → 캔버스 rect(y 위→아래).</summary>
    private static StageRect FromAnchors(
        double minX, double minY, double maxX, double maxY, double width, double height)
    {
        return new StageRect(
            minX * width,
            (1 - maxY) * height,
            (maxX - minX) * width,
            (maxY - minY) * height);
    }

    /// <summary>
    /// boxKind별 레이아웃 — surface 레이아웃 덤프가 있고 이름 매핑이 있으면 그 자리
    /// 그대로(텍스트·이름표 rect = 덤프 앵커), 없으면 기존 고정 비율 근사다.
    /// Speaker(하단 박스+이름표), OnlyText(무테 본문), LetterBox(상하 밴드+텍스트),
    /// BlackBook(책장 박스). Portrait/Surface와 미지의 종류는 Speaker 근사 +
    /// <see cref="StageDialogueBoxPlacement.Approximated"/> 표시다.
    /// </summary>
    private static StageDialogueBoxPlacement PlaceDialogueBox(
        string boxKind, double width, double height, SurfaceLayoutSet? surfaceLayouts = null)
    {
        if (surfaceLayouts is not null &&
            SurfaceKeyByBoxKind.TryGetValue(boxKind, out string? surfaceKey) &&
            surfaceLayouts.TryGet(surfaceKey, out SurfaceLayoutPreset preset))
        {
            StageRect text = FromAnchors(
                preset.LineMinX, preset.LineMinY, preset.LineMaxX, preset.LineMaxY, width, height);

            StageRect? name = preset.UseName
                ? FromAnchors(preset.NameMinX, preset.NameMinY, preset.NameMaxX, preset.NameMaxY, width, height)
                : null;

            if (boxKind == "LetterBox")
            {
                double bandHeight = height * 0.12;
                return new StageDialogueBoxPlacement(
                    StageDialogueBoxStyle.LetterBox, boxKind, Approximated: false,
                    text,
                    BoxRect: null,
                    name,
                    new StageRect(0, 0, width, bandHeight),
                    new StageRect(0, height - bandHeight, width, bandHeight));
            }

            // 박스 배경은 텍스트 자리 둘레의 여백이다 — 자리(rect)는 덤프, 장식은 툴의 몫.
            var box = new StageRect(
                text.X - width * 0.02,
                text.Y - height * 0.03,
                text.Width + width * 0.04,
                text.Height + height * 0.05);

            return new StageDialogueBoxPlacement(
                StageDialogueBoxStyle.Speaker, boxKind, Approximated: false,
                text, box, name, TopBand: null, BottomBand: null);
        }

        return PlaceDialogueBoxFallback(boxKind, width, height);
    }

    private static StageDialogueBoxPlacement PlaceDialogueBoxFallback(string boxKind, double width, double height)
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
