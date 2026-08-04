# Phase 2 런타임 지시 순서 (U13-a → U12-전체 → U13-b → U14)

2026-08-05 작성. VnTool 쪽 설계 확정([`work-orders/phase2-design-brief.md`](../work-orders/phase2-design-brief.md) §6,
[`handoff/architecture-decisions.md`](../handoff/architecture-decisions.md) H-2·H-3)과
런타임 조사([`core-extraction-survey.md`](core-extraction-survey.md))에 따른 **실행 순서와 붙여넣을 프롬프트**.

각 블록은 독립 커밋 단위다. **U 단위로 커밋.**

---

## 0. 왜 이 순서인가

```
U12-v1 ─────────────────────────────────────────────┐ (독립, 반나절, 즉시 가치)
U13-a (asmdef 경계) ──────────┐                      │
U12-전체 (스키마·프리셋 덤프) ─┴─> U13-b (코어 추출) ─┴─> U14 (등가성) ──> VnTool 2b 착수
```

- **U13-a가 U13-b의 문이다.** 어셈블리 경계가 없으면 코어를 별도 어셈블리로 뽑을 수 없다.
- **U12-전체가 U13-b보다 앞이다** — 이게 놓치기 쉬운 자리다.
  코어의 `Tuning/`(리그 스키마·포커스/뎁스 프리셋·기준 해상도)은 **U12-전체가 내보내는 것의 스키마**다.
  순서가 뒤집히면 U13-b가 모양을 지어내고 U12-전체가 거기 맞추거나, 둘이 어긋난다.
- **U13-a와 U12-전체는 서로 독립**이라 병행 가능하다.
- **U14가 2b의 초록불이다.** 런타임에도 아직 리듀서가 없었으므로(조사 §2),
  코어가 옳은지를 판정할 수 있는 것은 U14뿐이다.

> **2026-08-05 진행 상황**: U13-a **완료**. `Assets/Ked.Presentation.Core/`가 생겼고
> asmdef가 `noEngineReferences: true` · `references: []`로 "UnityEngine 참조 0"을 강제한다.
> 첫 산출물은 숫자 토큰 파서 셋(`DurationToken`·`UnitToken`·`NumberToken`).
> **U13-b 착수 전에 [`phase2-design-brief.md`](../work-orders/phase2-design-brief.md) §6.7의
> D-core-1(`1u`의 정의)을 답해야 한다** — 51개 커맨드가 상수를 참조하기 시작하면 되돌리기가 비싸진다.

## 1. ~~U13-a — 어셈블리 경계 긋기~~ ✅ 완료

> 소유자 예상: 코드를 신경 써서 만들었으니 순환 참조는 없고 경계만 그으면 된다.
> 그래도 **발견 건수는 보고하게 한다** — 0이면 0이라고 적으면 되고, 0이 아니면 그게 일정 신호다.

```
런타임 저장소 작업 U13-a를 수행해줘. 근거: runtime-work-orders.md의 U13,
그리고 core-extraction-survey.md §4(asmdef 0개).

목표: 나중에 Ked.Presentation.Core를 별도 어셈블리로 뽑아 외부 도구(VnTool)가
참조할 수 있도록, 지금 Assembly-CSharp 하나에 들어 있는 코드에 어셈블리 경계를 긋는다.
이번 작업은 경계만 긋는다 — 코드 로직은 한 줄도 바꾸지 않는다.

작업:
1. asmdef를 도입한다. 최소한 아래가 서로 다른 어셈블리로 갈리면 된다:
   - 미래 코어 후보: PresentationCore/, Commands/, CharacterRig/, ShotResponse/, BackgroundRig/
   - 씬·에디터 글루, Yarn 브리지, 그 밖의 게임 코드
   경계를 어디에 몇 개 그을지는 실제 참조 관계를 보고 정하되, 판단 근거를 남길 것.
2. 순환 참조가 나오면 해소한다. 해소 방법은 인터페이스 추출·이동 등 구조 변경이며,
   그 경우에도 동작은 불변이어야 한다.
3. Editor 전용 코드는 Editor 어셈블리로 분리한다(플랫폼 빌드에서 빠지도록).

보고할 것 (수용 기준의 일부다):
- 그은 경계 목록과 각각의 근거
- **순환 참조 발견 건수와 내용. 0건이면 0건이라고 명시할 것.**
- 해소를 위해 옮기거나 바꾼 파일이 있으면 그 목록과 이유

수용 기준:
- Unity 컴파일 통과, 기존 재생 동작 불변(대표 에피소드 스모크 1회).
- **로직 변경 0**: diff가 asmdef 추가 / 파일 이동 / 순환 해소를 위한 최소 구조 변경
  이외를 포함하지 않는다. 기능 개선·리팩터링을 섞지 말 것.

커밋은 U13-a 단위로.
```

## 2. U12-전체 — 데이터 덤프 (U13-a와 병행 가능)

```
런타임 저장소 작업 U12-전체를 수행해줘. 근거: runtime-work-orders.md의 U12,
runtime-contract.md §104(좌표가 전부 RigSpaceRoot 픽셀 기준이라 기준 해상도가 데이터로 필요).

목표: VnTool의 Phase 2a(에셋·리그 데이터 수입)가 읽을 수 있도록, 지금 코드와 SO에
흩어져 있는 연출 기준값을 JSON으로 내보낸다. 이 JSON의 스키마가 곧 나중에 만들
Ked.Presentation.Core의 Tuning 타입 모양이 되므로, 스키마를 문서로 함께 남긴다.

내보낼 것:
1. DBSO 프리셋 일습 — visual focus / mask motion / screen effect / depth /
   focus tuning / role anchor / surface layout
2. 리그 스키마 4종 테이블
3. CanvasScaler 기준 해상도 (VnTool이 좌표를 이 해상도 기준으로 해석한다)
4. BGM 문자열 키 보존 — BgmPlayer에 현재 키를 저장(U15 상태 스냅샷의 전제)

작업 원칙:
- 값은 지금 값 그대로 내보낸다. 이번에 튜닝하지 말 것.
- 규약 경로로 표현 못 하거나 내보낼 수 없는 항목은 건너뛰되 경고 목록으로 보고한다 —
  조용히 빠뜨리지 말 것.
- 스키마 문서를 함께 낸다: 각 필드의 의미·단위·좌표계 기준. VnTool이 이것만 보고
  읽을 수 있어야 한다.

수용 기준: 덤프 생성 확인, BGM 키 조회 가능, 스키마 문서만으로 필드 해석이 가능하다.

커밋은 U12-전체 단위로.
```

## 3. U13-b — 코어 추출 + 리듀서 표준화 (U13-a·U12-전체 후)

> **착수 전에 둘을 먼저 할 것.**
> ① `ClaimTarget` 표본 5~10개를 읽고 작업량 재추정 — 조사는 개수만 셌고 편차는 확인하지 않았다(조사 §6-3).
> ② **D-core-1 답하기**([`phase2-design-brief.md`](../work-orders/phase2-design-brief.md) §6.7):
> `UnitToken.UnitPixels`가 `const 40px`인데 VnTool은 기준 해상도를 게임별 데이터로 다룬다.
> `1u`가 절대 40px인지, 기준 폭 ÷ 48인지에 따라 이 값이 상수인지 `tuning` 인자인지 갈린다.

```
런타임 저장소 작업 U13-b를 수행해줘. 근거: runtime-work-orders.md의 U13,
core-extraction-survey.md, 그리고 VnTool 쪽 설계 확정
(work-orders/phase2-design-brief.md §6.2, handoff/architecture-decisions.md H-2).

배경: VnTool이 이 코어를 참조해서 같이 쓴다. 커맨드 해석을 두 벌 만들지 않기 위해서다
(규약을 두 번 구현하면 규약이 바뀌는 날 양쪽 다 오류 없이 어긋난다).

목표: Ked.Presentation.Core를 신설하고, 지금 51개 커맨드의 ClaimTarget에 흩어져 있는
"스펙 → 목표 상태" 계산을 표준화해 그리로 올린다.

제약 (어기면 공유가 성립하지 않는다):
1. 타깃 netstandard2.1, 외부 의존성 0, **UnityEngine 참조 0**.
   Vector2/Vector3/Color 대신 코어 자체 값 타입(Vec2/Rgba 등)을 쓴다.
   (U13-a에서 asmdef의 noEngineReferences로 이미 강제됨 — 유지할 것)
1-b. 좌표 산수는 코어에서 끝낸다. 호스트가 받아서 자체 배치 계산을 이어가면
   런타임과 미세하게 갈리고, 그 차이는 U14 ε 비교에 잡히지 않는 자리에서 생긴다.
2. 리듀서는 순수 함수다: Apply(state, command, catalog, tuning) -> state.
   시간·랜덤·IO·전역 상태 없음. 같은 입력은 언제나 같은 상태.
3. 게임별 값(리그 스키마·프리셋·기준 해상도)은 코드가 아니라 tuning 인자로 온다.
   U12-전체가 내보낸 스키마를 그대로 받는다.
4. 인식하지 못한 커맨드는 버리지 말고 Unhandled 목록에 보존한다(커맨드명 + 출처).
5. 좌표는 전부 기준 해상도 픽셀(RigSpaceRoot). 호스트가 뷰포트로 스케일한다.

경계 (조사 §6-2에서 확인된 것):
- CommandBase는 시그니처에 MonoBehaviour(코루틴 호스트)를 물고 있으므로 코어로 옮기지 않는다.
  코어에 가는 것은 커맨드 클래스가 아니라 각 커맨드의 "스펙 → 목표 상태" 변환부다.
- 트윈 실행부와 DOTween 의존(67파일)은 호스트에 남는다. 정지 프레임에 필요한 것은
  ClaimTarget이 산출하는 목표값뿐이다.

StageState 설계:
- ShotResponse/CharacterPlacementTargetLedger가 이미 이것의 원형이다
  ("트윈이 다 끝났다면 어디에 있을 것인가"를 별도 보관 중).
  이것을 승격시키되 두 가지를 바꾼다:
  (가) 키를 RectTransform → 논리 식별자(slotKey/stage/layer)로
  (나) 측정을 "Unity에 잠깐 써넣고 되돌리기" → 순수 변환 수학으로
- 담을 것: 리그·슬롯(좌표·스케일·정렬 순서 포함)·배경·샷·대사창·이펙트 상태·Unhandled.

착수 전에 할 것: ClaimTarget 표본 5~10개를 읽고 편차를 확인한 뒤 작업량을 다시 추정해서
보고할 것. 51곳이 미묘하게 다르면 표준화가 이 작업의 대부분이다.

수용 기준: 코어 단위 테스트(커맨드별 리듀서 골든), Unity 빌드 통과,
기존 재생 동작 불변, 코어 어셈블리에 UnityEngine 참조 0건.

커밋은 U13-b 단위로.
```

## 4. U14 — 등가성 하네스 (2b의 초록불)

```
런타임 저장소 작업 U14를 수행해줘. 근거: runtime-work-orders.md의 U14,
core-extraction-survey.md §3.

배경: 런타임에도 지금까지 "리듀서"라는 물건이 없었다(상태가 RectTransform에 살았다).
그래서 U13-b가 만든 코어가 실제 재생과 같은 상태를 내는지는 아무도 모른다.
이 하네스가 그것을 판정하는 유일한 장치이고, VnTool의 Phase 2b 착수 조건이다.

목표: 같은 라인까지의 (실제 재생 상태) vs (코어 리듀서로 접은 상태)를 자동 비교한다.

작업:
1. StageState CaptureCurrent() 구현 — 라이브 리그·시스템에서 확정 상태를 읽어낸다.
   새로 발명할 필요 없다. 이미 있는 두 패턴을 논리 키 기준으로 모으는 일이다:
   - PresentationResponseCoordinateMapper.CaptureBaseMeasure(target)
     (살아 있는 리그 → Unity 참조 없는 값 구조체)
   - CharacterPlacementTargetLedger.WorldPointToSettledParentLocalPoint
     (예약된 최종값이 적용된 세계를 만들어 측정하고 되돌린다)
2. 대표 에피소드를 라인별로 재생하며 캡처한 상태와, 같은 라인까지 코어 리듀서로
   접은 상태를 비교하는 자동 테스트(PlayMode 또는 배치 모드).
3. 허용 오차 정책을 명시한다 — float 좌표는 ε 비교, ε 값과 근거를 문서에 남길 것.

수용 기준: 대표 에피소드 전 라인 등가. 불일치는 리듀서 수정으로 수렴시킨다
(하네스를 느슨하게 만드는 방향으로 맞추지 말 것 — 불일치는 발견이다).

커밋은 U14 단위로.
```

## 5. 이 지시들이 끝나면

VnTool 쪽이 [`phase2-design-brief.md`](../work-orders/phase2-design-brief.md) §6.5의 이행 순서를 탄다:
코어 참조(git submodule 권장) → 1단계 `MiniStageFold` 교체(**화면·출력 불변 골든**) →
2단계 `StageSceneComposer` 좌표 사용(여기서 처음 화면이 바뀐다) → 3단계 근사·미표시 뱃지.

U12-v1(초상화 덤프)은 이 순서와 독립이고 여전히 유효하다 —
프롬프트는 [`runtime-parallel-orders.md`](runtime-parallel-orders.md) §3에 있다.
