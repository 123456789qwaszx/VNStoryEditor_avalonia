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

- **U13-a가 U13-b의 문이다.** 어셈블리 경계가 없으면 코어를 별도 어셈블리로 뽑을 수 없다. (✅ 완료)
- **U13-b는 여섯 단계다**(§3). "커맨드 51개 옮기기"가 아니라 **옮길 바닥부터 놓는 일**이고,
  바닥은 트랜스폼 수학(b-1) → 노드 트리(b-2) → 정착 계산(b-3) 순이다.
- **U12-전체가 U13-b보다 앞이다** — 이게 놓치기 쉬운 자리다.
  코어의 `Tuning/`(리그 스키마·포커스/뎁스 프리셋·기준 해상도)은 **U12-전체가 내보내는 것의 스키마**다.
  순서가 뒤집히면 U13-b가 모양을 지어내고 U12-전체가 거기 맞추거나, 둘이 어긋난다.
- **U13-a와 U12-전체는 서로 독립**이라 병행 가능하다.
- **U14가 2b의 초록불이다.** 런타임에도 아직 리듀서가 없었으므로(조사 §2),
  코어가 옳은지를 판정할 수 있는 것은 U14뿐이다.

> **2026-08-05 진행 상황**: U13-a **완료**. `Assets/Ked.Presentation.Core/`가 생겼고
> asmdef가 `noEngineReferences: true` · `references: []`로 "UnityEngine 참조 0"을 강제한다.
> 첫 산출물은 숫자 토큰 파서 셋(`DurationToken`·`UnitToken`·`NumberToken`).
> **D-core-1 확정**: `1u`는 절대 40px이 아니라 **기준 폭 ÷ 48**이다(소유자, 2026-08-05).
> `UnitToken`은 기준 폭을 인자로 받아야 한다 → **b-0**에서 즉시 고친다.

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

⚠ 리그 스키마에 반드시 담아야 할 것 (VnTool D-core-3):
   RectTransform의 위치는 단순 TRS가 아니라 anchorMin/anchorMax/pivot/sizeDelta와
   부모 rect 크기에 의존한다. 리그가 앵커를 고정으로 쓰는지 스트레치로 쓰는지에 따라
   필요한 필드가 달라진다. 이것이 스키마에 없으면 코어(U13-b)가 위치를 재현할 수 없다.
   실제 리그를 보고 무엇이 필요한지 판단하고, 뺀 필드가 있으면 왜 뺐는지 남길 것.

작업 원칙:
- 값은 지금 값 그대로 내보낸다. 이번에 튜닝하지 말 것.
- 규약 경로로 표현 못 하거나 내보낼 수 없는 항목은 건너뛰되 경고 목록으로 보고한다 —
  조용히 빠뜨리지 말 것.
- 스키마 문서를 함께 낸다: 각 필드의 의미·단위·좌표계 기준. VnTool이 이것만 보고
  읽을 수 있어야 한다.

수용 기준: 덤프 생성 확인, BGM 키 조회 가능, 스키마 문서만으로 필드 해석이 가능하다.

커밋은 U12-전체 단위로.
```

## 3. U13-b — 코어 추출 + 리듀서 표준화 (여섯 단계)

U13-b를 한 덩어리로 지시하면 "커맨드 51개를 옮긴다"로 읽히는데, 실제 순서는 그렇지 않다.
**옮기기 전에 옮길 바닥(트랜스폼 수학 → 노드 트리 → 정착 계산)이 먼저 있어야 한다.**
아래 여섯 단계는 각각 독립 커밋이고, **b-1~b-3이 진짜 작업이며 b-5는 그 위에서 반복 노동**이다.

| 단계 | 내용 | 선행 |
|---|---|---|
| **b-0** | 토큰 마무리 — `1u`를 기준 폭 파생으로 (D-core-1 확정) | 없음. **지금 즉시** |
| **b-1** | 값 타입 + 트랜스폼 수학 (유니티 대조 하네스 포함) | b-0 |
| **b-2** | 리그 노드 트리 모델 (`StageState`의 뼈대) | b-1 + U12-전체 |
| **b-3** | `CharacterPlacementTargetLedger` 승격 — 정착 계산 순수화 | b-2 |
| **b-4** | `ClaimTarget` 규약화 (타입 경계) | b-3 + 표본 재추정 |
| **b-5** | 커맨드 이관 — 카테고리 묶음 단위 | b-4 |

### b-0 — 토큰 마무리: `1u`는 기준 폭 ÷ 48

```
Ked.Presentation.Core의 UnitToken을 고쳐줘.

확정 사항(VnTool 쪽 D-core-1): 1u는 절대 40px이 아니라 "가로 기준 해상도의 1/48"이다.
해상도가 다른 게임에서도 화면 비율상 같은 크기여야 한다.

지금 코드는 이렇게 되어 있다:
  public const float ReferenceStageWidth = 1920f;
  public const float UnitPixels = ReferenceStageWidth / StageWidthDivisor; // 40px

문제: 기준 폭이 const라 게임별로 못 바꾼다. VnTool은 이미 기준 해상도를 게임 정의
파일에서 읽어 교체할 수 있게 해 두었고(GameDefinition.PreviewResolution),
U12-전체도 CanvasScaler 기준 해상도를 데이터로 내보내기로 되어 있다.
기준 해상도를 데이터로 내면서 1u 환산만 코드에 박으면 데이터로 낸 의미가 없다.

작업:
1. 기준 폭을 인자로 받도록 바꾼다. 예: UnitsToPixels(float units, float referenceStageWidth),
   TryParsePixels(string token, float referenceStageWidth, out float pixels).
   호출부가 기준 폭을 어디서 얻을지는 b-2의 tuning으로 이어지므로, 지금은 인자로만 열어 둔다.
2. StageWidthDivisor(48)는 규약이므로 const로 남긴다.
3. 1920은 "기본값"으로만 남기고(폴백), 상수 이름이 절대 크기를 뜻하지 않게 정리한다.
4. 세 파서 전부에 단위 테스트를 붙인다 — 특히 경계: 빈 문자열, 단위 없는 숫자,
   음수(TryParsePixels는 0 클램프 / TryParseSignedPixels는 보존), 대소문자, 소수점.

수용 기준: 같은 토큰이 기준 폭 1920에서 지금과 같은 픽셀을 내고, 3840에서는 두 배를 낸다.
코어 어셈블리에 UnityEngine 참조 0 유지.

커밋은 U13-b-0 단위로.
```

### b-1 — 값 타입 + 트랜스폼 수학 (**여기가 바닥이다**)

> `CharacterPlacementTargetLedger`가 부모를 타고 올라가며 `InverseTransformPoint`를 부른다.
> 이걸 순수 계산으로 옮기지 못하면 그 위의 어떤 것도 못 옮긴다.
> ⚠ **`RectTransform`은 단순 TRS가 아니다** — `anchorMin`/`anchorMax`/`pivot`/`sizeDelta`와
> 부모 rect 크기가 `anchoredPosition` → 실제 위치 계산에 들어간다.

```
Ked.Presentation.Core에 값 타입과 트랜스폼 수학을 넣어줘.

목표: 지금 Unity의 RectTransform이 해 주는 좌표 계산을, 같은 결과가 나오는 순수 계산으로
코어에 만든다. 이것이 이후 모든 단계의 바닥이다.

작업:
1. 값 타입: Vec2, Vec3, Rgba (UnityEngine.Vector2/Vector3/Color 대응). 불변 구조체.
2. 노드 하나의 트랜스폼 상태를 담는 값 타입.
   ⚠ 단순 TRS로는 부족하다. RectTransform의 위치는 다음에 의존한다:
     anchoredPosition, anchorMin, anchorMax, pivot, sizeDelta, localScale, localEulerAngles,
     그리고 부모의 rect 크기.
   이 중 리그가 실제로 쓰는 것만 담되, 무엇을 담고 무엇을 뺐는지 근거를 주석에 남길 것.
   (앵커를 고정으로 쓰는지 스트레치로 쓰는지에 따라 필요한 필드가 달라진다 — 실제 리그를 보고 정할 것)
3. 부모 사슬을 따라가는 변환: TransformPoint / InverseTransformPoint에 대응하는 순수 함수.
   입력은 (노드 트리, 노드 키, 점), 출력은 좌표. Unity 참조 없이.

검증 (이 단계의 핵심 산출물이다):
4. EditMode 테스트로 유니티 대조 하네스를 만든다 —
   실제 RectTransform 계층을 코드로 조립해 여러 값을 넣고,
   rect.TransformPoint(p) / InverseTransformPoint(p)와 코어 계산 결과를 비교한다.
   중첩 2~3단계, 스케일·회전·앵커 조합을 섞을 것.
5. 허용 오차(ε)를 여기서 정하고 근거를 문서에 남긴다. 이 ε 정책은 나중에 U14가 그대로 쓴다.

수용 기준: 대조 하네스 전 케이스 통과. 이 하네스가 U14의 축소판이므로 여기서 잘 만들면
U14가 그만큼 싸진다.

커밋은 U13-b-1 단위로.
```

### b-2 — 리그 노드 트리 (`StageState`의 뼈대)

```
Ked.Presentation.Core에 리그 노드 트리 모델을 넣어줘. 선행: b-1, U12-전체(리그 스키마).

목표: StageState가 "슬롯 딕셔너리"가 아니라 "노드 트리"임을 코드로 세운다.
CharacterPlacementTargetLedger가 current.parent로 타고 올라가던 그 구조가 데이터가 되어야 한다.

작업:
1. 노드 트리: 논리 키(slotKey/stage/layer/rig 등) → (부모 키, b-1의 트랜스폼 상태).
   키는 문자열 논리 식별자다. RectTransform 참조가 코어에 들어오면 안 된다.
2. U12-전체가 내보낸 리그 스키마 4종을 이 트리의 초기 모양으로 읽어들이는 경로.
   스키마는 tuning 인자로 온다 — 코어에 리그 구조를 하드코딩하지 말 것.
3. 트리 위에서 b-1의 변환을 쓰는 조회 API (특정 노드의 월드/부모 로컬 좌표).

수용 기준: 실제 리그 스키마를 넣으면 유니티 계층과 같은 부모 관계가 재현되고,
같은 노드에 대해 b-1 하네스와 같은 좌표가 나온다.

커밋은 U13-b-2 단위로.
```

### b-3 — Ledger 승격: "적용 → 측정 → 복원"을 없앤다

> 이 단계는 **VnTool을 위한 일이 아니라 런타임 자신의 견고함**이다.
> 지금 `MeasureSettledWorldPoint`는 정착 상태를 알기 위해 유니티를 잠깐 오염시켰다 되돌리는데,
> `try/finally`가 없어 측정 중 예외가 나면 리그가 더럽게 남는다.

```
CharacterPlacementTargetLedger를 코어의 정착 상태 계산으로 승격시켜줘. 선행: b-2.

배경: 지금 이 클래스는 "트윈이 다 끝났다면 어디에 있을 것인가"를 이미 별도로 보관하고 있다
(_targets: RectTransform -> AnchoredPosition/LocalScale/LocalEuler).
이게 StageState의 원형이다. 두 가지만 바꾸면 코어로 간다.

작업:
1. 키를 RectTransform -> 논리 식별자로 바꾼다(b-2의 노드 키).
2. MeasureSettledWorldPoint / WorldPointToSettledParentLocalPoint의 계산 방식을 바꾼다.
   지금: 부모들을 target 값으로 잠깐 세팅 -> 측정 -> 복원
   앞으로: 노드 트리에서 "target 값이 적용된 트리"를 만들어 b-1 수학으로 직접 계산.
   유니티를 건드리지 않으므로 복원이 필요 없고, 예외가 나도 리그가 더러워지지 않는다.
3. 호스트 쪽(런타임)은 코어가 낸 결과를 RectTransform에 적용하는 얇은 어댑터만 남긴다.

수용 기준:
- 기존 재생 동작 불변(대표 에피소드 스모크). 특히 shot/focus 계열이 같은 자리에 온다.
- 측정 중 예외가 나도 리그 상태가 변하지 않는다(테스트로 고정).
- 코어 쪽 계산에 UnityEngine 참조 0.

커밋은 U13-b-3 단위로.
```

### b-4 — `ClaimTarget` 규약화 (타입 경계)

> **착수 전에 표본 5~10개를 읽고 편차를 보고할 것.** 조사는 51곳의 개수만 셌다(조사 §6-3).
> 편차가 크면 여기가 U13-b의 대부분이고, 작으면 b-5가 기계적 반복이 된다.

```
ClaimTarget 패턴을 규약으로 승격시켜줘. 선행: b-3.

배경: 51개 커맨드가 "목표값 계산"과 "트윈"을 분리해 두었지만 이건 규약이 아니라 관습이다.
CommandBase에 선언이 없고 51곳이 각자 구현했다.

먼저 할 것: 표본 5~10개를 읽고 편차를 정리해 보고한다.
  - 시그니처가 같은가, 목표값을 어디에 저장하는가, 실패/무효 입력을 어떻게 다루는가
  - 이 보고 없이 다음으로 넘어가지 말 것

작업:
1. "스펙 -> 목표 상태" 변환의 타입 경계를 코어에 정의한다.
   입력: 커맨드 인자(파싱된 값) + 현재 StageState + tuning
   출력: 목표 상태 변경분. 순수 함수여야 한다.
2. 호스트 쪽 경계도 명시한다: CommandBase는 코루틴 호스트(MonoBehaviour)를 물고 있으므로
   코어로 가지 않는다. 코어에 가는 것은 각 커맨드의 변환부뿐이다.
   트윈 실행부와 DOTween 의존(67파일)은 호스트에 남는다.
3. 커맨드 1~2개를 이 경계로 실제 이관해 본보기를 만든다(placement 계열 권장).

수용 기준: 본보기 커맨드가 코어 순수 함수 + 호스트 어댑터로 갈라지고 동작이 불변이다.
표본 보고에 근거한 b-5 작업량 재추정이 함께 나온다.

커밋은 U13-b-4 단위로.
```

### b-5 — 커맨드 이관 (묶음 단위, 반복)

```
커맨드를 b-4의 경계로 이관해줘. 선행: b-4. 묶음마다 커밋한다.

순서(VnTool 쪽 D-2b-3 우선순위 — 구도의 대부분이고 전부 셰이더 없는 픽셀 산수다):
  1) char_rig_placement (14)
  2) char_rig_depth (7)
  3) shot (5)
  4) char_rig_staging (12)

규칙:
- 묶음마다 코어 단위 테스트(리듀서 골든)를 붙이고 기존 재생 동작 불변을 확인한다.
- 아직 안 옮긴 커맨드는 버리지 말고 Unhandled 목록에 남긴다(커맨드명 + 출처).
  이 목록이 그대로 다음 작업 백로그이고, VnTool 프리뷰가 "반영 안 된 연출 N"으로 보여 준다.
- audio(6)는 정지 프레임에 표시할 것이 없으므로 이번 범위가 아니다.
- transition(19)은 라인 경계에서 대개 완료 상태라 사실상 불리언에 가깝다 — 늦게 잡아도 싸다.

커밋은 U13-b-5-{묶음이름} 단위로.
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
