# RectChainMath — 좌표 규약과 허용 오차(ε) 정책

코어 좌표 계산의 바닥에 대한 규범 문서.
**이 문서의 ε 정책은 U14 등가성 하네스가 그대로 쓴다** — 여기서 값을 유도해 두는 이유가 그것이다.

## 1. 좌표 규약

- **"월드" = rootSpace 소유자의 로컬 좌표.** 절대 월드가 아니다.
  `CharacterPlacementTargetLedger`의 `stopRoot`(RigSpaceRoot)가 하던 역할과 같다.
  호스트가 리그를 화면 어디에 놓든 코어 계산은 영향받지 않는다.
  (`RectChainMathUnityParityTests.루트를_옮기고_돌려도_코어_결과는_같다`가 이걸 판정한다.)
- 체인은 루트에 가까운 쪽부터: `chain[0]`의 부모가 `rootSpace`다.
- 로컬 변환: `p' = t + R(euler) · (scale ⊙ p)`.
  `t`는 `anchoredPosition`이 아니라 앵커 수학(`LocalPosition`)을 거친 **pivot의 부모 로컬 좌표**다.
- 회전 적용 순서는 Unity `Quaternion.Euler`와 동일: **Z → X → Y.**
- `localPosition.z`는 항상 0으로 취급한다(리그가 쓰지 않는다 — `RectNodeState` 주석 참조).
- 스케일 0인 노드의 `InverseTransformPoint`는 정의하지 않는다(유니티도 특이 행렬).
  방어하지 않는 것이 규약이다 — 조용히 0을 돌려주면 잘못된 좌표가 그대로 흘러간다.

### 앵커 수학이 틀리기 쉬운 한 자리

```
rectMin  = -parentPivot * parentSize            부모 rect의 최소 모서리
anchorLo = rectMin + node.AnchorMin * parentSize
anchorHi = rectMin + node.AnchorMax * parentSize
ref      = anchorLo + (anchorHi - anchorLo) * node.Pivot     ← 자기 pivot 비율
결과      = ref + node.AnchoredPosition
```

마지막 보간에 `node.Pivot` 대신 `0.5`를 쓰면 **스트레치 풀 + 가운데 pivot에서는 값이 같아서
통과하고, 바닥 pivot 노드에서만 틀린다.** 캐릭터 리그의 `NeedsBottomPivot` 노드 11개가
정확히 그 경우다.

## 2. RectNodeState에 담은 것 / 뺀 것

[RectNodeState.cs](../Transforms/RectNodeState.cs) 주석이 규범이다. 요약:

- **담음** — `anchoredPosition` · `anchorMin`/`Max` · `pivot` · `sizeDelta` ·
  `localScale` · `localEulerAngles`(3축)
- **뺌** — `localPosition.z`(미사용) · `offsetMin`/`Max`(같은 정보의 다른 표현)

근거는 추측이 아니라 실제 리그 코드 조사다. 조사 대상:
`CharacterRigBuilder`(StretchFull + `NeedsBottomPivot`) · `BackgroundRigBuilder` ·
`OverlayRigBuilder`(고정 앵커 (0.5,0.5) 사용) · `CharRigImageSizingPolicy`(`sizeDelta`·`pivot.x`) ·
`OverlayRigOperations`(`sizeDelta`) · `CharacterPlacementTargetLedger`(위치·스케일·오일러 게시).

## 3. 허용 오차(ε) 정책

### 정책값

| 용도 | ε | 비고 |
|---|---|---|
| **U14 등가성 판정 (위치, px)** | **0.1 px** | 기준 해상도 픽셀 공간, 체인 깊이 ≤ 48 |
| U14 등가성 판정 (무단위 성분) | 1e-4 | 스케일·anchor·pivot |
| 유니티 대조 하네스 (위치, px) | 0.01 px | 얕은 체인(≤ 6단)이라 더 조인다 |
| 순수 수학 테스트 (손 계산 기대값) | 1e-4 | |

각도·alpha의 ε는 U14 비교기를 세울 때 같은 방식으로 유도해 이 표에 더한다.

### 근거 — "잡음의 천장"과 "신호의 바닥" 사이에 놓는다

float32 연산 오차의 상한(잡음의 천장):

```
ε_noise ≤ D × k × W × 2⁻²⁴
  D = 체인 깊이            (캐릭터 리그 실측 ~35단, 여유 48)
  k = 노드당 좌표별 연산 수 (~8)
  W = 좌표 크기 상한        (기준 폭의 2배 = 4000px 가정)
→ 48 × 8 × 4000 × 6.0e-8 ≈ 0.09 px
```

한편 의미 있는 오류(신호의 바닥)는 훨씬 크다: pivot이 틀리면 수백 px,
앵커가 틀리면 수십 px, 1u 환산이 틀리면 최소 수 px.
**1px 미만의 의미 오류는 이 코드 경로에서 만들어질 방법이 없다.**

그래서 ε = 0.1px: 잡음 상한(0.09)의 바로 위, 신호 바닥(≥1px)의 10분의 1.
어느 쪽으로도 오판할 여지가 없는 자리다.

유니티 대조 하네스는 체인이 얕아(D ≤ 6) 잡음 상한이 ~0.01px이므로 0.01px로 조인다.
**공식이 미묘하게 틀렸을 때 더 일찍 잡기 위해서다** — 위 "앵커 수학이 틀리기 쉬운 한 자리"가
그 예다.

### 금지 조항

**불일치가 나면 ε를 늘려서 맞추지 않는다.** 불일치는 발견이고, 고치는 쪽은 코드다.

ε를 바꿔도 되는 유일한 사유는 위 공식의 전제(체인 깊이·좌표 상한)가 실측과 다르다고
판명되는 경우이고, 그때도 **이 문서의 공식을 갱신하고 값을 다시 유도한다.**
숫자만 고치는 변경은 이 문서의 존재 이유를 없앤다.

## 4. 검증 — 두 겹으로 나눈 이유

| 어디 | 무엇을 판정하나 | 실패의 뜻 |
|---|---|---|
| [RectChainMathTests](../Tests/EditMode/RectChainMathTests.cs) | 손 계산 기대값 + 왕복·합성 성질 | **내 산수가 틀렸다** |
| [RectChainMathUnityParityTests](../Tests/EditMode/UnityParity/RectChainMathUnityParityTests.cs) | 실제 RectTransform과 양방향 대조 | **유니티 규약을 잘못 알았다** |

둘을 한 곳에 섞으면 실패했을 때 어느 쪽인지 모른다.

대조 방법: 코어의 "월드"가 rootSpace 로컬이므로 유니티 쪽을
`root.InverseTransformPoint(leaf.TransformPoint(p))`로 같은 공간에 맞춘다.

유니티 대조 케이스는 **실제 리그에 나오는 조합**을 고른다:
스트레치 풀 3단 · 바닥 pivot + 스케일(리그 슬롯 축) · Z 회전 + 비균등 스케일 ·
고정 앵커 + sizeDelta(초상 이미지) · 부분 스트레치(오버레이) · 오일러 3축 순서 ·
전 요소 혼합 5단 · 비중앙 루트 pivot · 루트 이동·회전 무관성.
