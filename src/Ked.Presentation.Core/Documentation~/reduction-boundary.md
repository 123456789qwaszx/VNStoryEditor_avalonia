# 리덕션 경계 — "스펙 → 목표 상태" 규약

코드베이스 **57곳**에 흩어져 있던 `ClaimTarget` 관습을 규약으로 승격한 것.
이후 커맨드 이관이 전부 이 문서의 모양을 따른다.

본보기: [`Reduce/CharPlacementReductions.cs`](../Reduce/CharPlacementReductions.cs) (MoveBy · ScaleTo)
↔ 호스트 `MoveByCommandCharR` · `ScaleToCommandCharR`.

## 코어로 가는 것 / 호스트에 남는 것

| | 어디 | 왜 |
|---|---|---|
| 스펙 → 목표 상태 변환 (`XxxReduction.Reduce`) | **코어** | 정지 프레임이 필요한 전부. 순수 산수다 |
| 출력 타입 (`StageNodeClaim`) | **코어** | 게시·트윈 종점·폴드가 같은 값을 봐야 한다 |
| `CommandBase` · 코루틴 · `WaitForCompletion` | 호스트 | MonoBehaviour 호스트를 문다 |
| `ResolveRefs` (slotKey → RectTransform) | 호스트 | 유니티 참조 해석이다 |
| `DOKill` · 트윈 생성/가속 (`OnStepLifetimeFinished`) | 호스트 | 시간의 세계다 |
| Commit의 트랜스폼 쓰기 · Ledger `Clear` | 호스트 | 유니티에 쓰는 일이다 |
| `HasClaimedTarget` 수명 관리 | 호스트 | 실행 수명의 일부다 |

## 리덕션의 형태

```csharp
public static class XxxReduction            // 커맨드당 하나, Reduce/ 폴더
{
    public readonly struct Args { ... }     // 파싱된 스펙 값만. UnityEngine 타입 금지
    public static StageNodeClaim Reduce(    // 순수 함수
        string nodeKey,                     // 논리 노드 키
        in Args args,
        /* 필요한 현재 상태를 명시적 인자로 */ ...);
}
```

- **순수**: 시간·랜덤·IO·전역 상태 없음. 트랜스폼을 **읽지도** 않는다 —
  현재 상태가 필요하면 값으로 받는다. 같은 입력은 언제나 같은 출력.
- **출력은 `StageNodeClaim`.** 복수 노드를 겨누면 배열이다(SetDepth처럼 위치+스케일 두 장).
  스케일 클레임의 z 보존 규약은 `ApplyTo`가 안다.

## 호스트 어댑터의 형태 (본보기에서)

```csharp
private void ClaimTarget()
{
    _rect.DOKill(true);                                    // 호스트: 시간의 세계
    _startPos = _rect.anchoredPosition;                    // 호스트: 현재 상태 읽기

    StageNodeClaim claim = MoveByReduction.Reduce(         // ── 코어 ──
        _rect.name,
        new MoveByReduction.Args(_spec.useAbsolutePosition, _spec.delta.ToCore()),
        _startPos.ToCore());

    _destPos = claim.Value.XY.ToUnity();                   // 호스트: 트윈 종점
    _rigRefs.PlacementTargets.PublishAnchoredPosition(_rect, _destPos); // 호스트: 게시

    HasClaimedTarget = true;
}
```

**동작 불변이 규약이다**: 어댑터는 계산의 **자리만** 바꾸고 값·순서·부작용을 바꾸지 않는다.
그래서 리덕션 테스트의 기대값은 **종전 식**에서 온다 — 새 기대값을 만들면 불변을 주장할 수 없다.

호스트에서 노드의 정체는 `RectTransform` 참조이고, 클레임의 `NodeKey`는 진단용 이름이다.
장부(`CharacterPlacementTargetLedger`)가 참조 ↔ 키 대응을 안다.

유니티 ↔ 코어 벡터 변환은 `PresentationCoreConversions`의 `ToCore()` / `ToUnity()` 한 곳에 둔다.

## 클레임이 흐르는 세 갈래

```
              StageNodeClaim
                    │
   ┌────────────────┼────────────────┐
   ↓                ↓                ↓
① 장부 게시      ② 트윈 종점      ③ 상태 폴드
Ledger.Publish   DOTween 종점     claim.ApplyTo(tree)
(정착 예약)      (재생)           (정지 프레임)
```

**셋이 같은 값을 보므로 "재생 결과 = 정착 예약 = 정지 프레임"이 한 곳에서 갈라진다.**
하나라도 빠뜨리면 나중에 원인 모를 불일치가 난다.
(`ReductionTests.세_갈래가_같은_값을_본다`가 이 성질을 고정한다.)

## 표본 정독 보고 — 편차 축

착수 전 표본 8곳 정독(placement / setup / acting / color / overlay). 결과:

> ⚠ **이 절은 조사 시점의 기록이다.** 아래에 근거로 든 커맨드 중
> 절차적 연기 계열(`Tremble` · `Sway` · `Breath` · `Walk` · `Hop` · `Bounce`)과
> `ColorTo` · 오버레이/이모지 리그는 **이후 삭제됐다**(연기는 Spine으로 주인이 바뀌었다 —
> [SCOPE-BOUNDARY.md](../../../SCOPE-BOUNDARY.md) 참조).
> 이름을 남겨 둔 이유는 **경계가 왜 여기 그어졌는지의 근거**이기 때문이다.
> 코드에서 찾지 말 것.

### 축 ① — dest 출처

| 유형 | 예 | 이관 |
|---|---|---|
| 순수 (스펙 + 현재 값) | MoveBy · ScaleTo · OverlaySize · ColorTo | 그대로 가능 |
| 튜닝 값 필요 | SetDepth (depth 프리셋) | 파싱된 값을 `Args`에 담아 받는다 |
| **현재 정착 상태 의존** | SetDepth(focus 보존) · PlaceCharacterFocus | solver 순수화가 선행 |
| **절차적 (시간·랜덤)** | Tremble · Sway · Breath · Walk · Hop · Bounce | **불가** (아래) |

### 축 ② — 상태 종류

트랜스폼(anchoredPosition / localScale / localEuler) · alpha(Fade) · color(ColorTo) ·
sizeDelta(Overlay) · 마스크(StageMaskMotion).
`StageNodeClaim`은 트랜스폼 셋으로 시작하고, 필요한 종류를 그때 더한다.

### 축 ③ — 게시 여부

57곳 중 **4곳만** 장부에 게시한다: MoveBy · ScaleTo · PlaceCharacterFocus · SetDepth.
나머지는 트윈 종점으로만 쓴다. 이건 편차가 아니라 **의미 차이**다 —
"이 노드가 정착 후 어디 있을지 남이 알아야 하는가"의 문제다.

## 두 가지 주의 (표본 정독에서 드러난 것)

**1. 이관 단위는 `ClaimTarget` 메서드가 아니라 "dest를 계산하는 자리"다.**
`ScaleToCommandCharR.ClaimTarget`은 `DOKill`만 하고, 계산은 `CaptureTweenEndpoints` →
`ResolveTargetScale`에, 게시는 `PublishSettledTarget`에 따로 있다.
메서드 이름으로 찾으면 놓친다.

**2. 절차적 커맨드는 이관 대상이 아니다.**
`Tremble`은 `_basePos`와 `Random.Range` 시드만 잡고 **목표값 자체가 없다** —
시간 함수로 계속 흔들 뿐이다. 정지 프레임에서 이런 커맨드는 목표가 정의되지 않으므로,
리듀서에서 `Unhandled`로 남는 것이 맞다. 이관 개수 추정에서 뺀다.

## 사장 코드

`PlaceToCommandCharR`은 게시·클리어가 통째로 주석 처리돼 있다. 이관 대상이 아니다.

## 이관 커버리지 — 무엇이 끝났고 무엇이 남았나

yarn 4묶음 기준. "이관 안 함"은 **조용히 버린 것이 아니라 판정한 것**이다.

### placement (char_rig_placement)

| 커맨드 | 처리 |
|---|---|
| `move_by` · `move_reset` | `MoveByReduction` |
| `scale_by` · `scale_reset` | `ScaleToReduction` |
| `set_anchor` / `show` | `SetAnchorReduction` |
| `fade_in` / `fade_out` | `FadeInReduction` / `FadeOutReduction` (가시성 축) |
| `place_focus` | `SettledFocusMath.SolveFocusPlacement` |
| `set_portrait_sprite` | `PortraitSizingReduction` — 폭만 접는다(아래 참조) |

### 초상 (portrait)

접는 것은 **사이징 한 축**이다: 어느 스프라이트인가가 아니라 그 스프라이트가 만드는
`CharacterPortraitSprite_Image.sizeDelta`. 종횡비는 `portrait-dimensions.json`에서 오고,
조회 규약(캐릭터 소문자 · 변형은 문자열 전체 · 표정 2자리 · (캐릭터,"a","01") 폴백)은
런타임 `PortraitResolver`를 그대로 옮긴 것이다.

| 커맨드 | 변형 | 표정 | 사이징 |
|---|---|---|---|
| `cast (slot, char, var, emo)` | 갱신 | 인자 | ✅ |
| `show (target, faceToken)` | 유지 | 인자 | ✅ |
| `face` · `face_swap (target, emo)` | 유지 | 인자 | ✅ |
| `pose (target, variant)` | 갱신 | — | ❌ |

**표정은 상태가 아니다** — 런타임 `CastBinding`도 변형만 들고, 표정은 커맨드마다 인자로 온다.
**`pose`가 사이징을 다시 접지 않는 것도 실동작이다** — `SetPortraitPoseCommandCharR`의
스프라이트 교체가 비활성이라, 폭은 다음 `show`/`face`/`face_swap`에서야 바뀐다.

알려진 한계 하나: `show`의 빈 faceToken은 런타임에서 표정 `"2"`지만
(생략된 인자의 기본값 `"e1"`과 다른 규칙), **원문 추출기가 빈 토큰을 버리므로**
폴드는 생략과 구분하지 못하고 둘 다 `"e1"`로 접는다. 실제 원문에 빈 토큰이 나오면
그 라인만 어긋난다 — 나오면 추출기의 토큰 규칙부터 고칠 일이다.

### depth

`SetDepth` · `PlaceCharacterFocus`의 계산부가 `SettledFocusMath`로 갔다.
이 묶음의 실작업은 solver 순수화 하나였고, `TryMeasureFocusWithTemporaryDepthTransform`
("적용→측정→복원")도 함께 사라졌다.

### shot

`shot_zoom` · `shot_track` · `shot_to` · `shot_reset` · `shot_focus_to` 5종 전부.
`ShotIntentMath`가 zoom→배율 규약(0.05)의 유일한 자리다 — 커맨드와 적용측이 같이 본다.

### staging (12커맨드)

| 커맨드 | 스펙 클래스 | 처리 |
|---|---|---|
| `move_by` · `move_reset` | `MoveByCommandSpecCharR` | placement 리덕션이 이미 덮는다 |
| `scale_by` · `scale_reset` | `ScaleToCommandSpecCharR` | 동일 |
| `rotate_by` · `rotate_reset` | `RotateToCommandSpecCharR` | `RotateToReduction` |
| `sibling_front` · `sibling_back` | — | **이관 안 함** (아래) |
| `char_to` · `char_to_s0/s1/s2` | — | **이관 안 함** (아래) |

**구조 커맨드 6종을 이관하지 않는 이유: 변환부가 항등이다.**
스펙 값(stage·layer·순서 모드)이 곧 목표이고 본문은 유니티 구조 조작(`SetParent` ·
`SetSiblingIndex`)뿐이라 **코어로 옮길 계산이 없다.**
이 구조 축(리그가 어느 스테이지/레이어에 어떤 순서로 붙는가)은 무대 상태 조립 때
데이터 모델로 들어간다.

### 이관 대상이 아닌 부류 — 절차적 커맨드

`Tremble` · `Sway` · `Breath` · `Walk` · `Hop` · `Bounce` 등은 목표값 자체가 없다.
`_basePos`와 랜덤 시드를 잡고 시간 함수로 계속 흔들 뿐이다.
정지 프레임에서 이들의 목표는 **정의되지 않으므로** 리듀서에서 `Unhandled`로 남는 것이 맞다.

**이 판정은 등가성 하네스의 실재생으로 확인됐다.** 라인 단위(HurryUpLine) 재생에서
절차적 축이 그대로 드러났다:

```
CharacterPortrait_SwayPivot.localEulerAngles  접힘=(0,0,0) vs 캡처=(0,0,7.53) / (0,0,359.62) …
CharSlot_Track_Idle.anchoredPosition          접힘=(0,0)   vs 캡처=(0, 7.99) …
```

값이 매 라인 제각각인 것이 특징이다 — 목표값이 아니라 **시간 함수의 스냅샷**이라서.
랩드스킵 재생에서는 즉시 확정되어 누적될 시간이 없으므로 이 축이 보이지 않는다.
그래서 폴드 대조의 기준 프로토콜은 랩드스킵이다(하네스 파일 상단 주석 참조).

### 남은 Unhandled의 회계 (대표 에피소드 실측)

수렴이 끝난 시점의 리포트 `finalUnhandled` 153건을 **하나도 남김없이** 분류한 것이다.
"아직 못 접는 것"의 목록은 작업 목록이어야 한다 — 작업이 아닌 것이 섞이면 거짓말이 된다.

| 부류 | 건수 | 판정 |
|---|---|---|
| 시간 커맨드 (`pause` · `1fr`~`48fr`) | 19 | **접을 것이 없다** → 무해하게 접도록 고쳤다 |
| 절차적 연기 (`tap`·`sway*`·`jolt`·`dip`·`idle_*`) | 38 | **이관 대상 아님** — 목표값이 정의되지 않는다 |
| 화면 효과 (`screen_blur/vignette/noise/flash`) | 36 | 무대 상태 모델 밖 (포스트 이펙트 축) |
| 배경 리그 (`bg_*`) | 19 | 무대 상태 모델 밖 (`StageState`는 캐릭터 리그만 담는다) |
| 캐릭터 비주얼 (`char_visual_*`) | 14 | 무대 상태 모델 밖 (머티리얼 축) |
| 오디오 (`bgm` · `stop_bgm` · `sfx`) | 14 | 무대 상태 모델 밖 |
| 트랜지션 (`tx_*`) | 10 | 무대 상태 모델 밖 |
| 캐릭터 리그 트랜스폼 (`slide_in`) | 2 | **이관 완료 (2026-08-24)** — 아래 참조 |
| 대사창 (`surface_reset`) | 1 | 무대 상태 모델 밖 |

즉 **캐릭터 리그의 좌표·가시성 축은 이제 전부 접힌다** — 마지막까지 남아 있던 `slide_in`을
2026-08-24에 이관했다(소유자 보고: "SlideOut, SlideIn이 지금 동작하지 않는데").

**둘은 대칭이 아니다.** `SlideCommandBase`가 등장·퇴장의 차이를 "현재 위치가 도착점이냐
출발점이냐" 하나로 환원하기 때문이다:

| | 정착 상태 | 표적 | 기본 방향·거리 |
|---|---|---|---|
| `slide_in` | **항등** — 도착점이 클레임 시점의 현재 위치이고, 화면 밖 출발점과 punch 오버슈트는 트윈 중에만 존재한다 | `CharSlot_Track` | left · 12u |
| `slide_out` | **방향 × 거리** — 나간 자리에 남는다(가시성은 안 건드린다) | `CharSlot_Track` | right · 12u |

그래서 `slide_in`의 폴드는 "**위치를 바꾸지 않는다**"를 명시하는 것이고 판정에는 영향이
없다 — 값이 아니라 `Unhandled`가 줄어든다(고칠 것이 없는 항목은 작업 목록의 소음이다).
반대로 `slide_out`은 **접히지 않는 동안 정착 상태가 실제로 틀렸다**: 나갔는데 폴드는
제자리를 말했다. 거리·방향 파싱은 브리지가 지나는 것과 같은 함수를 쓴다
(`UnitToken.TryParsePixels` — 음수는 거부가 아니라 0으로 클램프).

⚠ 트윈 중의 모양(등장이 들어오는 궤적, punch)은 폴드의 것이 <b>아니다</b> — 시간 축이고,
정지 프레임을 판정하는 이 문서의 범위 밖이다. 다만 **그 축도 값은 여기서 온다**: 방향 낱말
표·스펙 기본값(방향·거리·ease·punch)·튐 모양을 `SlideMotion` 하나가 갖고, 리듀서와 툴의
시간 계획이 같은 것을 본다(사본 금지). 등장의 화면 밖 출발점은 그 값으로 **합성**한다 —
`현재 자리 + 방향 × 거리`. 순변위가 0인 채로 궤적이 서는 이유다.

### 백로그

- 구조 축 상태 모델 (stage/layer 부착 + 형제 순서)
- `SetPortraitSprite` (초상 축)
- `ScreenFocusPoint` 비율표 — 게임별 값이므로 코어 코드가 아니라 **tuning 데이터**가 맞다.
  지금은 호스트 `ScreenFocusPointResolver`에 있다. U12 덤프 추가 후보.
- `RotateByCommandCharR` 등 SO 저작 전용 스펙 — yarn 4묶음 밖. 필요하면 같은 규약으로.
- **커맨드 수명 통합** — **완료** (`PresentationCore/Base/ClaimTweenCommandBase.cs`).
  `ShotIntentCommandBase`가 shot 계열에 했던 통합의 일반화. 하네스가 오라클이므로
  **이관 배치마다 랩드스킵 재판정으로 리포트가 리팩터 전과 같은지 확인했다.**
  - 배치 1: MoveBy · ScaleTo · RotateTo (코어 배선이 끝난 셋)
  - 배치 2·3: Fade 4종(`CanvasFadeCommandBase`) · BgR 트랜스폼 계열 · RotateBy 쌍
  - 배치 4: PivotRotateTo · Slide 4종(`SlideCommandBase`) ·
    Overlay Move/Scale/Size · Overlay Show/Hide(`OverlayRootFadeCommandBase`) ·
    SetDepth(클레임 2장 + 시퀀스) · PlaceCharacterFocus(클레임 1장 + 게시) ·
    MirrorCharacter · CharVisualFocus · StageDepthDefocus

  기반이 제공하는 변형점: `AccelerateOnStepFinish`(가속 여부) ·
  `CreateAcceleratedTween`(가속 트윈이 본 트윈과 다른 경우) ·
  `StepFinishSpeedUpMultiplier`(가속 배율) · `ResolvePlaybackDuration`(배속 축약).
  가속 잔여시간은 `MeasureRemainingRatio` 한 장으로 받는다 — 축이 여럿이면
  축별 `RemainingRatio`의 최댓값이다(가장 늦게 도착하는 축이 기준).

  - 대상 아님: 절차적 연기 —
    목표값이 정의되지 않는다. 즉시 확정(`SetAnchor`)은 트윈이 없어 모양이 다르다.
  - 기반이 의도적으로 통일한 미세 차이 (실경로 무영향):
    ① 해석 실패 시 NRE 대신 경고+건너뛰기
    ② 가속 잔여시간이 0으로 퇴화하는 경계에서 0초 트윈 대신 즉시 확정
    ③ 가속 잔여시간에 0.01초 하한 — 다수파 규칙으로 통일했다.
       종전에 하한이 없던 것은 `SetDepth` 하나뿐이다.
    ④ `PlaceCharacterFocus`는 출발=목표일 때 잔여비율이 NaN이 되어
       NaN 길이 트윈을 만들고 있었다. 기반의 0 가드가 이를 없앤다.
