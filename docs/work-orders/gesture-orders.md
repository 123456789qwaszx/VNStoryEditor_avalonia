# 몸짓 커맨드 `gesture` — 작업 지시 · 런타임 개통 요청

2026-08-21 · 소유자 의도 · 수신: `ked-presentation-runtime`

> **소유자 (2026-08-21)** — "move_by 커맨드에서 이징을 조절하면 사실 쉐이크라던지, 총총
> 뛰는 느낌이라던지 그런 걸 임시로 만들 수 있는데, 현재 구조에서는 x와 y에 값을 조금이라도
> 주어야지 ease값에 영향이 가는게 아쉬워. **가로세로의 최종값에는 영향없이 ease를 활용한
> 애니메이션연출**을 할 방법이 있을까? **또 세로와 가로 ease를 구분하고.**" → 설계 승인:
> "거의 move_by와 동일하게 쓸 수 있지만, shake로 양옆으로 흔들 수도 있고, 세로로 hop
> 느낌으로 ease 커브를 잡은 뒤 가로로는 이동시킨다던지, 자유롭게."
>
> 이 문서는 실코드를 확인하고 썼다. §1의 파일·줄이 그 근거다.
>
> **이름 확정 (소유자)**: `gesture` — shake·sway·jolt는 용도 하나씩만 말해서 좁다.
> 이 커맨드는 hop·jolt·surprise까지 품는 **제자리 몸짓 전반**이라 "오히려 모호한 게
> 맞다". W65에서 걷어낸 몸짓 프리셋 13종(스파인 이관)의 빈자리를, 프리셋이 아니라
> 작가가 곡선으로 직접 그리는 몸짓 하나로 채운다.

---

## 0. 왜 move_by 위에서는 안 되는가 (설계 근거)

변위(t) = **delta × 곡선(t)** 이다. 이징은 진행(0→1)의 모양만 정하므로 delta가 0이면 어떤
곡선도 × 0 = 0 — 제자리 요동이 원리적으로 불가능하다. 이걸 "끝값 0인 곡선 허용"으로 풀면
**리듀서가 곡선 내용을 알아야 종점을 접게 되어** "이징은 종점에 관여하지 않는다"는 불변식이
깨진다(2026-08-21 곡선 끝점 (0,0)·(1,1) 고정이 바로 그 불변식의 방어였다).

그래서 **종점 불변을 커맨드 이름에 싣는다**: `gesture`는 정의상 순변위 0이고, 리듀서는 내용을
안 보고 무변으로 접는다. 불변식이 유지된다.

## 1. 확인한 현재 모습 (실코드 근거)

| 사실 | 근거 |
|---|---|
| 리그에 `CharacterPortrait_Shake` 노드가 **이미 서 있다** — `SwayPivot`의 자식, `ActingScale`의 부모 | `CharacterRigDefinition.cs:76-77` (스키마), `:113`(Refs), `:162`(필드) |
| `CharacterRigTarget.CharacterPortrait_Shake`로 **해석까지 된다** | `CharacterRigDefinition.cs:216`, `CharacterRigBuilder.cs:224` |
| 그런데 **어떤 커맨드도 이 노드를 안 쓴다** | `YarnBridge/`·`Commands/`에서 `CharacterRigTarget.CharacterPortrait_Shake` 검색 0건 |
| `show`가 이 노드의 위치·회전·배율을 **이미 리셋 목록에 넣고 있다** — 등장 초기화는 기존 계약 | `StageReducer.Show.cs` `ShowResetPositionIds`·`ShowResetEulerIds`·`ShowResetScaleIds` |
| 같은 타깃의 둘째 커맨드는 `DOKill(true)`로 첫째를 완주시키고 시작 — **다른 타깃끼리는 충돌 없음** | `MoveByCommandCharR.ClaimTarget` (`_rect.DOKill(true)`) |
| `@이름` 커스텀 곡선 해석은 브리지 `ResolveEase` 한 자리 | `CommandBridge.CharRigSetup.cs:127-144` (`EaseCurveLibrary`, 없으면 경고 + 폴백) |
| 곡선 평가의 정본은 코어 `CurveFunctions.Evaluate(keys, t)` — 양쪽 등가 고정 | `Ked.Presentation.Core/Ease/CurveFunctions.cs:42` |

`CharacterPortrait_Shake`가 표적인 이유: `move_by`(CharSlot_Track)·`place`(Track_Focus)·
넛지(Track_X/Y)와 **다른 노드**라 DOKill 충돌 없이 겹친다. "총총 뛰며 이동" =
`move_by`(가로, 제 이징) + `gesture`(세로 진동)를 **같은 라인에 나란히** 쓰면 된다.

## 2. 커맨드 계약

```
<<gesture slot xAmp yAmp duration xEase yEase>>
<<gesture c1 0.3u 0u 12fr>>                     ; 양옆 흔들기(기본 혹 곡선)
<<gesture c1 0u 1.2u 24fr  "" @hop>>            ; 세로 총총 — move_by와 같은 라인에 겹친다
<<gesture c1 0.5u 0.5u 18fr @zigzag @hop>>      ; 축마다 다른 곡선
```

| 인자 | 타입 | 기본 | 뜻 |
|---|---|---|---|
| slot | roleKey | 필수 | 대상 슬롯/별칭 |
| xAmp | signedUnit | `0u` | 가로 **진폭**(최대 변위). 부호 = 곡선 좌우 반전 |
| yAmp | signedUnit | `0u` | 세로 진폭. 부호 = 상하 반전 |
| duration | duration | `12fr` | 0 이하면 아무것도 안 한다(순변위 0이라 스냅할 것도 없다) |
| xEase | ease | `""` | 가로 진동 곡선 — 아래 §3 |
| yEase | ease | `""` | 세로 진동 곡선 |

- **변위(t) = (xAmp × xCurve(t), yAmp × yCurve(t))** 픽셀(u 환산은 `YarnUnitParser` 그대로).
  `CharacterPortrait_Shake.anchoredPosition`에 싣는다 — 다른 커맨드가 안 쓰는 노드라
  기준은 늘 (0,0)이다.
- **트윈 하나**: `DOTween.To(0→1, duration)` **Linear**로 t를 흘리고, 콜백에서 두 곡선을
  각각 평가해 x·y를 함께 넣는다 — 축별 이징을 트윈 두 개 없이 얻는다(shot의
  `Interpolate` 콜백 패턴 그대로).
- **완료·스킵 커밋 = (0,0)** — 곡선 끝이 0이라 자연히 제자리다. 랩드스킵(스텝 경계
  가속)도 같은 커밋이면 충분하다 — 어차피 도착이 시작 자리라, shot처럼 가속 트윈을
  다시 만들 필요가 없다.
- 같은 타깃 재청구는 기존 규약 그대로 `DOKill(true)` — 앞 gesture를 (0,0)으로 완주시키고
  시작한다.

## 3. 진동 곡선 — 기존 곡선과 무엇이 다른가

- **시작 (0,0) · 끝 (1,0) 고정**, 중간 자유(음수 = 반대 방향). 값은 진폭에 대한 비율이라
  [-1,1] 권장이지만 강제하지 않는다 — 진폭이 스칼라일 뿐이다.
- `@이름`이면 `curves.json`의 곡선(해석은 기존 `ResolveEase` 자리 — 단 gesture에서는
  DOTween `Ease`로 폴백하지 않는다, 아래).
- **빈 토큰 = 내장 기본 혹**: `bump(t) = sin(π·t)` (0→1→0 한 혹). 코어에
  `OscillationFunctions.Bump` 한 함수로 두면 툴 프리뷰가 같은 모양을 그린다 —
  `EaseFunctions`·`CurveFunctions`와 같은 "정본은 코어" 규칙.
- **표준 이징 이름(OutCubic 등)은 gesture에서 무효**다 — 끝값이 1이라 제자리로 안 돌아온다.
  적혀 오면 경고 로그 + 기본 혹으로 굴러간다(침묵 금지 — `ResolveEase`의 미정의 곡선
  경고와 같은 결).
- 곡선의 "진동 종류" 표시는 **VnTool 프로젝트의 저작 데이터**다. `curves.json` 스키마는
  안 바뀐다 — 런타임은 받은 키를 평가만 하면 되고, 끝값 검증은 툴 내보내기가 막는다.
  방어로: 로드 시 끝값 |v| > 1e-3이면 경고 하나(그래도 재생 — 커밋이 (0,0)이라 안전).

## 4. 코어 폴드 — no-op이 정답이다

```csharp
case "gesture": return true;   // 순변위 0 — 정지 프레임(라인 시작 = 끝)이 이미 옳다
```

- 상태를 안 바꾸고 `Unhandled`에도 안 싣는다. 슬롯 존재 검사(`TryGetSpawnedSlot`)는
  해서 없는 슬롯이면 지금 규약대로 사유를 남긴다.
- ⚠ 폴드가 곡선·진폭을 읽지 않으므로 **인자 자리에 의존하는 것이 없다** — 그래도
  shot·scale 때처럼 `gesture의_인자는_폴드에_무해하다` 테스트로 못 박아 달라(이징 토큰
  유무·`@없는곡선` 포함).

## 5. 검증 목록 (런타임 EditMode)

- [ ] 폴드: `gesture`가 상태를 안 바꾸고 Unhandled 0 · 없는 슬롯은 사유
- [ ] 재생: 중간 프레임에서 `Shake.anchoredPosition ≠ (0,0)`, 완료·스킵 후 = (0,0)
- [ ] 축 분리: xEase만 준 경우 y는 내내 0 (그 반대도)
- [ ] 같은 라인 `move_by` + `gesture` 동시: Track은 이동하고 Shake는 진동 — 서로 DOKill 안 함
- [ ] 표준 이징 이름·미정의 `@이름` → 경고 + 기본 혹
- [ ] `bump` 샘플 값이 코어 함수와 일치(툴과 등가의 씨앗)

## 6. 개통 후 VnTool이 할 일 (참고 — 이쪽 몫)

카탈로그 항목(진폭 슬라이더 `-6~6` × 2 · duration · easeX/easeY) · `StageMotionPlan`에
진동 갈래(샷 갈래와 같은 자리 — 폴드 no-op이어도 시간 재생·스크럽이 amp×curve(t)를
Shake 노드에 흘린다) · 곡선 에디터 **진동 모드**(끝점 (0,0)·(1,0) 고정 — 이동 모드와
잠그는 값만 다르다) · 내보내기 검증(gesture가 참조하는 `@이름`은 진동 종류만 허용).
