using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // ExportedTuning/rig-schemas.json의 전송 타입.
    //
    // 필드 이름이 JSON 키와 1:1이다 — 바꾸면 덤프를 못 읽는다.
    // 코어는 외부 의존성이 0이라 JSON 파서를 갖지 않는다. 역직렬화는 호스트가 한다:
    //   유니티  : JsonUtility.FromJson<RigSchemasFileDto>(text)
    //   VNTool  : System.Text.Json (IncludeFields = true 필요 — 프로퍼티가 아니라 필드다)
    //
    // 필드의 "해석"은 RigSchemaLoader 한 곳이다 — 해석 사본을 만들지 말 것.
    // 각 필드의 의미·단위는 ExportedTuning/schema.md가 규범이다.
    // ─────────────────────────────────────────────────────────────────

    [Serializable]
    public sealed class RigSchemasFileDto
    {
        /// <summary>익스포터가 캡처할 때 쓴 부모 크기. 되세울 때 같은 공간을 써야 한다.</summary>
        public Float2Dto capturedUnderParentSize;

        public List<RigSchemaRigDto> rigs;
    }

    [Serializable]
    public sealed class RigSchemaRigDto
    {
        public string rigKind;

        /// <summary>세울 때 쓴 프리팹 경로. 빈 값이면 스키마 베이크로 세웠다는 뜻(추적용).</summary>
        public string sourcePrefab;

        /// <summary>부모가 항상 자식보다 먼저 온다 — 순서대로 넣으면 트리가 선다.</summary>
        public List<RigSchemaNodeDto> nodes;
    }

    [Serializable]
    public sealed class RigSchemaNodeDto
    {
        public string id;

        /// <summary>비어 있으면 리그 루트(__root) 직속.</summary>
        public string parent;

        public Float2Dto anchoredPosition;
        public Float2Dto anchorMin;
        public Float2Dto anchorMax;
        public Float2Dto pivot;
        public Float2Dto sizeDelta;
        public Float3Dto localScale;
        public Float3Dto localEulerAngles;

        /// <summary>
        /// ⚠ 재현 입력이 아니다. 캡처 시점의 파생 rect 크기 —
        /// 트리를 세운 뒤 GetRectSize와 대조하는 검산용이다.
        /// </summary>
        public Float2Dto measuredRectSize;

        /// <summary>
        /// 가시성 축 대상인가. 구덤프(필드 없음)는 역직렬화 기본값 false로 안전하게 떨어진다 —
        /// JsonUtility가 없는 필드를 0/false로 채우기 때문에, alpha만 두면
        /// "CanvasGroup이 없다"와 "alpha가 0이다"가 구분되지 않는다.
        /// </summary>
        public bool hasCanvasGroup;

        /// <summary>hasCanvasGroup일 때만 의미 있는 스폰 직후 alpha.</summary>
        public float canvasGroupAlpha;
    }

    [Serializable]
    public sealed class Float2Dto
    {
        public float x;
        public float y;

        public Vec2 ToVec2() => new(x, y);
    }

    [Serializable]
    public sealed class Float3Dto
    {
        public float x;
        public float y;
        public float z;

        public Vec3 ToVec3() => new(x, y, z);
    }
}