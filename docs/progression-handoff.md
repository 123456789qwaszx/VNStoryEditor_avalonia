# `ked-progression` 작업자에게 — VnTool 쪽에서 알아야 할 것

기준: 2026-08-18 (2차) · VnTool(`java-start`) 저장소 · 테스트 1171 통과

계약 원문은 [`runtime-contract.md`](runtime-contract.md) **2부**이고, 충돌하면 그쪽이 정본이다.
이 문서는 **저작 쪽에서 무엇이 바뀌었고 무엇을 믿어도 되는지**만 적는다.

> 1차 handoff(`Stats[]`가 나가기 시작했다는 그것)에 적혀 있던 내용 중 이미 닫힌 것은
> 여기서 걷어냈다. 이 파일 하나가 최신이다.

---

## 0. 한 장 요약

| | 상태 |
|---|---|
| `Stats[]` 내보내기 | ✅ 나간다 (1차에 닫힘) |
| **엔딩키 — 도착 에피소드의 `EndingKey`** | ✅ **나가기 시작했다** (이번) |
| 같은 도착에 엔딩키 충돌 → 내보내기 거부 | ✅ 걸었다 (그쪽 요청) |
| `Option.ViaNodeId` | ⛔ **그쪽 모델에 자리가 생겨야 낸다 — 이것 하나만 막혀 있다** |
| 표본 JSON | ✅ 엔딩 둘이 든 실물로 새로 냈다 |

**그쪽에 부탁하는 것은 §2 하나뿐이다.** 나머지는 보고다.

---

## 1. 엔딩키가 실리기 시작했다 (v11)

합의대로 **저작 표면과 계약 표면이 다르다.** 번역은 내보내기가 한다.

| 저작 (기획자가 보는 것) | 계약 (`progression.json`) |
|---|---|
| 엔딩키는 **간선**의 칸이다 (`간선` 시트 J열) | 엔딩키는 **도착 에피소드**의 `EndingKey`다 |

그쪽 D2("한 곳이 정한다")를 유지하기 위해서다. 받는 모양은 **바뀌지 않았다** — 이미 있던
두 필드가 이제 실제 값으로 채워질 뿐이다.

```json
{
  "EpisodeId": "좋은끝",
  "Title": "함께 문을 연다",
  "NextOptions": [],
  "IsChapterEndingCandidate": true,
  "EndingKey": "ch01_true"
}
```

- `IsChapterEndingCandidate`는 `EndingKey`가 비지 않았다와 **정확히 같다.** 둘 중 하나만
  봐도 된다(둘 다 내는 것은 스키마 1:1을 지키려는 것뿐이다).
- 엔딩 에피소드라고 `NextOptions`가 반드시 비는 것은 아니다. **"끝날 수 있는 곳"이지
  "막다른 곳"이 아니다** — 지금 표본에서는 우연히 비어 있다.

### 저작 쪽 `종류`(선택지/자동)는 나가지 않는다

`간선` 시트에 `종류` 열이 생겼지만 **계약을 넓히지 않았다.** 계약은 `ChoiceLabel`이
빈 문자열인 것으로 이미 자동 진행을 구별하고 있다. `종류`는 *문구를 실수로 지운 것*과
*의도한 자동 진행*을 저작 쪽에서 갈라 내는 안전장치이지 런타임 입력이 아니다
(그쪽 D5가 가리키던 구멍이 저작 쪽에서 닫혔다는 뜻이다).

---

## 2. ⛔ 그쪽에 필요한 것 — `EpisodeOption.ViaNodeId`

**이것 하나가 남았다.**

간선에 "대사 없는 연출 한 덩어리"를 매다는 기능이 저작 쪽에 다 들어갔다 — 기획자가
엔딩키를 적으면 툴이 연출 그래프에 노드를 세우고 이름을 워크북에 되쓴다. 화면에도
선다. **그런데 그 이름을 실어 보낼 칸이 계약에 없다.**

```
저작: 간선의 `연출` 칸  →  계약: 그 Option의 ViaNodeId
```

- 이름은 합의대로 **`ViaNodeId`**다(계약서 §H-3에 이미 있던 이름).
- ⚠ **"노드"는 Yarn 노드다** — 에피소드 노드가 아니다. 이 모호함은 이름을 하나로
  유지하는 값과 바꾼 것이라 주석으로 못 박아 두었다.
- 값은 그 길을 탈 때 재생할 연출의 Yarn 노드 이름. 없으면 빈 문자열.

**그쪽 `EpisodeOption`에 필드가 서면 그날 내보내기에 한 줄 붙는다.** 그전까지 이 값은
저작·화면에만 살고 JSON에 나가지 않는다 — 없는 필드를 먼저 내면 수입기가 모르는 키를
만난다(그쪽 규율대로면 진단이 뜬다).

---

## 3. 엔딩키 충돌 — 내보내기가 거부한다 (요청하신 것)

> 같은 도착 에피소드로 들어오는 간선들이 서로 다른 엔딩키를 가지면 **오류로 막고 파일을
> 내지 않는다.**

`ChapterDiagnosticCode.EndingKeyConflict`. 같은 키가 여럿 들어오는 것(여러 길이 한 엔딩으로
모이는 흔한 패턴)은 정상이다.

이게 **검증 소유 경계의 예외**라는 데 동의한다: 그래프 무결성은 원래 수입 쪽이 정본이지만,
이 하나는 저작 쪽만 볼 수 있다. 조용히 하나를 고르면 나머지가 사라지고, **JSON에 도착한
시점에는 이미 키가 하나라 수입기가 볼 방법이 없다.**

깔고 가는 가정은 **"한 에피소드 = 한 엔딩"**이다. 엔딩마다 대사가 다르니 에피소드가 따로
있는 것이 자연스럽고, 깨지면 에피소드를 하나 더 만드는 싼 우회가 있다.

---

## 4. 표본 JSON을 새로 냈다

[`ch01.progression.sample.json`](ch01.progression.sample.json) — **엔딩 둘이 든 실물 출력**이다.
로더 첫 테스트의 입력으로 그대로 쓸 수 있다.

```
시작 ──[라루를 믿는다 · trust +2]──▶ 믿는길 ──(자동)──▶ 좋은끝    EndingKey "ch01_true"
   └─[혼자 간다 · fatigue +1]────▶ 혼자길 ──(자동)──▶ 쓸쓸한끝  EndingKey "ch01_alone"
```

한 파일에서 보이는 것: 스탯 사전 · 스탯 증감이 붙은 선택지 · **문구 없는 자동 간선**
(`ChoiceLabel: ""`) · 서로 다른 두 엔딩키.

> 이전 표본은 손으로 만든 것이라 v11에서 엔딩키가 실리기 시작했는데도 **엔딩이 하나도 없는
> 옛 모양**으로 남아 있었다. 이제 `ProgressionSampleGoldenTests`가 실물 출력으로 붙들고
> 있어 규격이 바뀌면 테스트가 먼저 깨진다. 남에게 건넨 표본이 낡는 것은 계약서가 낡는 것과
> 같은 무게라고 봤다.

---

## 5. JSON을 읽을 때 걸리기 쉬운 것들

계약서 2부 §F에 다 있지만, 실제로 물릴 만한 것만 추린다.

| | |
|---|---|
| **`IntValue`는 0이면 키가 없다** | `WhenWritingDefault`. DTO 필드를 `int?`로 두면 가장 흔한 조건 `flag == false`가 통째로 어긋난다 — **반드시 `int`** |
| **`NextOptions` 순서가 화면 순서다** | `간선` 시트의 행 순서 그대로다. **다시 정렬하지 말 것** |
| **노드 쪽 관문은 언제나 빈 배열** | v8에서 간선으로 내려갔다. `Node.VisibleConditions`·`UnlockConditions`는 스키마 1:1을 위해 자리만 남아 있다 |
| **`ChoiceLabel`이 빈 문자열 = 자동 진행** | 에피소드당 하나, 관문 금지 |
| **`NotEqual`은 안 나온다** | 저작 파서가 닫아 두었다. 넣지 말 것 |
| **`IndexText`는 언제나 빈 문자열** | v5에서 폐지 |
| **`EndingRules`는 언제나 빈 배열** | 모양이 정해지면 이쪽이 채운다 (§7) |
| **`Attachments`는 언제나 빈 배열** | v1 비범위 |
| **타입 이름은 우리가 번역한다** | 저작 `Int` → JSON `"Number"`, `AtLeast` → `"GreaterOrEqual"`. JSON에 `"Int"`는 나오지 않는다 |
| **키는 PascalCase** | camelCase 정책 없음 |

### 디스크에 있는 파일은 이미 검증을 통과한 것이다

내보내기는 **검증 오류가 있으면 파일을 만들지 않는다.** 거부되면 화면 보고에 사유가 서고
이전 파일이 그대로 남는다.

**다만 그것에 기대지 말 것** — 손으로 고친 파일, 옛 버전 파일이 올 수 있다. 그쪽 규율 1
(침묵 금지)이 그래서 옳다. "정상 경로로 나온 파일은 깨끗하다" 정도만 알아 두면 된다.

---

## 6. 도달성 증명을 가져갈 때 (그쪽 W8)

원본: `src/Vn.Authoring/Chapters/ChapterReachabilityProver.cs`.
상태 = (에피소드, 스탯 정수 벡터)로 완전 탐색하며 `Math.Clamp(값, Minimum, Maximum)`으로
걷는다. 판정 기준은 그쪽이 적은 대로 **"이관 전후로 증명 결과가 같아야 한다"**가 맞다.

- **증명기 자체는 안 건드렸다.** VnTool 화면이 그것을 챕터별 (내용해시, 결과) 캐시 뒤에서
  부를 뿐이다 — 캐시는 화면의 사정이지 증명의 사정이 아니다.
- **상태공간이 곧 비용이다.** 스탯 5개 × 0..100이면 100억이다. 그쪽 "범위 0..5" 제한은
  취향이 아니라 성능 결정이고, 넓혀 놓고 나중에 좁히면 이미 쓴 조건식이 전부 의미를 잃는다.

---

## 7. 아직 열린 것

### `EndingRules`의 모양 — 그쪽 결정 대기

지금 **언제나 비워서** 낸다. 모델에 넣지 않은 그쪽 판단에 동의한다(데이터에 없는 것을
타입으로 먼저 만들면 영원히 안 타는 분기가 생긴다).

**모양이 정해지면 이쪽이 채운다.** 다만 그 결정을 미룰수록 값이 붙는다 — 엔딩키가 이제
실제로 나가므로, "이 엔딩이면 다음 챕터는 무엇" 규칙을 **지금은 아무도 안 들고 있다.**

### 앨범 — 그쪽 세이브(G4)가 서야 한다

엔딩 목록이 곧 앨범이지만 "도달했나"는 플레이어 데이터다. 이쪽은 **자리도 만들지 않았다.**

### bool 스탯을 켜는 경로 — 소유자가 "당장은 무시"

확인한 사실만 남긴다: `StatDelta`는 증감 전용이고 bool 증감은 오류다 → **bool 스탯은
`Initial`에 영원히 묶여 있다.** 조건에서 쓸 수는 있지만 값이 변할 방법이 없다. 이쪽
소유자가 별건으로 미뤘고, 규격을 정하면 저작·계약 양쪽이 함께 움직여야 한다.

### 챕터 간 `cleared:`

조건 파서에 `cleared:` 탈출구가 있고 `EpisodeCleared`로 나간다. **챕터 간은 아직 없다.**

---

## 8. ⚠ 브랜치 지뢰 (1차에서 이어지는 경고)

`docs/work-plan.md`의 근거 셋 — `ChapterEndingRule` · `EpisodeSelectionStateData` ·
`VNSaveData` — 은 전부 **`test13` 브랜치(2026-08-12에 멈춤)**의 것이다.
현재 `dev`(08-17)는 세이브·진행·변수 저장소를 통째로 걷어냈다.

| | `dev`에서 |
|---|---|
| `VNSaveData` · 세이브/로드 | 0파일 |
| `VariableStorage` (런타임 코드가 잡는 자리) | 0파일 |
| `Progression` · `ChoiceReplay` | 0파일 |
| 서브 레인 `pres_*` · 원샷 레인 | 0파일 |
| `InlineAdvance` / `[adv/]` | 0파일 |

`PresentationCore/Flow/`의 `StepGate*` · `SequenceSpecSO` · `PresentationSession`도 `dev`에
없다(`phase2-*` · `test13`에만). `SequenceSpecSO.sequenceKey`가 가리키는 `RouteCatalogSO`는
**어느 브랜치에도 없다**.

**그리고 도구 하나 — 이 저장소들에서 `find`가 없는 폴더를 보여 준다**(낡은 디렉터리 항목).
지난 세션에 두 번 속아 계약서에 틀린 내용을 적었다. **존재 확인은 `ls`·`grep`으로 한다.**

---

## 9. `.yarn` 쪽 — 대본이 하나가 됐다

진행 계층과 직접 상관은 없지만 `DialogueEntryId`가 가리키는 대상이다.

VnTool이 내던 트리오(`Story_*` / `Set_*` / `Pres_*`)가 **`Story_{이름}.yarn` 하나**로
합쳐졌다. 소유자 결정 — 런타임이 여러 레인을 읽고 동기화하고 그것을 다시 롤백에 반영하는
값을 치르지 않기로 했고("디버깅비용이 최소 10배 이상"), **합치는 쪽이 저작 도구**가 됐다.

`EpisodePlayer.StartGameAsync(string nodeName)`가 경계다 — **런타임은 챕터도 에피소드
구조도 모른다.** 어느 노드를 재생할지는 **그쪽 전이기**가 정하고 런타임은 이름만 받는다.
`EpisodeNode.DialogueEntryId`가 그 이름이고, §2의 `ViaNodeId`도 같은 종류의 이름이다.

---

## 10. 이쪽 참조 위치

| 무엇 | 어디 (`java-start` 기준) |
|---|---|
| 계약 원문 | [`docs/runtime-contract.md`](runtime-contract.md) 2부·3부 |
| **v11 규격 (엔딩·연출)** | [`docs/work-orders/edge-presentation-orders.md`](work-orders/edge-presentation-orders.md) |
| 내보내기 | `src/Vn.Authoring/Chapters/ChapterProgressionExporter.cs` |
| 엔딩키 규칙 (한 곳) | `ChapterGraphModel.EndingKeyOf(episodeId)` |
| 충돌 검사 | `ChapterWorkbookReader.VerifyEndingKeysAgree` |
| 도달성 증명 (이관 원본) | `src/Vn.Authoring/Chapters/ChapterReachabilityProver.cs` |
| 조건식 분해·조립 | `src/Vn.Authoring/Chapters/ConditionExpressionParser.cs` |
| 표본을 붙드는 테스트 | `tests/Vn.Authoring.Tests/Chapters/ProgressionSampleGoldenTests.cs` |
| 결정 기록 | [`docs/run-log.md`](run-log.md) — 2026-08-18 항목들 |

새로 필요한 것이 생기면 `runtime-contract.md` **3부(§G — VnTool이 할 일)**에 적어 주면 된다.
그 절이 이쪽 작업 목록이다.
