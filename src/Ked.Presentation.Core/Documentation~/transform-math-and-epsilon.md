# RectChainMath — 좌표 규약과 허용 오차(ε) 정책

U13-b-1 산출물. 이 문서의 ε 정책은 **U14 등가성 하네스가 그대로 쓴다.**

## 1. 좌표 규약

- **"월드" = rootSpace 소유자의 로컬 좌표.** 절대 월드가 아니다.
  `CharacterPlacementTargetLedger`의 `stopRoot`(RigSpaceRoot)가 하던 역할과 같다.
  호스트가 리그를 화면 어디에 놓든 코어 계산은 영향을 받지 않는다.
- 체인은 루트에 가까운 쪽부터: `chain[0]`의 부모가 `rootSpace`다.
- 회전 적용 순서는 Unity `Quaternion.Euler`와 동일: **Z → X → Y.**
- 로컬 변환: `p' = t + R(euler) · (scale ⊙ p)`. `t`는 anchoredPosition이 아니라
  앵커 수학(`LocalPosition`)을 거친 pivot의 부모 로컬 좌표다.
- `localPosition.z`는 항상 0으로 취급한다(리그가 쓰지 않는다 — RectNodeState 주석 참조).
- 스케일 0인 노드의 `InverseTransformPoint`는 정의하지 않는다(유니티도 특이 행렬).

## 2. RectNodeState에 담은 것 / 뺀 것

[RectNodeState.cs](../Transforms/RectNodeState.cs) 주석이 규범이다. 요약:
담음 — anchoredPosition · anchorMin/Max · pivot · sizeDelta · localScale · localEulerAngles(3축).
뺌 — localPosition.z(미사용) · offsetMin/Max(같은 정보의 다른 표현).

근거 조사 대상: CharacterRigBuilder(StretchFull + NeedsBottomPivot), BackgroundRigBuilder,
OverlayRigBuilder(고정 앵커 사용), CharRigImageSizingPolicy(sizeDelta·pivot.x),
OverlayRigOperations(sizeDelta), CharacterPlacementTargetLedger(위치·스케일·오일러 게시).

## 3. 허용 오차(ε) 정책

### 정책값

| 용도 | ε | 비고 |
|---|---|---|
| **U14 등가성 판정 (위치, px)** | **0.1 px** | 기준 해상도 픽셀 공간, 체인 깊이 ≤ 48 |
| b-1 유니티 대조 하네스 (위치, px) | 0.01 px | 얕은 체인(≤ 6단)이라 더 조인다 |
| 스케일 등 무단위 성분 | 1e-5 | |

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
앵커가 틀리면 수십 px, 1u 환산이 틀리면 최소 수 px. **1px 미만의 의미 오류는
이 코드 경로에서 만들어질 방법이 없다.**

그래서 ε = 0.1px: 잡음 상한(0.09)의 바로 위, 신호 바닥(≥1px)의 10분의 1.
어느 쪽으로도 오판할 여지가 없는 자리다.

b-1 하네스는 체인이 얕아(D ≤ 6) 잡음 상한이 ~0.01px이므로 0.01px로 조인다.
공식이 미묘하게 틀렸을 때(예: 앵커 보간에 pivot 대신 0.5를 쓰는 류) 더 일찍 잡기 위해서다.

### 금지 조항

**불일치가 나면 ε를 늘려서 맞추지 않는다.** 불일치는 발견이고, 고치는 쪽은 리듀서다
(phase2-runtime-orders.md §4 U14 수용 기준). ε를 바꿔야 할 유일한 사유는
위 공식의 전제(깊이·좌표 상한)가 실측과 다르다고 판명되는 경우이고, 그때도
이 문서의 공식을 갱신하고 값을 다시 유도한다.

## 4. 검증

- 순수 테스트: `Tests/EditMode/RectChainMathTests.cs` — 손으로 계산한 기대값 + 왕복 성질.
- 유니티 대조: `Tests/EditMode/UnityParity/RectChainMathUnityParityTests.cs` —
  실제 RectTransform 계층을 조립해 `TransformPoint`/`InverseTransformPoint`와 비교.
  중첩 2~5단, 스트레치/고정 앵커 · 바닥 pivot · 비균등 스케일 · 3축 회전 조합.
