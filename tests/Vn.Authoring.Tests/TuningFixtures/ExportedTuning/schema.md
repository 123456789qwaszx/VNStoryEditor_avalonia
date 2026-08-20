# ExportedTuning — 연출 기준값 덤프 스키마

`Assets/Editor/PresentationTuningExporter.cs`가 내보낸다.
**이 JSON들의 스키마가 곧 `Ked.Presentation.Core`의 Tuning 타입 모양이 된다** —
코어는 게임별 값을 코드로 갖지 않고 여기서 읽는다(코어 계약 2).

재생성: 메뉴 `Ked / U12 / Export Presentation Tuning Dump`
(또는 `-batchmode -executeMethod PresentationTuningExporter.ExportAll`).

## 0. 읽는 쪽이 먼저 알아야 할 것

**역직렬화는 호스트가 한다.** 코어는 외부 의존성이 0이라 JSON 파서를 갖지 않는다.
- 유니티: `JsonUtility.FromJson<T>(text)`
- VnTool 등 순수 .NET: `System.Text.Json` — **`IncludeFields = true` 필수**
  (DTO가 프로퍼티가 아니라 필드다)

**`JsonUtility`의 "없는 필드 = 0" 함정.** 구덤프를 새 DTO로 읽으면 없는 필드가 예외가
아니라 기본값(0/false/null)으로 채워진다. 그래서 나중에 필드를 더할 때는
"값이 0인 것"과 "필드가 없는 것"을 구분할 수단(bool 게이트 등)을 같이 넣어야 한다.

**좌표 규약.** 픽셀 단위, 기준 해상도(1920×1080) 공간, **y는 위가 양수**.
코어의 좌표 규약 전문은 `Assets/Ked.Presentation.Core/Documentation~/transform-math-and-epsilon.md`.

---

## 1. `base-resolution.json`

프레젠테이션 `CanvasScaler` 실측값. 코어의 **루트 공간 크기**이자 **1u 환산의 유일한 입력**이다.

| 필드 | 뜻 |
|---|---|
| `canvasName` | 실측한 Canvas 오브젝트 이름 (추적용) |
| `uiScaleMode` | `ScaleWithScreenSize`가 아니면 익스포터가 경고한다 — 좌표 전제가 흔들린다 |
| `referenceResolution` | 기준 해상도. 코어 `RectSpace`의 `Size`, `ReferenceStageWidth`의 원천 |
| `matchWidthOrHeight` | 0=폭 기준, 1=높이 기준, 0.5=절충 |

현재 값: `[UIManager]` · `ScaleWithScreenSize` · `1920×1080` · `0.5`.

---

## 2. `rig-schemas.json`

리그 4종의 **노드 트리 초기 상태**. 빌더 로직을 전사한 것이 아니라,
실제 프리팹·실제 빌더로 리그를 세운 뒤 **실물 `RectTransform`에서 읽은 값**이다
(`BindRefsFromRoot`의 그래프 검증·복구까지 지난 뒤의 상태 — 런타임과 같은 경로).

```
{
  "capturedUnderParentSize": { "x": 1920, "y": 1080 },
  "rigs": [ { "rigKind", "sourcePrefab", "nodes": [ ... ] }, ... ]
}
```

### `capturedUnderParentSize` — 재현의 전제

익스포터는 **가운데 pivot(0.5, 0.5)**, 이 크기의 임시 부모 밑에서 캡처했다.
코어에서 이 덤프를 되세울 때는 같은 공간을 써야 한다:

```csharp
new RectSpace(capturedUnderParentSize, Vec2.Half)
```

스트레치 앵커 노드의 rect 크기가 부모 크기에서 파생되므로, 이게 어긋나면
트리 전체 좌표가 어긋난다.

### `rigs[]`

| 필드 | 뜻 |
|---|---|
| `rigKind` | `character` · `background` · `screenEffect` — 익스포터가 내보내는 세 종류다. 노드 수는 스키마를 따라 변하므로 여기 적지 않는다 |
| `sourcePrefab` | 세울 때 쓴 프리팹 에셋 경로. **빈 문자열이면 프리팹 없이 스키마 베이크로 세웠다는 뜻** |
| `nodes` | 부모가 항상 자식보다 먼저 온다 — 순서대로 넣으면 트리가 세워진다 |

> `screenEffect`의 `sourcePrefab`이 비어 있는 것은 정상이다. 프리팹 필드 자체가 없어
> **부트스트랩도 `null`을 넘긴다** — 스키마 베이크가 곧 런타임 경로다.

### `nodes[]` — 코어 `RectNodeState`와 1:1

| 필드 | 단위/뜻 |
|---|---|
| `id` | 논리 노드 키. 스키마 enum 이름 그대로. 리그 루트는 **`__root`** |
| `parent` | 부모의 `id`. **빈 문자열이면 리그 루트(`__root`) 직속** |
| `anchoredPosition` | px. 앵커 기준점에서 pivot까지의 오프셋 |
| `anchorMin` / `anchorMax` | 0~1 비율. 같으면 고정 앵커, 다르면 스트레치 |
| `pivot` | 0~1 비율. 기본 (0.5, 0.5). 바닥 pivot 노드는 (0.5, 0) |
| `sizeDelta` | px. **스트레치 앵커면 "앵커 간격 대비 증감", 고정 앵커면 "크기 그 자체"** |
| `localScale` | 무단위 배율 (x, y, z) |
| `localEulerAngles` | 도(°). 적용 순서는 Unity `Quaternion.Euler`와 같은 **Z → X → Y** |
| `measuredRectSize` | ⚠ **재현 입력이 아니다.** 캡처 시점의 파생 rect 크기 — 검산용으로만 쓴다 |

`localPosition.z`와 `offsetMin`/`offsetMax`는 담지 않는다
(전자는 리그가 안 쓰고, 후자는 `anchoredPosition` + `sizeDelta`의 다른 표현이라 중복).

### 검산 포인트

`character` 리그에서 `pivot`이 `(0.5, 0)`인 노드의 목록이
**`CharacterRigSchema`의 `NeedsBottomPivot` 노드와 정확히 일치**해야 한다.

개수를 여기 적지 않는 이유: 스키마가 바뀌면 문서가 거짓이 된다. 실제로 한 번 그랬다 —
이모지 서브시스템(리그 노드 39개)을 지운 뒤 덤프를 재생성하지 않아,
문서의 "11개"와 코드의 5개가 하루 넘게 어긋나 있었다.

어긋나면 값이 실측이 아니거나 **덤프가 낡은 것이다.** 후자가 훨씬 흔하다 —
스키마를 바꿨으면 `Ked / U12 / Export Presentation Tuning Dump`를 다시 돌려야 한다.
`RigSchemaTreeUnityParityTests`가 이 덤프를 라이브 리그와 대조하므로,
재생성을 빠뜨리면 **테스트가 코드가 아니라 낡은 덤프를 신고한다.**

---

## 3. `presets/*.json`

유니티 `EditorJsonUtility.ToJson` 출력 **그대로**다. 필드를 골라 재조립하지 않는다 —
전사 실수가 들어올 자리를 없애기 위해서다.

따라서 모든 파일이 이 껍데기를 쓴다:

```json
{ "MonoBehaviour": { "m_Enabled": ..., "m_Name": ..., "m_EditorClassIdentifier": ..., <실제 필드들> } }
```

`m_*`는 유니티 메타 필드다. **코어 DTO는 `MonoBehaviour` 래퍼와 실제 필드만 담고
`m_*`는 무시한다.**

에셋 참조 필드가 있으면 `{"instanceID": ...}`로 직렬화되어 이 덤프만으로는 해석할 수 없다.
익스포터가 그런 파일을 만나면 경고한다. **현재 9개 파일 모두 `instanceID` 없음.**

### `depth.json` — `CharacterDepthTuningSO`

`size` 계열 커맨드가 쓰는 depth 프리셋.

```
MonoBehaviour
├─ presets : { far, back, mid, front, close, exp1, exp2 }
│    └─ depthY(px, Vec2) · depthScale(무단위) ·
│       preserveFocusPreset(int, 아래 표) · preserveFocusOffset(px, Vec2)
└─ level   : { yCurve, scaleCurve, far/mid/close/frontPreserveFocus }
```

현재 값:

| 프리셋 | `depthY.y` | `depthScale` | `preserveFocusPreset` |
|---|---|---|---|
| far | 480 | 1.00 | 0 (Feet) |
| back | 240 | 1.14 | 20 (Bust) |
| mid | 0 | 1.00 | 20 (Bust) |
| front | −320 | 1.38 | 20 (Bust) |
| close | 440 | 1.58 | 30 (Face) |

> `level`은 `AnimationCurve` 기반이라 코어 DTO에 담지 않는다.
> 레벨 수치 입력(커브 조회)은 폴드 미지원 — `Unhandled`로 남긴다.

### `focus-tuning.json` — `CharacterFocusTuningDBSO`

focus 지점의 로컬 오프셋. `place` · `size`의 focus 보존 · `shot_focus_to`가 쓴다.

```
MonoBehaviour
├─ baseOffsets : { feet, body, bust, face, faceAura, handLeft, handRight }  (px, Vec2)
└─ entries[]   : { key(캐릭터), defaultOffset(px), offsets(baseOffsets와 같은 모양) }
```

해석 규약(런타임 `ResolveOffset`과 같은 합):

```
offset = baseOffsets[preset]
       + entries[character].defaultOffset
       + entries[character].offsets[preset]
       + 커맨드 오프셋
```

현재 `baseOffsets`: feet (0, 480) · body (0, 680) · bust (0, 820) · face (0, 950) ·
faceAura (0, 950) · handLeft (−80, 0) · handRight (80, 0). 엔트리 6건.

**`preserveFocusPreset` 정수 ↔ 이름 대응** (`CharacterFocusPreset`):

| 값 | 이름 |
|---|---|
| 0 | Feet |
| 10 | Body |
| 20 | Bust |
| 30 | Face |
| 31 | FaceAura |
| 40 | HandLeft |
| 41 | HandRight |

### `role-anchor.json` — `RoleAnchorTuningDBSO`

`show` / `set_anchor`가 캐릭터를 역할 기본 자리에 세울 때 쓰는 값.

```
MonoBehaviour.entries[] : { key(캐릭터), offset(px, Vec2), visualScale(무단위) }
```

`visualScale`은 적용 시 하한 `0.0001`로 클램프한다(런타임 규약).

> ⚠ **엔트리가 전부 기본값(0,0 / 1)이 아니다.** 현재 11건 중 2건이 다르다:
> `tyrant` = offset (−30, −800), scale 5.0 / `Amber` = offset (330, 0), scale 10.0.
> 폴드에서 `Default(0/1)`로 가정하면 이 두 캐릭터가 나오는 장면이 어긋난다 —
> **이 파일을 tuning으로 배선해야 한다.**

### `visual-focus.json` · `mask-motion.json` · `screen-flash/noise/vignette.json` · `surface-layout.json`

전부 `MonoBehaviour.entries[]` 배열이고 각 엔트리의 첫 필드가 `key`(프리셋 이름)다.
아직 코어가 소비하지 않는다 — 해당 커맨드가 코어로 이관될 때 DTO를 만든다.

| 파일 | 엔트리 | 성격 |
|---|---|---|
| `visual-focus` | 8 | 캐릭터 dim/rim 강조 (색·세기) |
| `mask-motion` | 20 | 화면 전환 마스크 모션 (px·초·ease) |
| `screen-flash` | 5 | 플래시 (색·세기·attack/hold/release 초) |
| `screen-noise` | 7 | 노이즈 (세기·스케일·속도·대비) |
| `screen-vignette` | 9 | 비네트 (세기·반경·softness·aspect) |
| `surface-layout` | 5 | 대사창 레이아웃 (앵커·pivot·px·폰트) |

---

## 4. `portrait-dimensions.json`

`Assets/Data/Generated/PortraitGeneratedDB.asset` → (캐릭터 · 변형 · 표정) → 초상 스프라이트 픽셀 치수.

프리셋과 달리 직렬화 그대로 낼 수 없어 따로 있다: DB가 들고 있는 건 `Sprite` **참조**이고,
`EditorJsonUtility`는 그걸 `instanceID`로만 적는다 — 덤프만으로는 해석되지 않는 값이다.
그래서 익스포터가 스프라이트를 실제로 열어 `sprite.rect`를 읽는다.

| 필드 | 뜻 |
|---|---|
| `sourceAsset` | 원천 에셋 경로 (재현 정보) |
| `entries[].character` · `.variant` · `.emotion` | **정규화하지 않은 원본 키** |
| `entries[].width` · `.height` | `sprite.rect`의 픽셀 크기 |

### 키를 정규화하지 않고 내는 이유

정규화 규칙(캐릭터 소문자화 · 변형은 **마지막 글자만** · 표정 2자리)은 런타임
`PortraitResolver.MakeKey`가 갖고 있다. 덤프에서 미리 접으면 같은 규칙이 두 곳에 살게 되고,
한쪽만 바뀌는 순간 폴드가 다른 스프라이트를 고른다. 조회 시점에 코어
`PortraitKeyNormalizer`가 같은 규칙을 적용한다.

그래서 `variant`가 `yoonsaea_a`처럼 접두사를 달고 있어도 키는 마지막 글자 `a`다.

### 코어가 쓰는 값은 종횡비 하나

초상 사이징은 `HeightFitPreserveAspect` — `폭 = 부모 rect 높이 × (width/height)`,
`sizeDelta = (폭, 0)`. 폭·높이를 둘 다 남기는 것은 나중에 값이 어긋났을 때
어느 쪽이 변했는지 알기 위해서다.

**종횡비는 캐릭터당 하나가 아니다.** 실측 덤프에서 `yoonsaea_a/01`은 378×658이고
`yoonsaea_b/01`은 425×746이다 — 변형·표정마다 다르다. 캐릭터 단위로 균일하다고 가정하면
`pose` 이후 구간이 통째로 어긋난다.

없는 조합은 `(캐릭터, 'a', "01")`로 한 번 물러선다(런타임 폴백과 같다). 그것도 없으면
사이징이 `Unhandled`로 남는다 — 짐작으로 잇지 않는다.

---

## 5. `export-report.json`

| 필드 | 뜻 |
|---|---|
| `exportedAtUtc` · `unityVersion` · `scenePath` | 재현 정보 |
| `warnings` | **비어 있어야 정상이다.** 한 건이라도 있으면 덤프를 믿지 말 것 |
| `knownButNotExported` | 존재를 알지만 이번 범위에 없어 내보내지 않은 에셋 — 조용히 빠뜨린 것과 구분한다 |

---

## 6. 덤프가 낡으면

빌더나 프리팹을 고치고 재내보내기를 잊으면 코어 계산이 조용히 어긋난다.
그걸 막는 장치는 **코어 쪽 유니티 대조 테스트**다 — 이 덤프로 세운 트리와
실제 리그를 비교해서 어긋나면 실패한다. 값을 손으로 고치지 말고 익스포터를 다시 돌릴 것.
