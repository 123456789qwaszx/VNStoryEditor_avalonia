using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // presets/depth.json · presets/focus-tuning.json의 전송 타입.
    // EditorJsonUtility 직렬화 모양 그대로다: {"MonoBehaviour": { ...필드... }}.
    // m_* 메타 필드는 담지 않는다(역직렬화가 무시한다).
    // 필드 의미·단위는 ExportedTuning/schema.md가 규범이다.
    // ─────────────────────────────────────────────────────────────────

    [Serializable]
    public sealed class DepthTuningFileDto
    {
        public DepthTuningBodyDto MonoBehaviour;
    }

    [Serializable]
    public sealed class DepthTuningBodyDto
    {
        public DepthPresetSetDto presets;
        /// <summary>
        /// 숫자 레벨(size c1 5)이 쓰는 커브. 프리셋 표와는 **다른 데이터**다 —
        /// 라벨(far·mid…)은 이 커브 위의 점이 아니라 손으로 튜닝한 별도 값이다.
        /// </summary>
        public DepthLevelTuningDto level;
    }

    [Serializable]
    public sealed class DepthPresetSetDto
    {
        public DepthPresetDto far;
        public DepthPresetDto back;
        public DepthPresetDto mid;
        public DepthPresetDto front;
        public DepthPresetDto close;
        public DepthPresetDto exp1;
        public DepthPresetDto exp2;

        /// <summary>프리셋 토큰 → 값. 모르는 토큰이면 false — 비슷한 이름으로 잇지 않는다.</summary>
        public bool TryGet(string presetKey, out DepthPresetDto preset)
        {
            switch ((presetKey ?? "").Trim().ToLowerInvariant())
            {
                case "far": preset = far; return preset != null;
                case "back": preset = back; return preset != null;
                case "mid": preset = mid; return preset != null;
                case "front": preset = front; return preset != null;
                case "close": preset = close; return preset != null;
                case "exp1": preset = exp1; return preset != null;
                case "exp2": preset = exp2; return preset != null;
                default: preset = null; return false;
            }
        }
    }

    [Serializable]
    public sealed class DepthPresetDto
    {
        public Float2Dto depthY;
        public float depthScale;

        /// <summary>CharacterFocusPreset 정수 값 (0/10/20/30/31/40/41).</summary>
        public int preserveFocusPreset;

        public Float2Dto preserveFocusOffset;
    }


    // ── 레벨 커브 (presets/depth.json 의 "level") ─────────────────────
    //
    // Unity AnimationCurve의 직렬화 모양 그대로다: { "m_Curve": [ {time,value,inSlope,outSlope}, … ] }.
    // m_PreInfinity/m_PostInfinity(WrapMode)는 **읽지 않는다** — 런타임이 그걸 쓰지 않고
    // 직접 외삽하기 때문이다(CharacterDepthLevelTuningSet.EvaluateUnclamped).
    // 흉내 내면 재생과 갈린다.

    [Serializable]
    public sealed class DepthLevelTuningDto
    {
        public AnimationCurveDto yCurve;
        public AnimationCurveDto scaleCurve;

        // farPreserveFocus 등 보존 프리셋은 담지 않는다 — 프리셋 경로와 같은 이유로
        // 런타임이 커맨드 인자로 덮어쓰는 사장 데이터다(SetDepthStageReduction 주석 참조).

        public bool TryResolve(float level, out float depthY, out float depthScale)
        {
            depthY = 0f;
            depthScale = 0f;

            CurveKey[] yKeys = yCurve?.ToKeys();
            CurveKey[] scaleKeys = scaleCurve?.ToKeys();

            if (yKeys == null || yKeys.Length == 0 || scaleKeys == null || scaleKeys.Length == 0)
                return false;

            depthY = CurveFunctions.EvaluateUnclamped(yKeys, level);

            // 런타임과 같은 하한(Mathf.Max(0.0001f, …)).
            float rawScale = CurveFunctions.EvaluateUnclamped(scaleKeys, level);
            depthScale = rawScale < 0.0001f ? 0.0001f : rawScale;

            return true;
        }
    }

    [Serializable]
    public sealed class AnimationCurveDto
    {
        public List<KeyframeDto> m_Curve = new();

        public CurveKey[] ToKeys()
        {
            if (m_Curve == null || m_Curve.Count == 0)
                return null;

            CurveKey[] keys = new CurveKey[m_Curve.Count];

            for (int i = 0; i < m_Curve.Count; i++)
            {
                KeyframeDto k = m_Curve[i];
                keys[i] = new CurveKey(k.time, k.value, k.inSlope, k.outSlope);
            }

            return keys;
        }
    }

    [Serializable]
    public sealed class KeyframeDto
    {
        public float time;
        public float value;
        public float inSlope;
        public float outSlope;
    }
    [Serializable]
    public sealed class FocusTuningFileDto
    {
        public FocusTuningBodyDto MonoBehaviour;
    }

    [Serializable]
    public sealed class FocusTuningBodyDto
    {
        public FocusOffsetSetDto baseOffsets;
        public List<FocusEntryDto> entries = new();

        public bool TryGetEntry(string characterKey, out FocusEntryDto entry)
        {
            if (!string.IsNullOrWhiteSpace(characterKey))
            {
                string key = characterKey.Trim();

                for (int i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i]?.key?.Trim(), key, StringComparison.Ordinal))
                    {
                        entry = entries[i];
                        return true;
                    }
                }
            }

            entry = null;
            return false;
        }
    }

    [Serializable]
    public sealed class FocusEntryDto
    {
        public string key;
        public Float2Dto defaultOffset;
        public FocusOffsetSetDto offsets;
    }

    [Serializable]
    public sealed class FocusOffsetSetDto
    {
        public Float2Dto feet;
        public Float2Dto body;
        public Float2Dto bust;
        public Float2Dto face;
        public Float2Dto faceAura;
        public Float2Dto handLeft;
        public Float2Dto handRight;

        public bool TryGet(string presetName, out Vec2 offset)
        {
            Float2Dto value = presetName switch
            {
                "feet" => feet,
                "body" => body,
                "bust" => bust,
                "face" => face,
                "faceAura" => faceAura,
                "handLeft" => handLeft,
                "handRight" => handRight,
                _ => null,
            };

            offset = value?.ToVec2() ?? Vec2.Zero;
            return value != null;
        }
    }

    /// <summary>
    /// focus 프리셋 어휘. 런타임 CharacterFocusPreset과의 대응은 값으로 잇는다.
    ///
    /// ⚠ 호스트 파서(CharacterFocusPresetParser)에는 별칭 표가 크다(p3·torso·x1 …).
    /// 코어는 정규 이름만 안다 — 실측 결과 원문이 정규 이름만 쓰기 때문이다
    /// (bust·body·face, 별칭 0건). 별칭이 오면 조용히 넘기지 않고 Unhandled로 소리를 낸다.
    /// </summary>
    public static class FocusPresetName
    {
        /// <summary>덤프의 preserveFocusPreset 정수 → 이름. 모르면 false.</summary>
        public static bool TryFromEnumValue(int value, out string name)
        {
            name = value switch
            {
                0 => "feet",
                10 => "body",
                20 => "bust",
                30 => "face",
                31 => "faceAura",
                40 => "handLeft",
                41 => "handRight",
                _ => null,
            };

            return name != null;
        }

        /// <summary>yarn 토큰("bust") 정규화. 모르면 false — 추측 보정하지 않는다.</summary>
        public static bool TryNormalizeToken(string token, out string name)
        {
            name = (token ?? "").Trim().ToLowerInvariant() switch
            {
                "feet" => "feet",
                "body" => "body",
                "bust" => "bust",
                "face" => "face",
                "face_aura" or "faceaura" => "faceAura",
                "hand_left" or "handleft" => "handLeft",
                "hand_right" or "handright" => "handRight",
                _ => null,
            };

            return name != null;
        }
    }
}
