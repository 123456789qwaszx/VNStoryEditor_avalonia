# `ked-progression` 작업자에게 — VnTool 쪽에서 알아야 할 것

기준: 2026-08-19 (**3차**) · VnTool(`java-start`) 저장소 · 테스트 1197 통과

계약 원문은 [`runtime-contract.md`](runtime-contract.md) **2부**이고, 충돌하면 그쪽이 정본이다.
이 문서는 **저작 쪽에서 무엇이 바뀌었고 무엇을 믿어도 되는지**만 적는다.

> 그쪽 2차 회신(`ked-progression/docs/vntool-handoff.md`)을 받고 다시 썼다.
> 이 파일 하나가 최신이다.

---

## 0. 한 장 요약 — 부탁하는 것은 **§6의 칸 하나**뿐이다

| | 상태 |
|---|---|
| `Stats[]` · 엔딩키(`EndingKey`) · 엔딩키 충돌 거부 | ✅ 나간다 |
| **`Option.ViaNodeId`** | ✅ **나가기 시작했다** (v11 §6) |
| 표본 JSON에 연출이 실린 실데이터 | ✅ 있다 (그쪽 부탁 2번) |
| `ViaNodeId` 키 이름을 글자로 검증 | ✅ 걸었다 (그쪽 부탁 1번) |
| 증명기를 바꿀 때 알린다 | ✅ 코드에 못 박았다 (그쪽 부탁 3번) |
| **⛔ 깃발을 실을 `StatChange.Op`** | **그쪽 §6-4의 답이 나왔다 — 칸 하나가 필요하다 (§6)** |

**v11 규격 6단계가 전부 닫혔다.** 남은 경계면은 깃발 하나다.

### 정정 — 2차에서 이쪽이 틀리게 적은 것 둘

1. *"`ViaNodeId`는 그쪽 모델에 자리가 생겨야 한다 — 이것 하나만 막혀 있다"* → **틀렸다.**
   `eb1786d`로 이미 있었고 이쪽 문서보다 앞섰다. 저장소를 확인하지 않고 자체 작업지시서의
   *"저쪽 모델 대기"* 줄을 그대로 옮겼다. 없는 병목을 하나 만들어 낸 것이라,
   **저쪽이 회신하지 않았으면 §6이 그만큼 늦었다.**
2. *"'이 엔딩이면 다음 챕터는 무엇' 규칙을 지금은 아무도 안 들고 있다"* → **틀렸다.**
   `EndingRule`·`ScenarioProgression`·`ScenarioTransition`이 이미 있고 픽스처로 돈다.

---

## 1. `ViaNodeId`가 나간다 (v11 §6 — 마지막 칸)

```json
{
  "TargetEpisodeId": "좋은끝",
  "ChoiceLabel": "",
  "ViaNodeId": "엔딩 ch01_true",
  "StatChanges": []
}
```

| | |
|---|---|
| 원천 | 간선의 `연출` 칸 (`ChapterEdge.PresentationNodeName`) |
| 키 이름 | **`ViaNodeId`** — 계약서 §H-3 그대로 |
| 자동 진행 간선 | **붙는다.** 엔딩 전이가 정확히 그 모양이다 |
| 연출 없음 | **빈 문자열로 언제나 나간다** — 키를 없애면 *빠뜨린 것*과 *없는 것*이 같아진다 |

### 부탁 1번 — 키 이름을 글자로 검증했다

그쪽이 짚은 그대로다. 저작 이름(`PresentationNodeName`)과 계약 이름(`ViaNodeId`)이 갈리는
자리이고, 틀리면 역직렬화기가 모르는 속성을 조용히 버려 **오류 없이 연출만 사라진다.**

```csharp
Assert.Equal("fade_trust", Option("ep1").GetProperty("ViaNodeId").GetString());
```

`EdgeKindAndEndingTests` 둘 — 선택지 간선과 자동(엔딩) 간선 양쪽, 그리고 빈 경우.

### 파라미터는 안 붙인다

동의한다. 지속시간·이징·색이 들어오는 순간 경계면이 진짜로 넓어진다. **이 칸은 이름
하나**이고, 이쪽 DTO의 타입이 `string`인 것을 테스트가 붙들고 있다.

---
## 2. 엔딩키 충돌 — 내보내기가 거부한다 (요청하신 것)

> 같은 도착 에피소드로 들어오는 간선들이 서로 다른 `엔딩키`를 가지면 **오류로 막고 파일을
> 내지 않는다.**

`ChapterDiagnosticCode.EndingKeyConflict`. 같은 키가 여럿 들어오는 것(여러 길이 한 엔딩으로
모이는 흔한 패턴)은 정상이다.

이게 **검증 소유 경계의 예외**라는 데 동의한다: 그래프 무결성은 원래 수입 쪽이 정본이지만,
이 하나는 저작 쪽만 볼 수 있다. 조용히 하나를 고르면 나머지가 사라지고, **JSON에 도착한
시점에는 이미 키가 하나라 수입기가 볼 방법이 없다.**

깔고 가는 가정은 **"한 에피소드 = 한 엔딩"**이다. 엔딩마다 대사가 다르니 에피소드가 따로
있는 것이 자연스럽고, 깨지면 에피소드를 하나 더 만드는 싼 우회가 있다.

---

## 3. 표본 JSON — 연출이 실린 실데이터다 (부탁 2번)

[`ch01.progression.sample.json`](ch01.progression.sample.json)

```
시작 ─[라루를 믿는다 · trust +2]→ 믿는길 ─(자동)→ 좋은끝    EndingKey "ch01_true"   Via "엔딩 ch01_true"
   └─[혼자 간다 · fatigue +1]──→ 혼자길 ─(자동)→ 쓸쓸한끝  EndingKey "ch01_alone"  Via "엔딩 ch01_alone"
```

한 파일에서 보이는 것: 스탯 사전 · 증감이 붙은 선택지 · **문구 없는 자동 간선**
(`ChoiceLabel: ""`) · 서로 다른 두 엔딩키 · **`ViaNodeId`가 실린 간선 둘과 빈 간선 둘.**

> *"연출이 실린 실데이터가 이쪽에 아직 없다"* — 이제 있다. 이걸로 그 경계면이 실물로
> 한 번 통과한 게 된다.

`ProgressionSampleGoldenTests`가 실물 출력으로 붙들고 있어 규격이 바뀌면 테스트가 먼저
깨진다. 골든 비교 하나로는 **둘 다 낡은 것**을 못 잡으므로, 표본이 무엇을 보여 주기로 한
파일인지(엔딩 둘 · `ViaNodeId` 둘)를 따로 걸어 두었다.

---
## 4. JSON을 읽을 때 걸리기 쉬운 것들

계약서 2부 §F에 다 있지만, 실제로 물릴 만한 것만 추린다.

| | |
|---|---|
| **`IntValue`는 0이면 키가 없다** | `WhenWritingDefault`. DTO 필드를 `int?`로 두면 가장 흔한 조건 `flag == false`가 통째로 어긋난다 — **반드시 `int`** |
| **`NextOptions` 순서가 화면 순서다** | `간선` 시트의 행 순서 그대로다. **다시 정렬하지 말 것** |
| **노드 쪽 관문은 언제나 빈 배열** | v8에서 간선으로 내려갔다. `Node.VisibleConditions`·`UnlockConditions`는 스키마 1:1을 위해 자리만 남아 있다 |
| **`ChoiceLabel`이 빈 문자열 = 자동 진행** | 에피소드당 하나, 관문 금지 |
| **`NotEqual`은 안 나온다** | 저작 파서가 닫아 두었다. 넣지 말 것 |
| **`IndexText`는 언제나 빈 문자열** | v5에서 폐지 |
| **`EndingRules`는 언제나 빈 배열** | 모양은 그쪽에 이미 있다 — 이쪽이 안 낼 뿐 (§6) |
| **`Attachments`는 언제나 빈 배열** | v1 비범위 |
| **타입 이름은 우리가 번역한다** | 저작 `Int` → JSON `"Number"`, `AtLeast` → `"GreaterOrEqual"`. JSON에 `"Int"`는 나오지 않는다 |
| **키는 PascalCase** | camelCase 정책 없음 |

### 디스크에 있는 파일은 이미 검증을 통과한 것이다

내보내기는 **검증 오류가 있으면 파일을 만들지 않는다.** 거부되면 화면 보고에 사유가 서고
이전 파일이 그대로 남는다.

**다만 그것에 기대지 말 것** — 손으로 고친 파일, 옛 버전 파일이 올 수 있다. 그쪽 규율 1
(침묵 금지)이 그래서 옳다. "정상 경로로 나온 파일은 깨끗하다" 정도만 알아 두면 된다.

---

## 5. 도달성 증명 — 이제 이 코드는 **여러분의 오라클이다** (부탁 3번)

이관을 코퍼스로 고정한 방식에 동의한다. 그 결과로 이쪽에 생긴 의무를 받아 적는다:

> **`ChapterReachabilityProver`의 동작을 바꾸면 — 버그 수정이라도 — 알린다.**

그러지 않으면 저쪽 코퍼스는 옛 답을 들고 있고 저쪽 테스트는 계속 초록이라, **갈렸다는
것을 아무도 모른다.** 그래서 말로만 두지 않고 **파일 맨 위 주석에 못 박았다** — 이 코드를
고치러 오는 사람이 이 문서를 읽었으리라고 기대할 수 없기 때문이다.

- **캐시는 상관없다.** 화면이 (내용해시 → 결과)로 감싸 부르지만 답을 바꾸지 않는다.
  거기 손대는 것은 알릴 일이 아니다.
- **상태공간이 곧 비용이다.** 스탯 5개 × 0..100이면 100억. "범위 0..5" 제한은 취향이
  아니라 성능 결정이고, 넓혀 놓고 나중에 좁히면 이미 쓴 조건식이 전부 의미를 잃는다.
  **기획자에게 전달되어야 한다는 지적에 동의한다** — 안내서(`chapter-layer-guide.md`)에
  넣는다.

### 전역 조건(`EndingSeen`)은 챕터 안 간선에 두지 않는다

그쪽 §4의 요청을 **저작 쪽 규칙으로 받는다.** 챕터 안까지 열면 상태공간이
(한 판 × 전역 이력)으로 곱해져 위의 100억이 거기서 터진다. 지금 조건 문법에 전역을
가리키는 낱말이 없으므로 **지금은 저절로 지켜지고 있고**, 생길 때 시나리오 층 간선에만 둔다.

---

## 6. 아직 열린 것

### `EndingRules` — 모양은 그쪽에 있다. 이쪽이 안 낼 뿐이다

2차에서 이쪽이 *"아무도 안 들고 있다"*고 쓴 것은 틀렸다. `EndingRule`·`ScenarioProgression`·
`ScenarioTransition`이 있고 픽스처로 돈다.

지금은 **언제나 빈 배열**로 낸다. 규칙이 하나도 없는 챕터가 정상이라 아무것도 안 깨진다는
확인을 받았으므로 급하지 않다 — **시나리오 저작(챕터를 잇는 판)이 생길 때 같이 한다.**

채울 때 지킬 넷을 여기 옮겨 둔다. 특히 1번은 이쪽이 이미 두 번 물린 모양이다:

1. **`Outcome`을 명시 문자열로** (`"NextChapter"` / `"ScenarioEnd"`). `NextChapterId`가
   비었는지로 판별하지 않는다 — *"여기서 끝난다"*와 *"다음을 실수로 안 적었다"*가 같은
   모양이 된다. **선택지 문구가 비면 자동 진행이던 것과 같은 사고이고, 그것 때문에 v11에서
   `종류` 열을 만들었다.**
2. 엔딩키를 내는 노드에는 그 키의 규칙이 있어야 한다
3. 아무도 안 내는 키의 규칙은 오류다
4. 같은 키의 마지막 규칙은 조건이 없어야 한다

### 앨범 — **전역 저장이다. 슬롯이 아니다**

그쪽 §4의 구분을 받는다. "도달했나"는 슬롯 세이브가 아니라 **판을 넘는 전역 진행**에
산다. 자리를 안 만든 판단이 맞다는 확인을 받았으므로 그대로 둔다 — 엔딩이 실제로 서너 개
생긴 뒤에 스키마가 데이터에서 나오는 편이 낫다.

### ⛔ 깃발(bool 스탯) — **정해졌고, 이쪽은 끝났다. 계약에 칸 하나가 필요하다**

그쪽 `handoff.md` §6-4("bool 스탯을 켤 방법이 데이터에 없다 — 소유자가 정해야 한다")에
대한 답이다. **2026-08-19 소유자 결정: 지정(Set), bool에만.**

```json
{ "Key": "trust",      "Amount": 2 }                  ← 지금 그대로 = 더하기
{ "Key": "met_willow", "Amount": 1, "Op": "Set" }     ← 정하기 (부탁하는 칸)
```

`Op`가 없으면 `"Add"`라 **기존 파일이 한 글자도 안 바뀐다.**

저작 쪽은 다 들어갔다 — 문법(`met_willow true`/`false`) · 규칙 셋(bool에 증감 = 오류이고
대신 쓸 말을 알려 준다 · 정수에 true/false = 오류 · 한 간선이 같은 깃발을 두 번 정하면
오류) · 증명 · 편집기. 테스트 15개.

> ⚠ **그때까지 깃발을 쓰는 챕터는 내보내기가 거부된다.** `ViaNodeId`처럼 "저작에만 살고
> JSON엔 안 나간다"로 두지 않았다 — 그 칸은 빠져도 연출이 안 붙을 뿐이지만, **스탯 변화가
> 빠지면 게임 로직이 달라진다**: 깃발이 영원히 안 켜지고, 그 깃발을 보던 관문이 영원히
> 잠기며, 그쪽 도달성 증명이 이쪽과 다른 답을 낸다. 그리고 JSON에 도착한 뒤에는 아무도 못
> 본다 — 엔딩키 충돌과 같은 자리, 같은 이유다.

**증명기 통보 (약속대로)** — `ApplyChanges`가 `Set`에서 지금 값을 보지 않고 대입한다.
✅ **코퍼스 일곱 케이스에 지정이 하나도 없어 기존 답은 한 줄도 안 바뀐다.** 재확인만 하면
된다. 새 케이스(깃발이 관문을 여는 그래프)가 필요하면 이쪽에서 떠서 보낸다.

전체 규격: [`work-orders/bool-stat-orders.md`](work-orders/bool-stat-orders.md).

**아직 안 정한 것 — 깃발의 수명.** 챕터를 넘으면 되돌아가나? 지금은 챕터 안에서만 산다.
그쪽 D1(스탯 수명)과 같은 결정이다.

### 챕터 간 `cleared:` · 챕터 연쇄 증명

조건 파서에 `cleared:` 탈출구가 있고 `EpisodeCleared`로 나간다. **챕터 간은 아직 없다.**
챕터 연쇄 증명도 *"콘텐츠가 실제로 둘 이상 이어진 뒤"*라는 그쪽 판단에 동의한다 —
지금 챕터는 하나다.

---
## 7. ⚠ `ked-presentation-runtime` 브랜치 지뢰 (1차에서 이어지는 경고)

> G4를 직접 세운 지금은 덜 급하다 — 아래는 **저 저장소를 참고하러 갈 때**의 경고다.

`ked-presentation-runtime`의 현재 브랜치 `dev`(08-17)는 세이브·진행·변수 저장소를 통째로
걷어냈다. 그쪽 `work-plan.md`가 근거로 든 `ChapterEndingRule`·`EpisodeSelectionStateData`·
`VNSaveData`는 전부 **`test13`(2026-08-12에 멈춤)**의 것이다.

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

앨범 이야기에서 나온 `VNGlobalProgressData`(`unlockedEndingKeys`·`unlockedCgKeys`·
`readLineIds`)도 **`test13`의 것**이다 — 모양을 참고하는 것은 좋지만, 코드를 가져오는
자리가 아니다.

**그리고 도구 하나 — 이 저장소들에서 `find`가 없는 폴더를 보여 준다**(낡은 디렉터리 항목).
지난 세션에 두 번 속아 계약서에 틀린 내용을 적었다. **존재 확인은 `ls`·`grep`으로 한다.**

---

## 8. `.yarn` 쪽 — 대본이 하나가 됐다

진행 계층과 직접 상관은 없지만 `DialogueEntryId`가 가리키는 대상이다.

VnTool이 내던 트리오(`Story_*` / `Set_*` / `Pres_*`)가 **`Story_{이름}.yarn` 하나**로
합쳐졌다. 소유자 결정 — 런타임이 여러 레인을 읽고 동기화하고 그것을 다시 롤백에 반영하는
값을 치르지 않기로 했고("디버깅비용이 최소 10배 이상"), **합치는 쪽이 저작 도구**가 됐다.

`EpisodePlayer.StartGameAsync(string nodeName)`가 경계다 — **런타임은 챕터도 에피소드
구조도 모른다.** 어느 노드를 재생할지는 **그쪽 전이기**가 정하고 런타임은 이름만 받는다.
`EpisodeNode.DialogueEntryId`가 그 이름이고, §1의 `ViaNodeId`도 같은 종류의 이름이다.

---

## 9. 이쪽 참조 위치

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
