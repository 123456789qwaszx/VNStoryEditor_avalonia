using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // U12 프리셋 덤프(presets/depth.json · focus-tuning.json)의 전송 타입.
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
        // levelTuning(AnimationCurve)은 담지 않는다 — 커브 폴드는 미지원(레벨 입력은 Unhandled).
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
            switch (presetKey)
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
        public int preserveFocusPreset;   // CharacterFocusPreset 값 (0/10/20/30/31/40/41)
        public Float2Dto preserveFocusOffset;
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
        public List<FocusEntryDto> entries = new List<FocusEntryDto>();

        public bool TryGetEntry(string key, out FocusEntryDto entry)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i]?.key, key, StringComparison.Ordinal))
                {
                    entry = entries[i];
                    return true;
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

    // ── 초상 치수 (portrait-dimensions.json) ──────────────────────────

    [Serializable]
    public sealed class PortraitDimensionsFileDto
    {
        public List<PortraitDimensionDto> entries = new List<PortraitDimensionDto>();

        /// <summary>
        /// (캐릭터, 변형 접미사, 정규화된 표정)의 종횡비. 런타임 PortraitResolver와 같은
        /// 조회·폴백 규약: 정확 일치 → (캐릭터, 'a', "01") 폴백 → 실패.
        /// </summary>
        public bool TryGetAspect(
            string characterKey, char variantSuffix, string emotion,
            out float aspect, out string reason)
        {
            characterKey = (characterKey ?? "").Trim().ToLowerInvariant();
            variantSuffix = char.ToLowerInvariant(variantSuffix);

            // 런타임 MakeKey는 DB 엔트리와 요청 양쪽에 같은 정규화를 적용한다.
            // 지원하지 않는 토큰은 빈 키로 exact 조회한 뒤 a/01 폴백으로 간다.
            if (!PortraitEmotionCode.TryNormalize(emotion, out string normalizedEmotion))
                normalizedEmotion = "";

            if (TryFind(characterKey, variantSuffix, normalizedEmotion, out aspect))
            {
                reason = null;
                return true;
            }

            // 리졸버의 폴백: 기본 변형 'a' + 표정 "01".
            if (TryFind(characterKey, 'a', "01", out aspect))
            {
                reason = null;
                return true;
            }

            reason = $"초상 치수가 덤프에 없다: {characterKey}/{variantSuffix}/{normalizedEmotion}";
            return false;
        }

        private bool TryFind(string characterKey, char variantSuffix, string emotion, out float aspect)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                PortraitDimensionDto entry = entries[i];

                if (entry == null || entry.height <= 0f)
                    continue;

                if (!string.Equals(
                        (entry.character ?? "").Trim().ToLowerInvariant(), characterKey, StringComparison.Ordinal))
                    continue;

                // 변형은 접미사 한 글자로 대응한다 ("parkeunseol_a" → 'a') — 리졸버 규약.
                string variant = (entry.variant ?? "").Trim().ToLowerInvariant();
                char suffix = variant.Length > 0 ? variant[variant.Length - 1] : 'a';

                if (suffix != variantSuffix)
                    continue;

                if (!PortraitEmotionCode.TryNormalize(entry.emotion, out string entryEmotion))
                    entryEmotion = "";

                if (!string.Equals(entryEmotion, emotion, StringComparison.Ordinal))
                    continue;

                aspect = entry.width / entry.height;
                return true;
            }

            aspect = 0f;
            return false;
        }
    }

    /// <summary>초상 표정 코드 규약 (런타임 PortraitResolver.NormalizeEmotionCode와 동일).</summary>
    public static class PortraitEmotionCode
    {
        public const string Default = "01";

        /// <summary>"2" → "02", "02" → "02". 그 외는 실패 — 추측 보정하지 않는다.</summary>
        public static bool TryNormalize(string input, out string code)
        {
            code = null;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            input = input.Trim();

            if (input.Length == 2 && IsDigit(input[0]) && IsDigit(input[1]))
            {
                code = input;
                return true;
            }

            if (input.Length == 1 && IsDigit(input[0]))
            {
                code = "0" + input;
                return true;
            }

            return false;
        }

        /// <summary>show의 faceToken 별칭("e1"/"emo2"/"face3") → 원시 표정 토큰. 빈 값은 "2" (런타임 규약).</summary>
        public static string ParseShowFaceAlias(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "2";

            string s = token.Trim().ToLowerInvariant();

            if (s.StartsWith("emotion", StringComparison.Ordinal)) return s.Substring(7);
            if (s.StartsWith("emo", StringComparison.Ordinal)) return s.Substring(3);
            if (s.StartsWith("face", StringComparison.Ordinal)) return s.Substring(4);
            if (s.StartsWith("e", StringComparison.Ordinal)) return s.Substring(1);

            return s;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
    }

    [Serializable]
    public sealed class PortraitDimensionDto
    {
        public string character;
        public string variant;
        public string emotion;
        public float width;
        public float height;
    }

    /// <summary>focus 프리셋 어휘 — 런타임 CharacterFocusPreset과의 대응은 값으로 잇는다.</summary>
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