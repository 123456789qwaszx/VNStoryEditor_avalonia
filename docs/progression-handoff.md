# `ked-progression` 작업자에게 — VnTool 쪽에서 알아야 할 것

기준: 2026-08-18 · VnTool(`java-start`) 저장소 기준 · 테스트 1149 통과

이 문서는 **저작 도구 쪽에서 무엇이 바뀌었고 무엇을 믿어도 되는지**를 적는다.
계약 원문은 [`runtime-contract.md`](runtime-contract.md) **2부**이고, 충돌하면 그쪽이 정본이다.

---

## 0. 제일 급한 것 — `Stats[]`가 나가기 시작했다

**그쪽 §5-①(다음 작업을 막는 유일한 항목)이 닫혔다.** 커밋 `559a1fc`.

`progression.json` 최상위에 `Stats[]`가 실린다. **배달 경로는 (가)안** — 챕터 JSON에
실려 나간다. 로더 시그니처를 `Load(ChapterProgressionDto dto)` 하나로 닫아도 된다.

```json
"Stats": [
  { "Key": "trust",   "DisplayName": "신뢰", "Type": "Number",
    "Initial": 0, "Minimum": 0, "Maximum": 10 },
  { "Key": "fatigue", "DisplayName": "피로", "Type": "Number",
    "Initial": 0, "Minimum": 0, "Maximum": 10 }
]
```

`Nodes`보다 **앞**에 둔다 — 조건·스탯변화의 키를 검사하려면 스탯 사전이 먼저 서야 한다.

### ⚠ 타입 이름을 우리가 번역한다

저작 쪽 enum은 `Int`/`Bool`인데 그쪽 `StatType`은 **`Number`**/`Bool`이다.
그대로 냈으면 로더 검사 1번("알 수 없는 enum 이름")에 정확히 걸렸을 자리라,
**내보내기가 `Int → "Number"`로 옮겨서 낸다.** JSON에 `"Int"`는 나오지 않는다.

조건 연산자를 `AtLeast → "GreaterOrEqual"`로 옮기는 것과 같은 자리, 같은 이유다.

### 싣지 않는 것

`SourceRow`(엑셀 몇 행에서 왔는지)는 저작의 사정이라 안 나간다.

---

## 1. 실물 표본

[`ch01.progression.sample.json`](ch01.progression.sample.json)이 옆에 있다 — **`Stats[]`가 실린 진짜 출력**이다
(에피소드 4개, 스탯 2개, 갈라졌다 다시 만나는 최소 모양). 로더 첫 테스트의 입력으로
그대로 쓸 수 있다.

다시 만들려면: VnTool로 프로젝트를 열면 `exported/{챕터}.progression.json`이
**자동으로** 나간다(사람이 [내보내기]를 누르지 않는다).

---

## 2. 디스크에 있는 `progression.json`은 이미 검증을 통과한 것이다

**내보내기는 검증 오류가 있으면 파일을 만들지 않는다.** 거부되면 화면 보고에 사유가
서고 파일은 그대로 남는다(쓰지 않는다).

그래서 로더가 받는 파일은 이미 이쪽 `ChapterValidator` + `ChapterReachabilityProver`를
통과한 것이다. **다만 그것에 기대지 말 것** — 손으로 고친 파일, 옛 버전 파일이 올 수
있고, 그쪽 규율 1(침묵 금지)이 그래서 옳다. 여기서는 "정상 경로로 나온 파일은 깨끗하다"
정도만 알아 두면 된다.

---

## 3. ⚠ 그쪽 계획이 `test13`에 얹혀 있다

`docs/work-plan.md`의 근거 셋 — `ChapterEndingRule` · `EpisodeSelectionStateData` ·
`VNSaveData` — 은 전부 **`test13` 브랜치(2026-08-12에 멈춤)**의 것이다.

**현재 브랜치 `dev`(08-17)는 세이브·진행·변수 저장소를 통째로 걷어냈다.** 실측:

| | `dev`에서 |
|---|---|
| `VNSaveData` · 세이브/로드 | **0파일** |
| `VariableStorage` (런타임 코드가 잡는 자리) | **0파일** |
| `Progression` · `ChoiceReplay` | **0파일** |
| 서브 레인 `pres_*` · 원샷 레인 `OneShot` | **0파일** |
| `InlineAdvance` / `[adv/]` | **0파일** |

그쪽 handoff가 *"구 런타임에서 코드를 가져오지 말 것 — 명세로만 읽는다"*고 적어 둔 것과
같은 이야기다. 다만 **시나리오 층의 모양을 test13에서 받아쓰는 계획**은 그 경계에 서 있다.
`ChapterEndingRule`이 "이미 있으니 받아 적으면 된다"가 아니라 **"팀이 그 설계를 되살릴
의도인가"를 먼저 확인**하는 편이 안전하다.

### 브랜치 지뢰 하나 더

`PresentationCore/Flow/`의 `StepGate*` · `SequenceSpecSO` · `PresentationSession`은
**`dev`에 없다** — `phase2-rewrite` · `phase2-core-extraction` · `test13`에만 있다.
`SequenceSpecSO.sequenceKey`가 주석에서 가리키는 `RouteCatalogSO`는 **어느 브랜치에도
없다**(끊어진 참조).

### 그리고 도구 하나 — `find`를 믿지 말 것

이 저장소들에서 `find`가 **없는 폴더를 보여 준다**(낡은 디렉터리 항목). 이번 세션에서
두 번 속아 계약서에 틀린 내용을 적었다가 소유자가 짚어 줘서 고쳤다.
**존재 확인은 `ls`와 `grep`으로 한다.**

---

## 4. `.yarn` 쪽도 바뀌었다 — 대본이 하나가 됐다

진행 계층과 직접 상관은 없지만, `DialogueEntryId`가 가리키는 대상이라 알아 두면 좋다.

VnTool이 내던 트리오(`Story_*` / `Set_*` / `Pres_*`)가 **`Story_{이름}.yarn` 하나**로
합쳐졌다. 소유자 결정 — 런타임이 여러 레인을 읽고 동기화하고 그것을 다시 롤백에
반영하는 값을 치르지 않기로 했고("디버깅비용이 최소 10배 이상"), **합치는 쪽이 저작
도구**가 됐다.

`EpisodePlayer.StartGameAsync(string nodeName)`가 그 경계다 — **런타임은 챕터도
에피소드 구조도 모른다.** 어느 노드를 재생할지는 **그쪽 전이기**가 정하고, 런타임은
결정된 노드 이름만 받는다. `EpisodeNode.DialogueEntryId`가 그 이름이다.

---

## 5. 도달성 증명을 가져갈 때 (그쪽 W8)

원본: `src/Vn.Authoring/Chapters/ChapterReachabilityProver.cs`.
상태 = (에피소드, 스탯 정수 벡터)로 완전 탐색하며 `Math.Clamp(값, Minimum, Maximum)`으로
걷는다. **판정 기준은 그쪽이 적어 둔 대로 "이관 전후로 증명 결과가 같아야 한다"가 맞다.**

두 가지만 미리 알아 두면 좋다.

- **증명기 자체는 이번에 안 건드렸다.** 다만 VnTool 화면이 그것을 **챕터별 (내용해시,
  결과) 캐시** 뒤에서 부른다(성능 — 노드 60개에서 갱신 426ms → 111ms였다). 캐시는
  화면의 사정이지 증명의 사정이 아니다.
- **상태공간이 곧 비용이다.** 스탯 5개 × 0..100이면 100억이다. 그쪽 계획의 "범위 0..5"
  제한은 취향이 아니라 성능 결정이고, 넓혀 놓고 나중에 좁히면 이미 쓴 조건식이 전부
  의미를 잃는다 — 그 판단에 동의한다.

---

## 6. 열린 결정에 대한 이쪽 의견

그쪽 §5의 항목들에 대해, 저작 쪽에서 보이는 것만 적는다. **결정은 소유자 몫이다.**

### D1 — 스탯 수명 (챕터를 넘으면 되돌아간다)

**이쪽이 `Stats[]`를 내보내는 것은 이 결정에 막히지 않는다.** 어느 쪽으로 나든
**나가는 필드는 동일**하고, 바뀌는 것은 `Initial`의 *뜻*이지 모양이 아니다
(챕터가 소유 → 시작값 / 시나리오가 소유 → 도달성 증명의 가정값).

그래서 결정을 기다리지 않고 먼저 풀었다. 반대로, 결정이 나면 **이쪽 시트 설명만
바뀌고 코드는 그대로**일 가능성이 높다.

### `EndingRules`의 모양

지금 `List<object>`를 **언제나 비워서** 낸다. 모델에 넣지 않은 그쪽 판단에 동의한다 —
데이터에 없는 것을 타입으로 먼저 만들면 영원히 안 타는 분기가 생긴다.

⚠ 다만 CHANGELOG의 근거("exporter가 비워서 내므로 데이터에 모양이 없다")는 **`test13`에
`ChapterEndingRule`이 있다**는 발견으로 전제가 흔들렸다. 결정 기록이라 그쪽이 정정 여부를
판단할 일이고, 이쪽은 **모양이 정해지면 내보내기에 채워 넣는다**는 것만 약속할 수 있다.

### `Option.ViaNodeId` (§G8)

"선택지 문구를 열쇠로 자유 씬을 매단다"가 **저작에서는 되는데 내보내기에 자리가 없다.**
옛 계약서가 "스탯 경계와 함께 정하라"고 했고 그 경계가 §0으로 풀렸으므로 **지금이 그
자리**다. 모델에 칸 하나 더하는 비용은 0이고, 실제 작업은 이쪽 발행 경로 수정이다.

### `Flag` · `ChapterCleared` · `Token`

- `Flag` — 이쪽은 v9에서 **`Stat` 0/1 + `Equal`**로 통일했다. 버려도 될 듯하다는 데 동의.
- `ChapterCleared` — 이쪽 조건 파서에 `cleared:` 탈출구가 있고 `EpisodeCleared`로 나간다.
  챕터 간은 아직 없다.
- `Token`(아이템/열쇠) — 이쪽 **작가 계층**에 아이템·능력 개념이 있지만 그건 Yarn 변수로
  살고 **진행 JSON에 나가지 않는다**. 두 계층이 다른 것이니 섞지 말 것.

---

## 7. JSON을 읽을 때 걸리기 쉬운 것들

계약서 2부 §F에 다 있지만, 실제로 물릴 만한 것만 추린다.

| | |
|---|---|
| **`IntValue`는 0이면 키가 없다** | `WhenWritingDefault`. DTO 필드를 `int?`로 두면 가장 흔한 조건 `flag == false`가 통째로 어긋난다 — **반드시 `int`** |
| **`NextOptions` 순서가 화면 순서다** | `간선` 시트의 행 순서 그대로다(`SourceRow` 정렬). **다시 정렬하지 말 것** |
| **노드 쪽 관문은 언제나 빈 배열** | v8에서 간선으로 내려갔다. `Node.VisibleConditions`·`UnlockConditions`는 스키마 1:1을 위해 자리만 남아 있다 |
| **`ChoiceLabel`이 빈 문자열 = 보이지 않는 기본** | 에피소드당 하나, 관문 금지 |
| **`NotEqual`은 안 나온다** | 저작 파서가 닫아 두었다. 넣지 말 것 |
| **`IndexText`는 언제나 빈 문자열** | v5에서 폐지 |
| **키는 PascalCase** | camelCase 정책 없음 |

---

## 8. 이쪽 참조 위치

| 무엇 | 어디 (`java-start` 기준) |
|---|---|
| 계약 원문 | [`docs/runtime-contract.md`](runtime-contract.md) 2부·3부 |
| 내보내기 | `src/Vn.Authoring/Chapters/ChapterProgressionExporter.cs` |
| 챕터 모델 (`ChapterStat`) | `src/Vn.Authoring/Chapters/ChapterGraphModel.cs` |
| 검증 | `src/Vn.Authoring/Chapters/ChapterValidator.cs` |
| 도달성 증명 (이관 원본) | `src/Vn.Authoring/Chapters/ChapterReachabilityProver.cs` |
| 조건식 분해·조립 | `src/Vn.Authoring/Chapters/ConditionExpressionParser.cs` |
| 저작 쪽 최신 규격 | [`docs/handoff/current-state.md`](handoff/current-state.md) 맨 위 계약 박스 |
| 결정 기록 | [`docs/run-log.md`](run-log.md) — 2026-08-18 항목 넷 |

---

## 9. 요약 — 지금 그쪽을 막는 것은 없다

`Stats[]`가 나가므로 **실데이터로 `ChapterProgression`을 세울 수 있다.** 다음은 그쪽
계획대로 DTO → `ProgressionLoader`(진단을 전부 모아서) → `ChapterTransition` 순서다.

이쪽에서 새로 필요한 것이 생기면 `runtime-contract.md` 3부(§G — VnTool이 할 일)에
적어 주면 된다. 그 절이 이쪽 작업 목록이다.
