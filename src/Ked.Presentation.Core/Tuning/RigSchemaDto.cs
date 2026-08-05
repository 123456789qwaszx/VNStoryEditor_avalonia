using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // U12-전체가 내보낸 ExportedTuning/rig-schemas.json의 전송 타입.
    //
    // 필드 이름이 JSON 키와 1:1이다 — 바꾸면 덤프를 못 읽는다.
    // 코어는 외부 의존성이 0이라 JSON 파서를 갖지 않는다. 역직렬화는 호스트가 한다:
    //   유니티  : JsonUtility.FromJson<RigSchemasFileDto>(text)
    //   VnTool  : System.Text.Json (IncludeFields = true 필요 — 프로퍼티가 아니라 필드다)
    // 필드의 "해석"은 RigSchemaLoader 한 곳이다 — 해석 사본을 만들지 말 것.
    //
    // 각 필드의 의미·단위는 ExportedTuning/schema.md가 규범이다.
    // ─────────────────────────────────────────────────────────────────

    [Serializable]
    public sealed class RigSchemasFileDto
    {
        public Float2Dto capturedUnderParentSize;
        public List<RigSchemaRigDto> rigs;
    }

    [Serializable]
    public sealed class RigSchemaRigDto
    {
        public string rigKind;
        public string sourcePrefab;
        public List<RigSchemaNodeDto> nodes;
    }

    [Serializable]
    public sealed class RigSchemaNodeDto
    {
        public string id;
        public string parent;          // 비어 있으면 루트 공간 직속 (덤프의 __root)
        public Float2Dto anchoredPosition;
        public Float2Dto anchorMin;
        public Float2Dto anchorMax;
        public Float2Dto pivot;
        public Float2Dto sizeDelta;
        public Float3Dto localScale;
        public Float3Dto localEulerAngles;
        public Float2Dto measuredRectSize; // 캡처 시점 파생값. 재현 입력으로 쓰지 않는다

        /// <summary>가시성 축 대상인가. 구덤프(필드 없음)는 역직렬화 기본값 false로
        /// 안전하게 떨어진다 — JsonUtility는 없는 필드를 0/false로 채운다.</summary>
        public bool hasCanvasGroup;

        /// <summary>hasCanvasGroup일 때만 의미 있는 초기 alpha (스폰 시 가시성).</summary>
        public float canvasGroupAlpha;
    }

    [Serializable]
    public sealed class Float2Dto
    {
        public float x;
        public float y;

        public Vec2 ToVec2() => new Vec2(x, y);
    }

    [Serializable]
    public sealed class Float3Dto
    {
        public float x;
        public float y;
        public float z;

        public Vec3 ToVec3() => new Vec3(x, y, z);
    }
}
