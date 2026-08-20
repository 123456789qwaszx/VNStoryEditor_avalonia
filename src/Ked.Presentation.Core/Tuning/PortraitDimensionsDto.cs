using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// portrait-dimensions.json — (캐릭터 · 변형 · 표정) → 초상 스프라이트 픽셀 치수.
    ///
    /// 조회 규약은 런타임 PortraitResolver를 그대로 따른다. 그게 요점이다 —
    /// 폴드가 다른 스프라이트를 고르면 폭이 달라지고, 그건 곧 불일치다.
    ///   · 키 정규화: 캐릭터 소문자 · 변형은 **마지막 글자만**(`body_b` → `b`) · 표정 2자리
    ///   · 중복 키는 **먼저 들어온 것이 이긴다** (PortraitResolver의 TryAdd와 같다)
    ///   · 정확 일치 실패 시 (캐릭터, 'a', "01")로 한 번 물러선다
    /// </summary>
    [Serializable]
    public sealed class PortraitDimensionsFileDto
    {
        /// <summary>PortraitResolver.DefaultVariant. 변형 인자가 없을 때 서는 자리.</summary>
        public const string DefaultVariantKey = "a";

        public string sourceAsset;
        public List<PortraitDimensionDto> entries;

        [NonSerialized] private Dictionary<(string, char, string), PortraitDimensionDto> _index;

        /// <summary>
        /// 종횡비(가로/세로). 사이징이 필요로 하는 유일한 값이다.
        /// 찾지 못하면 false — 짐작으로 잇지 않는다(Unhandled로 소리를 낸다).
        /// </summary>
        public bool TryGetAspect(
            string characterKey, string variantKey, string emotionKey,
            out float aspect, out string reason)
        {
            aspect = 0f;

            // 비어 있는 인자만 기본값으로 채운다 — PortraitResolver.Resolve와 같은 자리다.
            // 값이 있는데 규격 밖인 표정("abc")은 여기서 손대지 않는다:
            // 정규화가 빈 코드를 내고, 그 키로 조회했다가 폴백으로 물러서는 것이 런타임 동작이다.
            if (string.IsNullOrEmpty(variantKey))
                variantKey = DefaultVariantKey;

            if (string.IsNullOrEmpty(emotionKey))
                emotionKey = PortraitKeyNormalizer.DefaultEmotionCode;

            char variantSuffix = PortraitKeyNormalizer.VariantSuffix(variantKey);
            string emotionCode = PortraitKeyNormalizer.EmotionCode(emotionKey);
            string character = PortraitKeyNormalizer.CharacterKey(characterKey);

            if (string.IsNullOrEmpty(character))
            {
                reason = "초상 치수를 찾을 캐릭터 키가 없다 (cast 선행 필요)";
                return false;
            }

            EnsureIndex();

            if (!_index.TryGetValue((character, variantSuffix, emotionCode), out PortraitDimensionDto entry) &&
                !_index.TryGetValue((character, PortraitKeyNormalizer.DefaultVariantSuffix,
                    PortraitKeyNormalizer.DefaultEmotionCode), out entry))
            {
                reason =
                    $"초상 치수가 없다: 캐릭터='{character}', 변형='{variantSuffix}', 표정='{emotionCode}' " +
                    $"(폴백 '{PortraitKeyNormalizer.DefaultVariantSuffix}'/" +
                    $"'{PortraitKeyNormalizer.DefaultEmotionCode}'도 없다)";

                return false;
            }

            if (!(entry.height > 0f))
            {
                reason = $"초상 치수의 높이가 유효하지 않다: {entry}";
                return false;
            }

            aspect = entry.width / entry.height;
            reason = null;
            return true;
        }

        private void EnsureIndex()
        {
            if (_index != null)
                return;

            _index = new Dictionary<(string, char, string), PortraitDimensionDto>();

            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                PortraitDimensionDto entry = entries[i];

                if (entry == null)
                    continue;

                var key = (
                    PortraitKeyNormalizer.CharacterKey(entry.character),
                    PortraitKeyNormalizer.VariantSuffix(entry.variant),
                    PortraitKeyNormalizer.EmotionCode(entry.emotion));

                // 먼저 들어온 것이 이긴다 — PortraitResolver가 중복 키를 버리는 방향과 같다.
                if (!_index.ContainsKey(key))
                    _index.Add(key, entry);
            }
        }
    }

    [Serializable]
    public sealed class PortraitDimensionDto
    {
        public string character;
        public string variant;
        public string emotion;
        public float width;
        public float height;

        public override string ToString() => $"{character}|{variant}|{emotion} = {width}x{height}";
    }

    /// <summary>
    /// 초상 키 정규화 — 런타임 PresentationKeyNormalizer · PortraitResolver의 규칙을 옮긴 것.
    /// 두 곳이 갈라지면 폴드가 다른 스프라이트를 고르므로, 바꿀 때는 반드시 함께 바꾼다.
    /// </summary>
    public static class PortraitKeyNormalizer
    {
        public const char DefaultVariantSuffix = 'a';
        public const string DefaultEmotionCode = "01";

        public static string CharacterKey(string key) => (key ?? "").Trim().ToLowerInvariant();

        /// <summary>변형 키의 **마지막 글자**가 곧 접미사다 (`a` → 'a', `body_b` → 'b').</summary>
        public static char VariantSuffix(string variantKey)
        {
            variantKey = (variantKey ?? "").Trim().ToLowerInvariant();

            return variantKey.Length == 0 ? '\0' : variantKey[variantKey.Length - 1];
        }

        /// <summary>
        /// 표정 코드를 두 자리로 (`2` → `02`, `02` → `02`).
        /// 숫자가 아니면 빈 문자열 — 런타임도 그렇게 두고 그 키로 조회한다(조용히 기본값으로 잇지 않는다).
        /// </summary>
        public static string EmotionCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            input = input.Trim();

            if (input.Length == 2 && IsAsciiDigit(input[0]) && IsAsciiDigit(input[1]))
                return input;

            if (input.Length == 1 && IsAsciiDigit(input[0]))
                return "0" + input;

            return "";
        }

        /// <summary>
        /// show의 faceToken → 표정 인자. `e1`·`emo2`·`emotion3`·`face4`의 접두사를 벗긴다.
        /// **빈 토큰은 "2"다** — 생략된 인자의 기본값("e1")과는 다른 규칙이니 헷갈리지 말 것.
        /// (런타임 ShowFaceAliasParser와 같다)
        /// </summary>
        public static string ParseShowFaceAlias(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "2";

            string s = token.Trim().ToLowerInvariant();

            if (s.StartsWith("emotion", StringComparison.Ordinal))
                return s.Substring("emotion".Length);

            if (s.StartsWith("emo", StringComparison.Ordinal))
                return s.Substring("emo".Length);

            if (s.StartsWith("face", StringComparison.Ordinal))
                return s.Substring("face".Length);

            if (s.StartsWith("e", StringComparison.Ordinal))
                return s.Substring(1);

            return token.Trim();
        }

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}
