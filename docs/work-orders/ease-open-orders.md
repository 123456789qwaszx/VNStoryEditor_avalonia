# 이징을 데이터로 여는 길 — 작업 지시 · 런타임 개통 요청

2026-08-20 · 소유자 의도 · **VnTool 연출 커맨드 편집(`presentation-refresh-orders.md`)의 선행물**

수신: `ked-presentation-runtime`. VnTool이 `move_by`의 수치(delta·duration·ease)를
프리뷰를 보며 실시간으로 조절하는 편집(계획서 W66–W67)을 시작한다. 그 프리뷰가
거짓말하지 않으려면 **곡선 모양의 정본**이 필요하고(W66b 시간 재현), ease를 편집한
결과를 실으려면 **텍스트에 ease가 설 자리**가 필요하다(W67). 둘 다 그쪽 개통이 선행이다.

> 이 문서는 실코드를 확인하고 썼다. 아래 §1의 파일·줄이 그 근거다.
> (별도로 받은 초안 `motionlab-orders.md`는 코드를 모르고 쓰인 문서라
> `ease=OutCubic` 문법이 이미 있다고 전제했다 — **없다.** 이 문서가 그 정정본이다.)

---

## 1. 확인한 현재 모습

| 사실 | 근거 |
|---|---|
| Yarn `move_by`는 **위치 인자 4개**다: `slot x y duration`. ease 토큰은 없다 | `YarnBridge/CommandBridge.cs:107` — `AddCommandHandler<string,string,string,string>` |
| 브리지는 ease를 건드리지 않는다 → 스펙 기본값 **`Ease.OutCubic`** 이 모든 `move_by`의 모양이다 | `CommandBridge.CharRigSetup.cs:99`(`EnqueueSetAnchorOffsetSpecs`) · `MoveByCommandCharR.cs:27` |
| 미는 축은 **`CharSlot_Track`** (x·y), 상대 delta, 단위 u | 같은 함수 — `target = CharacterRigTarget.CharSlot_Track`, `useAbsolutePosition = false` |
| 이웃 커맨드는 ease를 **하드코딩**한다 — nudge 계열 OutCubic, per-frame 이동 Linear | `CommandBridge.CharRigPlacement.cs:74·124` |
| 종점 산수는 이미 코어다: `MoveByReduction.Reduce` — 호스트·리듀서가 같은 값을 본다 | `Ked.Presentation.Core/Reduce/CharPlacementReductions.cs` · `reduction-boundary.md` |
| **코어에 이징이 없다.** `Ease`를 아는 곳은 DOTween(`SetEase`)뿐이다 | `Ked.Presentation.Core`에서 `Ease` 검색 0건 |
| `duration <= 0`이면 트윈 없이 즉시 스냅 | `MoveByCommandSpecCharR.duration` 툴팁 |
| 같은 라인의 커맨드들은 **한 배치로 동시 시작**하고, 같은 타깃의 두 번째 커맨드는 `DOKill(true)`로 첫째를 **완주시키고** 시작한다 | `ClaimTweenCommandBase` 경로 · `MoveByCommandCharR.ClaimTarget` |

마지막 줄이 중요하다 — 그래프가 그릴 시간축의 의미론이 여기서 나온다.
같은 라인·같은 축의 `move_by` 둘은 "구간별 곡선"이 아니라 **계단 + 트윈**이다.
VnTool은 이 의미론을 그대로 그린다(`motion-graph-orders.md` §2).

---

## 2. 부탁 ① — 이징 골든 덤프 (반나절)

`ExportedTuning`(U12)과 같은 패턴의 에디터 메뉴 하나:

- DOTween `EaseManager.Evaluate`를 샘플링해 JSON으로 덤프한다.
  **표준 Ease 전 항목**(`Unset`·`INTERNAL_*`·`Custom` 제외 — 목록은 이 덤프가 정본이다.
  대략 33종) × `t ∈ [0,1]` **257등분**.
- Back·Elastic·Flash 계열이 쓰는 **overshoot·amplitude·period 기본값을 파일에 명시**한다.
- 산출물 `ease-golden.json`은 VnTool 저장소로 보낸다(그쪽 테스트 픽스처가 된다).
- 재덤프 시 바이트 동일(결정적)이어야 한다.

## 3. 부탁 ② — 이징 순수 함수를 코어로

`Ked.Presentation.Core`에 `EaseFunctions.Evaluate(EaseKind, float t) → float`
(이름은 그쪽이 정하는 게 맞다). 표준 Penner 수식 + 위 기본 상수.

- **형태 규약은 `reduction-boundary.md` 그대로** — 순수, UnityEngine 타입 금지,
  시간·전역 상태 없음.
- 등가의 심판은 **그쪽 EditMode 테스트**다: DOTween을 직접 참조할 수 있는 유일한
  자리이므로, `EaseFunctions` ↔ `EaseManager.Evaluate` 전 샘플 오차 `< 1e-4`를
  거기서 고정한다. (VnTool 쪽은 덤프 파일 대조만 한다 — DOTween이 없다.)
- 호스트 어댑터는 손대지 않는다 — `SetEase(_spec.ease)`는 그대로다. 이 함수의
  첫 고객은 VnTool 프리뷰이고, 그쪽 정지 프레임(2b)이 시간 보간으로 갈 때
  두 번째 고객이 된다.
- **사본 동기화**: `Ked.Presentation.Core`는 아직 패키지가 아니라 양쪽 저장소에
  사본으로 산다(정본은 그쪽). 새 파일을 VnTool 사본에도 복사해야 하며, 복사 후
  **양쪽에서 각각 빌드·테스트**한다 — zip 스냅샷으로 건너온 코드가 빌드 검증 없이
  같은 컴파일 오류를 되살린 전력이 있다.

## 4. 부탁 ③ — `move_by`에 다섯째 인자 `ease` 개통

```
<<move_by c1 +2u 0u 12fr OutCubic>>
        slot  x   y  dur  ease(선택)
```

- `AddCommandHandler<string,string,string,string,string>` — 다섯째는 기본값 `""`.
- 파싱은 `Enum.TryParse<Ease>(token, ignoreCase: true)`. **key=value 문법이 아니다** —
  기존 인자 전부가 위치 인자이고, Yarn 핸들러가 위치 기반이다.
- **미지정 = `OutCubic`** — 지금 스펙 기본값 그대로. **기존 대본은 한 글자도 안
  바뀌고 재생 결과도 같다.** 이것이 이 개통의 수용 기준 1번이다.
- 파싱 실패는 **조용히 기본값으로 떨어뜨리지 않는다**(침묵 금지) — 오류 로그를
  남기고 OutCubic으로 재생은 계속한다. 1차 방어는 VnTool 쪽이다: 카탈로그의
  ease 타입이 후보 토큰을 제한하므로 툴이 만든 텍스트는 틀릴 수 없다.
- `scale_by`·`rotate_by`는 **이번에 열지 않는다.** 모션 그래프 v1의 대상이
  `move_by` 하나이고, 같은 패턴이 검증된 뒤 여는 것이 싸다. 한 번에 여는 게
  낫다고 판단하면 그쪽 결정에 따른다 — 다만 기본값 불변 원칙은 같다.

## 5. 부탁 ④ — 채널 사상 확정 회신

VnTool 카탈로그에 "이 커맨드가 무슨 축을 미나"를 **선언**으로 굳힌다(추측 금지 —
이름·정규식으로 축을 추측하는 코드는 이쪽에서 반려 사유다). 아래 표가 맞는지
확인해 달라. 이 회신이 카탈로그 선언의 근거 문서가 된다.

| 커맨드 | 노드 | 축 | 값 의미 | 시간 | ease |
|---|---|---|---|---|---|
| `move_by` | `CharSlot_Track` | x·y | 상대 delta, u | duration (fr/s) | 스펙 `ease`, 기본 OutCubic |
| `move_reset` | `CharSlot_Track` + `CharSlot_Track_Focus` | x·y | 절대 (0,0) | duration | 기본 OutCubic |
| (참고) `place_*` | `CharSlot_Track_Focus` | — | move_by 축과 **별개** | — | — |

## 6. 수용 기준

1. 기존 `.yarn` 코퍼스 전부: 4-인자 `move_by` 파싱·재생 결과 불변 (스모크).
2. `ease-golden.json` — §2의 조건 충족, VnTool에 전달.
3. `EaseFunctions` ↔ DOTween 등가 EditMode 테스트 통과 (`< 1e-4`).
4. 다섯째 인자 재생: `12fr Linear`가 실제로 Linear로 움직인다 (EditMode 또는 육안 1회).
5. §5 표 회신 (수정이 있으면 수정본으로).

## 7. 하지 않는 것

- 커스텀 커브(`ease=@key`) — VnTool 쪽 스냅 정책의 탈출구 후보인데, 표현 불가
  실사례가 쌓이기 전에는 만들지 않는다(그쪽 결정 대기 아님 — **이쪽**이 아직 안 정했다).
- 호스트 트윈 경로 변경 — DOTween은 그대로 시간의 세계를 진다.
- 세이브·타임라인 등 이 개통을 빌미로 한 스코프 확장 — `SCOPE-BOUNDARY.md` §0 그대로.
