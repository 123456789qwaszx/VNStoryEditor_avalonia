# VnTool 구조

이 문서 하나가 구조에 대한 기준이다. 무엇이 어떻게 바뀌었는지, 각 조각이 무슨 일을 하는지,
그리고 어떤 기능을 손보려면 어느 파일을 여는지를 담는다.

제품 사용 흐름은 [README.md](README.md)에 있다.

> **⚠ 이 문서의 유효 범위 (2026-08-18에 확인)**
>
> §1~§9의 본문은 **작가·연출 계층**(대본·노드·발행·합성·출력)을 다룬다. 그 서술은 지금도
> 유효하다.
>
> 그 위에 **챕터 계층**(기획자 · 엑셀 워크북 · 에피소드 그래프)이 2026-08-11에 새로 얹혔고,
> 오늘 코드의 절반 가까이가 거기 있다(`Vn.Authoring/Chapters/` 22개 파일). 이 문서는
> 2026-08-04에 멈춰 있어서 그 계층을 한 번도 언급하지 않았다 — **[§10](#10-챕터-계층-기획자)**을
> 새로 붙여 그 구멍을 메웠다.
>
> **규격(엑셀 시트·열·전이 규칙)의 정본은 이 문서가 아니다** —
> [`docs/handoff/current-state.md`](docs/handoff/current-state.md)의 맨 위 계약 박스이고,
> 충돌하면 그쪽이 이긴다. 여기는 **코드가 어디 있는지**를 답한다.

---

## 1. 무엇이 바뀌었나 — 원본을 셋으로 나눴다

### 이전 (형식 버전 2)

```
StoryProject  ← 진실
   ├─ SetNode
   ├─ DialogueNode
   │    └─ LineBox[]   Id · 화자 · 대사 · 조건 전환   ← 전부 한 객체
   └─ PresentationNode
        └─ Presentation link ──▶ 편집 중인 DialogueNode를 실시간으로 읽음
```

두 가지가 문제였다.

**첫째, 화자와 대사의 원본이 어디인지 말할 수 없었다.** 작가는 대본을 밖에서 쓰는데
`LineBox`가 그 문장의 수정 가능한 복사본을 들고 있었다. 대본을 다시 읽으면 어느 쪽이
진실인지 아무도 답할 수 없다.

**둘째, 연출이 움직이는 대사 위에서 만들어졌다.** 연출가가 작업하는 동안 기획자가 대사를
고치면 완성된 연출표가 어느 대사에 맞는 것인지 말할 방법이 없었다. 어긋난 결과물은
겉보기에 멀쩡하다는 것이 특히 나빴다.

### 지금 (형식 버전 3)

```
작가의 평평한 대본 파일          ← 도구는 읽기만 한다
        │ 가져오기 · 재동기화 (ScriptSynchronizer)
        ▼
ScriptDocument                  ← 화자·대사의 유일한 수정 가능한 원본
   ├─ ScriptLine[]                 LineId · Revision · 은퇴 여부   (산출물 1)
   └─ ScriptLocale[]               locale별 화자·대사              (산출물 2)
        │
        │  DialogueScriptResolver가 합친다 (저장하지 않는다)
        ▼
DialogueNode                    ← 대사 논리만 소유한다
   ├─ ScriptId                     어느 대본을 읽는가
   ├─ DialogueLineExtension[]      LineId별 조건 전환
   ├─ BranchExits                  조건 갈래 출구
   └─ DefaultExit
        │  Publish (DialoguePublisher)
        ▼
DialogueResult vN               ← 불변. 그 시점의 본문까지 얼린다
        │  읽기 전용 입력
        ▼
PresentationNode                ← Source = {ResultId, Version, ContentHash}
        │  Publish (PresentationPublisher)
        ▼
PresentationResult              ← 어느 대사 결과 위에서 만들었는지 기록
        │
        └───┬─── RuntimeComposition ───┬───
            ▼                          ▼
        ResultDocumentComposer → 다섯 가지 출력
```

### 바뀐 것 한눈에

| 주제 | 형식 버전 2 | 형식 버전 3 |
|---|---|---|
| 화자·대사의 원본 | `LineBox`가 직접 소유 | `ScriptDocument`만 소유 |
| 줄 순서의 주인 | `DialogueNode.Lines` | 대본의 `ScriptLine` 순서 |
| DialogueNode가 소유하는 것 | 줄 전체 | LineId별 조건 전환과 출구 |
| 연출의 입력 | 편집 중인 DialogueNode (live link) | 발행된 `DialogueResult` (얼어붙은 스냅샷) |
| `NodeLinkKind` | `Settings`, `Presentation` | `Settings`만. 연출 관계는 계산한다 |
| 정식 출력의 입력 | 현재 프로젝트 상태 | `RuntimeComposition`이 고른 결과 두 개 |
| 버전 개념 | 없음 | `ResultId` + `Version` + `SchemaVersion` + `ContentHash` |
| 이전 형식 열기 | 관대하게 마이그레이션 | **명시적으로 거부** (§8) |

**변하지 않은 것:** 안정 식별자, `ProjectEditor` 단일 변경 통로, aggregate 스냅샷 되돌리기,
결정적 직렬화와 원자적 저장, 조건 흐름 해석기, `RenderedSegment`와 source mapping,
출력 프리셋과 Formatter 분리, `GraphProjection`, 세분된 refresh plan.

---

## 2. 어떻게 바꿨나 — 제거한 것과 그 이유

기존 코드를 많이 남기는 것을 목표로 삼지 않았다. 새 모델에서 **할 일이 없어진** 것은 지웠다.
남겨 두면 저작 모델이 두 벌이 되고, 어느 쪽이 진실인지 알 수 없게 된다.

| 제거한 것 | 지운 이유 | 대체 |
|---|---|---|
| `ProjectSession` | 상태 소유와 편집이 한 클래스에 섞여 있었다 | `AuthoringSession` + `ProjectEditor` |
| `OpenDocumentSession` | `WorkingText`라는 단일 문자열이 더는 진실이 아니다 | `StoryProject` |
| `StoryLineReplacer` / `StoryLineEditor` | 부분 문자열 교체 저장을 버렸다 | 프로젝트 전체 직렬화 |
| `StoryFileService` | 원본 인코딩·BOM·줄바꿈 보존은 *우리가 만드는* 파일에 필요 없다 | 형식을 우리가 정한다 (LF, BOM 없는 UTF-8) |
| `WorkspaceService` | 좌표만 따로 둘 이유가 사라졌다 | `StoryNode.Layout` |
| `AnalysisView` | Yarn 분석 결과 열람 화면 | `GraphEditorView` + 노드 편집기 |
| `BoxListView` | 읽기 위주의 구조 카드 | `DialogueNodeEditor` |
| `GraphView` (구) | 간선을 그리기만 했다 | `GraphEditorView` (연결 편집 가능) |
| `AppSettingsService.LoadRecentNode` / `SaveRecentNode` | 새 앱에서 아무도 부르지 않는 잔재였다 | 없음 (필요해지면 노드 Id로 다시 만든다) |

### 형식 버전 3에서 제거한 것

| 제거한 것 | 지운 이유 | 대체 |
|---|---|---|
| `LineBox` | 한 객체가 정체성·본문·조건을 모두 소유해 원본이 어디인지 말할 수 없었다 | `ScriptLine`(정체성) + `LocalizedLine`(본문) + `DialogueLineExtension`(논리) |
| `DialogueNode.Lines` | 줄 순서의 주인이 두 곳이 되면 대본 재동기화가 성립하지 않는다 | `ScriptDocument.Lines` 순서 |
| `NodeLinkKind.Presentation` | 연출이 움직이는 대사를 실시간으로 읽으면 결과가 무엇에 맞는지 말할 수 없다 | `PresentationNode.Source` (결과 스냅샷 참조) |
| `ConnectedPresentationNodeResolver` | live link가 사라졌다 | `PresentationBindingResolver`가 결과를 푼다 |
| `DialogueDocumentComposer` | 편집 중인 상태를 정식 출력처럼 합성했다 | `ResultDocumentComposer` + `WorkingDialoguePreview` |
| `LegacyProjectJson` | 관대한 마이그레이션이 새 의미로 원고를 오인할 수 있다 | 명시적 거부 (§8) |

**남긴 것:** `Vn.Core`(Yarn 분석 엔진 전체), `Vn.Cli`, 골든 픽스처, `AppSettingsService`(최근 프로젝트),
`StartupLog`, `Program`/`App` 시작 경로. 1년치 분석 엔진은 그대로 살아 있다.

### 더 이상 지원하지 않는 것

- Yarn 원문 직접 편집, 원문 서식·주석 보존
- 중첩 조건, 조건 체인과 선택 체인의 중첩 — 발행 검증이 거부한다
  (선택지 자체는 선택 전환 체인 `BeginChoice/BeginNextOption/EndChoice`로 지원한다)
- `.yarnproject` 열기 — 열려고 하면 **거부하고 알린다** (관대하게 읽으면 저장 시 원본이 덮어써진다)
- 형식 버전 1·2 프로젝트 열기 — **거부하고 이유를 알린다** (§8)

---

## 3. 프로젝트 구성과 각자의 역할

```
Vn.Authoring ── 저작 도메인. 공식 원본. 화면도 파일 대화상자도 모른다.
   ▲            (Script·Model·Results·Flow·Graph·Editing·Rendering + Chapters — §10)
   └── Vn.App    Avalonia 저작 화면

Vn.Core   ── Yarn 읽기·분석 엔진. 저작 도메인에는 관여하지 않는다.
   ▲
   ├── Vn.Cli    Yarn 검증 콘솔
   └── Vn.App    산출물 컴파일 검증 (2026-08-23 — 아래)

Ked.Presentation.Core ── 무대 상태 계산. 런타임 저장소에서 소스째 복사해 온 한 벌이다
                         (architecture-decisions H-4 — 이쪽에서 고치거나 솎지 않는다).

tests/Vn.Authoring.Tests        대본·동기화·조건 흐름·발행·합성·출력·저장·챕터 (760)
tests/Vn.App.Tests              앱 서비스·화면 — 세션·갱신 범위·챕터 화면·일의 양·산출물 검증 (336)
tests/Ked.Presentation.Core.Tests  무대 상태 계산 (344)
tests/Vn.Core.Tests             Yarn 분석과 골든 픽스처 (60)
```

테스트 수는 2026-08-23 기준 **1528개**다(Ked.Presentation.Core 344 · Vn.Core 60 · Vn.Authoring 806 · Vn.App 318).

### `Vn.Authoring/Chapters/ChapterExportService` — 화면에서 나온 정책 (2026-08-23)

챕터의 **증명 캐시와 진행 JSON 내보내기**가 `ChapterGraphView`에서 나왔다. 둘이 한
객체인 이유는 규칙 하나다 — *같은 증명을 두 번 돌리지 않는다*. 화면에 남은 것은
**언제 부르나**뿐이다.

⚠ **왜 옮겼나** — 그 정책이 3,835줄 코드비하인드에 살아서 **밖에서 보이지 않았다.**
실제로 물렸다: *"동기화는 고른 챕터만, 내보내기는 전 챕터"*라는 비대칭이 묻혀 있어,
저작 관문(⑧)을 걸려던 시도가 뒤늦게 "안 연 챕터가 전부 거부된다"를 알았다. 그 비대칭은
이제 `ExportAll`의 주석에 ⚠로 서 있다.

남은 것은 `SyncEpisodes`(156줄)다 — `AuthoringSession`을 깊이 물어 별도 판단이 필요하다
([`architecture-plan-2026-08-23.md`](../architecture-plan-2026-08-23.md) 2b).

### ⚠ `Vn.App` → `Vn.Core` — 2026-08-23에 이었다

이 문서는 오래 *"`Vn.App`은 `Vn.Core`를 참조하지 않는다. 두 세계가 갈라져 있다"*고
적어 두었다. **소유자 결정으로 그 선을 넘었다.** 무엇이 바뀌고 무엇이 안 바뀌었는지:

| | |
|---|---|
| **바뀐 것** | 셸이 이미터 산출물을 **진짜 Yarn 컴파일러로 컴파일해 본다** (`Services/YarnOutputVerification`) |
| **안 바뀐 것** | **`Vn.Authoring`은 여전히 `Vn.Core`를 모른다.** 저작 도메인이 컴파일러에 묶이지 않는다는 원래 목적은 그대로다 |

**왜 넘었나** — 이 검사는 **테스트에만** 꽂혀 있었다. 프로덕션 경로에서 컴파일러를 부르는
자리가 **0건**이라, 툴이 컴파일 안 되는 대본을 써도 유니티까지 아무도 몰랐다.
2026-08-23의 이름 갈림과 정확히 같은 모양이다 — **감지기는 있는데 정작 중요한 경로에 없다.**

**무엇을 보나** — 문법과 **전역 LineId 유일성**(계약서 C4)까지다. 어휘(미등록 커맨드)는
보지 않는다: 그 사전이 `game.definition.json`과 `game.schema.json` 둘로 갈려 있어, 여기서
한쪽을 고르면 **세 번째 판정 기준**이 생긴다. 둘을 한 어휘로 합치는 것은 별건이다.

**규율 둘** — ① 검증이 산출물을 바꾸지 않는다(그래서 `.yarnproject`를 만들지 않고
`VnProjectAnalyzer.AnalyzeFiles`로 파일 목록을 바로 건다) ② 실패해도 쓰기를 막지 않는다
(고치는 중이 곧 산출물 없음이 되면 저작을 막는다).

### 3.1 `Vn.Authoring/Script` — 화자와 대사는 여기에만 있다

| 타입 | 역할 |
|---|---|
| `ScriptDocument` | 대본 하나. **화자·대사의 유일한 수정 가능한 원본** |
| `ScriptLine` | 줄의 **정체성**: `LineId` · `Revision` · 은퇴 여부. 본문은 없다 |
| `ScriptLocale` | locale별 `LineId → LocalizedLine` 표 |
| `LocalizedLine` | 화자와 대사 한 벌. 값 타입이라 한쪽만 바뀌지 않는다 |
| `ScriptParser` | 평평한 텍스트 → 줄 + **버리지 않은 문제 목록** |
| `ScriptSynchronizer` | 다시 읽었을 때 기존 LineId를 어디까지 이을지 **계획**한다 |
| `ScriptSyncPlan` | 검토 가능한 계획. `HasConflicts`면 적용하지 않는다 |

**정체성과 본문을 나눈 이유:** 작가는 문구를 계속 고친다. 고칠 때마다 LineId가 바뀌면
연출·녹음·번역이 한꺼번에 끊어지는데 화면에는 아무 오류도 나타나지 않는다.
`Revision`만 올리면 "같은 줄인데 문구가 바뀌었다"를 말할 수 있다.

**작가의 원본 파일은 읽기 전용 가져오기 입력이다.** 도구는 그 파일에 쓰지 않는다.
다시 읽는 것은 `ScriptSynchronizer`를 지나는 명시적 동작이며, 확신할 수 없는 연결이
하나라도 있으면 아무것도 바꾸지 않는다.

**은퇴한 줄은 지우지 않는다.** 지우면 그 LineId를 가리키던 연출이 왜 고아가 되었는지
물을 수조차 없다. 은퇴한 Id는 재사용하지도 않는다.

### 3.2 `Vn.Authoring/Model` — 무엇이 있는가

| 타입 | 역할 |
|---|---|
| `StoryProject` | 대본 · StoryFile · 발행 결과 · 결과 조합을 모은 aggregate root |
| `StoryFile` | 노드를 **소유하는** 단위. Id는 이름·경로와 분리되어 있다 |
| `StoryNode` (추상) | Id, 이름, 그래프 좌표, **기본 출구**. 종류를 가리지 않는 공통부 |
| `SetNode` | 조건 정의와 변수 값. **조건이 태어나는 유일한 자리.** 실행 출구가 없다 — 공급자이지 실행 노드가 아니다(공급 노드들도 동일) |
| `DialogueNode` | `ScriptId` + `DialogueLineExtension` 목록 + `BranchExits`. **본문은 없다** |
| `DialogueLineExtension` | LineId 하나에 얹는 대사 논리. 조건·선택 전환과 변수 변경(`SetOperation`). 옵션 라벨 전환은 안정 `OptionId`(`op_`)를 가진다 — 선택지 리플레이가 위치 기반이라(계약서 C3) 순서 변경을 이 Id로 감지해 경고한다 |
| `PresentationNode` | `Source`(대사 결과 참조) + Setup 커맨드(노드 수준, Set_ 노드 본문이 된다) + `LineId`별 binding. binding은 인라인 동기화 마커(`[adv/]` 위치 + 커맨드 그룹 경계)도 가진다. **대사를 복사하지 않는다** |
| `PresentationCommandInstance` | 명령 하나. 정의 Id, 인자, 사용 여부, 메모. 순서를 보존한다 |
| `LineConditionTransition` | `BeginIf` / `BeginElseIf` / `EndIf` |
| `ConditionDefinition` | Id + 작가용 이름 + 게임이 평가할 식 |
| `CommandSupplyNode` | PresentationNode에 커맨드 범주·프리셋을 공급하는 노드. 어떤 범주 묶음을 "카메라 노드"라 부를지는 데이터다 |
| `CommandPreset` | 값이 세팅된 커맨드(`pp_`). 발행 시에는 참조가 아니라 해석된 최종 인자가 얼어붙는다 |
| `NodeLink` | 실행이 아닌 연결. `Settings`(SetNode→Dialogue), `CommandSupply`(공급→연출), `PresentationSupply`(연출→Dialogue — **발행된 결과**를 공급하며 내보내기 짝이 된다) |
| `Identifier` | `sf_` / `nd_` / `ln_` / `cd_` / `lk_` / `sc_` / `rs_` / `rc_` 생성 |

**Id와 이름을 나눈 이유:** 작가는 노드 이름을 바꾸고 줄 순서를 계속 바꾼다.
그때마다 간선과 출구가 끊어지면 저작 도구로 쓸 수 없다. 파일도 같다.

**NodeId는 파일이 아니라 프로젝트 전체에서 유일하다.** 파일을 넘나드는 출구가 있고,
노드가 파일 사이를 옮겨 다니기 때문이다.

**한 DialogueNode는 대본 하나 전체를 읽는다.** 한 대본을 여러 노드로 쪼개는 규칙은
아직 없다. 필요해지면 `DialogueNode`에 범위를 더한다 (§5.2).

### 3.3 `Vn.Authoring/Results` — 얼어붙은 것

| 타입 | 역할 |
|---|---|
| `ResultIdentity` | `ResultId` + `Version` + `SchemaVersion` + `ContentHash` |
| `DialogueResultReference` | 연출이 어느 대사 결과를 읽었는지. 세 값을 모두 기억한다 |
| `ResultHash` | 본문의 내용 해시. `IsIntact`로 파일 변조를 확인할 수 있다 |
| `DialogueResult` | 불변. 줄·조건·출구·assignment와 **그 시점의 본문**까지 얼린다 |
| `PresentationResult` | 불변. LineId별 명령과 대상 대사 결과 참조 |
| `DialoguePublisher` | 작업 상태 → 초안(검증) → 결과. 작업 중 미리 보기도 같은 초안을 쓴다 |
| `PresentationPublisher` | 같은 구조. 정확한 대사 결과가 없으면 발행을 막는다 |
| `ResultRepository` | **추가만 가능한** 결과 보관소 |
| `RuntimeComposition` | 대사 결과 하나와 연출 결과 하나를 짝지은 정식 출력 입력. **화면은 더 이상 이것을 만들지 않는다** — 저장 호환용으로 남아 있다 |
| `RuntimeCompositionResolver` | 그 짝이 실제로 호환되는지 판정한다 |
| `NodeExportResolver` | 대사 노드 하나의 내보내기 짝을 **연출 공급 연결에서** 계산한다. 연출 결과가 읽은 대사 결과(Id·버전·해시)가 곧 짝이라 구조적으로 호환된다. 공급이 없으면 Story 단독이다 |

**결과가 본문을 복사하는 것은 §4.3 위반이 아니다.** 결과는 어디에서도 수정할 수 없다.
대본을 참조만 하면 나중에 대본을 고쳤을 때 v1이 함께 바뀌고, 그러면 버전을 매길 이유가 없다.

**같은 내용을 다시 발행하면 새 버전을 만들지 않는다.** 저장 버튼을 두 번 눌렀다는 이유로
v2, v3이 쌓이면 어느 것이 의미 있는 버전인지 알 수 없다. 판정 기준은 내용 해시 하나다.

**해시에는 identity와 발행 시각이 들어가지 않는다.** 정규 표현은 저장 형식과 같은 결정적
JSON을 그대로 쓴다. 해시 전용 표현을 따로 만들면 저장 형식이 바뀔 때 둘이 조용히 어긋난다.

### 3.4 `Vn.Authoring/Flow` — 무엇을 계산하는가

| 타입 | 역할 |
|---|---|
| `DialogueScriptResolver` | 대본 + 확장 데이터를 합친 **읽기 전용 투영**을 만든다 |
| `DialogueScript` / `DialogueLine` | 그 투영. 줄 순서·본문·전환, 그리고 고아 목록 |
| `ConditionFlowResolver` | **조건 모델의 유일한 해석자.** 줄의 전환을 훑어 갈래를 만든다 |
| `DialogueFlow` | 계산 결과: `ResolvedLine[]`, `ConditionBranch[]`, `FlowProblem[]` |
| `ConditionBranch` | 갈래 하나. 여는 줄 Id, 조건, 체인 번호, 색 자리, 범위, 출구 |
| `ResolvedLine` | 줄 하나의 갈래·깊이·출구 여부, 그리고 **전환 적용 전** 갈래 |
| `ConditionChoices` | 조건 드롭다운에 무엇을 보여 줄지 |
| `AvailableConditionResolver` | **이 DialogueNode가 쓸 수 있는 조건**의 카탈로그 |
| `AvailablePresentationCommandResolver` | **이 PresentationNode가 쓸 수 있는 커맨드 범주·프리셋**. 공급 노드 미연결 시 전체 카탈로그 폴백 |
| `PresentationBindingResolver` | binding이 입력 결과의 어느 줄에 붙는지, 고아인지 |
| `NodeConnections` | 노드의 실행 출력 포트와 프로젝트 전체 간선 |
| `MiniStageFold` | 무대 프리뷰의 순수 폴드 — 선택 라인까지 접은 배경·슬롯·별칭·대사창 상태와 **미반영 커맨드 목록**(§3.10) |

`ResolvedLine`에 "전환 적용 전 갈래"(`PrecedingBranch`)가 함께 있는 이유는,
드롭다운의 의미가 **바로 앞 줄까지의 상태**로 정해지기 때문이다.

**합성 방향은 언제나 대본 → 화면이다.** 화면이 본 것을 대본으로 되돌려 쓰지 않는다.

### 3.5 `Vn.Authoring/Graph` — 그래프에 무엇을 그릴지

| 타입 | 역할 |
|---|---|
| `GraphProjectionBuilder` | 프로젝트·펼침 상태·`GraphFilter`(노드 종류 토글)로부터 **화면에 그릴 것**을 계산한다. 필터는 화면이 아니라 여기서 거른다 — 간선의 한쪽 끝이 숨으면 간선도 숨는다 |
| `GraphFilter` | 노드 종류·결과 간선 토글. `FlowOnly`는 DialogueNode만 남기는 흐름 보기다 |
| `ExpandedNodeProjection` | 펼친 파일의 실제 노드 카드. `Badge`에 대본·발행 버전이 붙는다 |
| `CollapsedFileProjection` | 접힌 파일 하나. 소유 노드가 `CollapsedNodeEntry` 행으로 들어간다 |
| `GraphConnectionProjection` | 간선 하나와 그 라벨·색 자리 |
| `GraphEndpointProjection` | 간선의 끝이 실제 포트인지 프록시의 몇 번째 행인지 |
| `OrthogonalEdgeRouter` | 두 끝점 사이 ㄱ자 경로 |

간선의 종류는 네 가지다.

```
ExecutionDefault / ExecutionBranch   실행 흐름     (출구가 소유)
Settings                             기능 공급     (NodeLink가 소유)
ResultSnapshot                       결과 입력     (아무도 소유하지 않는다 — 계산이다)
```

`ResultSnapshot` 간선은 저장되지 않는다. `PresentationNode.Source`가 가리키는 결과를
낳은 DialogueNode를 찾아 매번 그린다. 방향은 **대사 → 연출**이다. 화살표 방향이
의존 방향과 같아야 누가 누구를 읽는지 그림만 보고 알 수 있다.

**모든 발행 버전을 카드로 펼치지 않는다.** 몇 번만 발행해도 그래프가 결과로 덮인다.
현재 상태는 카드의 `Badge`와 포트 라벨(`결과 v3`)로만 보여 준다.

**연결의 소스와 대상은 언제나 실제 NodeId다.** 파일을 접어도 대상이 FileId로 바뀌지
않는다. 바뀌는 것은 그 끝을 화면 어디에 붙일지뿐이다.

### 3.6 `Vn.Authoring/Editing` — 어떻게 바꾸는가

| 파일 | 역할 |
|---|---|
| `ProjectEditor.cs` | 노드·파일·조건·출구·연출 명령 |
| `ProjectEditor.Scripts.cs` | 대본. **화자·대사를 바꾸는 코드는 여기에만 있다** |
| `ProjectEditor.Results.cs` | 발행과 결과 조합 |
| `ProjectChangedEventArgs` | `Structure` / `Content` / `DialogueContent` / `PresentationContent` / `ConditionDefinition` / `Connections` / `Results` / `NodeMetadata` / `Layout` |

**모델을 바꾸는 유일한 통로다.** 되돌리기와 알림을 함께 책임진다.

**변경 종류는 영향 범위를 말한다.** 조건 이름을 고치는 일과 조건을 추가하는 일은
화면에 주는 영향이 다르다. 같은 신호를 보내면 화면은 최악의 경우를 가정해 편집 중인
컨트롤까지 다시 만들고, 작가는 입력 도중 포커스를 잃는다.

**시각과 LineId 생성기는 주입한다.** 결과 해시와 동기화 계획을 테스트가 읽을 수 있어야 한다.

### 3.7 `Vn.Authoring/Rendering` — 평평한 문서로 펼치기

| 타입 | 역할 |
|---|---|
| `ResultDocumentComposer` | **정식 출력의 유일한 입구.** 결과 → `RenderedSegment` 목록 |
| `WorkingDialoguePreview` | 발행 전 상태를 같은 합성기로 펼친다. `IsPublished`가 false다 |
| `RenderedDocument` / `RenderedSegment` | 문서 한 벌과 그 조각. 종류·레이어·들여쓰기 |
| `RenderSourceReference` | 이 조각이 어느 결과·노드·줄·조건·명령에서 나왔는지 |
| `DocumentOutputOptions` | 어떤 레이어와 연출 category를 담을지. 다섯 프리셋이 여기 있다 |
| `YarnPreviewFormatter` | Runtime Full의 Yarn 스타일 문자열 |
| `YarnSyntax` | Yarn 표기 조립 규칙의 단일 구현. Preview와 이미터가 공유한다 |
| `YarnBundleEmitter` | 합성 하나 → 런타임 재생용 .yarn 트리오(Story/Set/Pres) 텍스트와 파일. 변수 선언은 폴더당 하나뿐인 `declarations.yarn`에 합집합으로 낸다(같은 변수의 초기값이 합성 간에 다르면 거부). 사양은 `docs/runtime-contract.md` |
| `DocumentPreviewFormatter` | 시나리오·녹음·번역·연출 지시서 문자열 |
| `CsvBundleExporter` | 합성 하나 → CSV 3종(번역·녹음 / 기획 검수 / 연출 테이블). **UTF-8 BOM 포함**(엑셀 한글 호환 — .yarn의 no-BOM과 의도적으로 다름), CRLF, RFC 4180 |
| `ILocalizedLineProvider` | LineId로 번역문을 공급하는 바깥 경계 |
| `ConnectedSetNodeResolver` | 이 Dialogue에 연결된 SetNode와 그 순서를 한 번만 계산 |

**미리 보기와 정식 출력이 같은 코드를 지난다.** `WorkingDialoguePreview`는 발행 초안을
만들어 버전 0짜리 작업 중 결과로 감싼 다음 `ResultDocumentComposer`에 그대로 넘긴다.
미리 보기 전용 합성기를 따로 두면 화면에서 본 것과 발행된 것의 차이를 아무도 찾지 못한다.

**합성이지 파싱이 아니다.** 방향은 언제나 결과 → 문서다. Preview 문자열을 다시 읽어
모델로 되돌리지 않는다. 되파싱을 허용하는 순간 진실이 두 곳이 된다.

**출력 프리셋은 옵션일 뿐 저장 대상이 아니다.** `StoryProject`, 스냅샷, 되돌리기 어디에도
들어가지 않는다. 필터로 빠진 Segment가 있어도 남은 Segment의 원본 참조는 그대로다.

Segment는 문자열만 들고 있지 않다. 어디서 나왔는지를 함께 들고 있어야 미리보기 줄을
눌러 원본으로 갈 수 있고, 녹음 대본과 번역본이 같은 LineId를 공유할 수 있다.

### 3.8 `Vn.Authoring/Serialization`, `Definition`

| 타입 | 역할 |
|---|---|
| `ProjectManifestJson` | `*.vnproject.json` — 목차, 조건 공급 link, 결과 조합 |
| `ScriptDocumentJson` | `*.vnscript.json` — 대본 하나. 줄 정체성과 locale별 본문 |
| `StoryFileJson` | `*.vnstory.json` — StoryFile 하나와 그것이 소유한 노드 |
| `StoryNodeJson` | 노드 직렬화 규칙의 **단일 구현**. manifest도 스냅샷도 여기를 지난다 |
| `ResultStoreJson` | `results.vnresults.json` — 발행 결과 전체. **본문 표현이 해시 입력이다** |
| `ProjectStore` | 경로 해석, 전체 읽기·쓰기, 임시 파일 교체 |
| `ProjectSnapshotCodec` | 되돌리기와 저장 여부 비교용 aggregate 문자열 |
| `JsonSupport` | 공통 읽기 도우미와 형식 검증 |
| `GameDefinition` | 게임별 변수·이벤트·연출 범주·명령 후보. 없으면 빈 정의로 계속 |
| `PresentationCommandCatalog` | 게임별 연출 명령 정의. 범주는 문자열 Id, 파라미터는 **순서 있는 목록**(순서가 곧 Yarn 포지셔널 인자 순서). 정의 파일이 없으면 내장 기본 카탈로그(`docs/game.definition.json`을 리소스로 링크한 런타임 교차 검증본, 201 커맨드) |

디스크 배치:

```
project.vnproject.json      목차 · Settings link · RuntimeComposition
script/<scriptId>.vnscript.json    줄 정체성 + locale별 화자·대사
story/<fileId>.vnstory.json        노드 · LineId별 조건 · 출구
results.vnresults.json      발행된 불변 결과
```

**결과를 파일 하나에 모은 이유:** 결과는 불변이고 추가만 되므로 한 파일을 원자적으로
교체하는 편이 단순하고, 부분적으로만 갱신된 결과 집합이 디스크에 남는 상태를 아예 만들지
않는다. 파일이 커져 곤란해지면 계보별 디렉터리로 나눈다. 그 경계는 `ResultStoreJson` 하나다.

**연출 명령을 C# enum에 박지 않는다.** 게임마다 명령과 프리셋이 다르다. 코드가 특정
게임의 명령 이름을 알기 시작하면 그 게임 전용 도구가 된다. 변수·이벤트와 같은 이유다.
편집 범주도 마찬가지다 — "카메라 노드"가 어떤 범주 묶음인지는 `presentationCommandCategories`
데이터가 정하고, 코드는 문자열 Id로만 다룬다. 기본 프리셋도 범주를 열거하지 않는다
(null = 전체 포함). 특정 범주만 고르는 것은 사용자 정의 옵션의 몫이다.

**디스크 배치와 되돌리기 형식을 나눈 이유:** 되돌리기 한 번에 여러 실제 파일을 다시
조립할 이유가 없다. 파일 구조가 앞으로 또 바뀌어도 편집 기록은 `ProjectSnapshotCodec`의
aggregate 하나로 독립되어 있어야 한다.

**저장 순서:** 대본·StoryFile·결과를 먼저 임시 파일로 교체하고 manifest를 마지막에 바꾼다.
중간에 멈춰도 manifest가 가리키는 파일 집합은 언제나 존재한다.

### 3.9 `Vn.App` — 화면

| 타입 | 역할 |
|---|---|
| `AuthoringSession` | 열린 프로젝트 경로, 저장 여부, 선택 상태, `ActiveFileId`와 `ExpandedFileIds`, 프리뷰 에셋 인덱스·비트맵 캐시. 편집은 도메인이 한다 |
| `GraphEditorView` | 노드 추가·이동·선택, 포트 드래그 연결, 간선 선택·삭제 |
| `DialogueNodeEditor` | 대본 선택·가져오기, 줄 카드, 조건 드롭다운, 발행 탭, 출력 미리 보기 |
| `SetNodeEditor` | 조건과 변수 값 정의 |
| `PresentationNodeEditor` | 입력 결과 선택, 읽기 전용 대사 미러, LineId별 연출 명령, 발행 |
| `MiniStagePreview` | 대사·연출 편집기가 공유하는 무대 프리뷰 패널(§3.10). 에셋 루트 설정과 명시적 새로 고침도 여기 |
| `PreviewImageCache<T>` | 경로 키(Ordinal) 비트맵 캐시. 로더 주입식 — 무효화는 명시적 `Clear`뿐 |
| `BranchPalette` | 갈래 색 표. 데이터가 아니라 표시 수단 |
| `MainWindow` | 도구 모음, 열기·저장, 세션을 화면들에 물려준다 |
| `ProjectRefreshPlanner` | 변경 알림 하나가 어느 화면을 다시 만들게 할지 정하는 유일한 자리 |
| `AppSettingsService` | 최근 프로젝트 기억 |
| `StartupLog` | `%LOCALAPPDATA%\VnTool\logs\startup-error.log` |

**발행된 것에는 수정 UI가 없다.** 연출 편집기의 대사 미러는 `TextBlock`이지 `TextBox`가
아니다. 발행 결과 목록도 읽기 전용 카드다.

### 3.10 프리뷰 계층 — 무대 프리뷰 (Phase 2a-v1)

"무슨 배경에서 누가 말하는가"만 답하는 읽기 전용 소비자다. 장면 재현이 아니라 정보가
목표라서 좌표·크기·이펙트·시간은 다루지 않는다(그건 2b 정지 프레임 렌더러의 일).
이미터·발행·계약서에는 손대지 않는다.

책임은 세 겹으로 나뉜다.

```
Vn.Authoring/Assets   에셋 해석 — 파일 경로 수준까지만. 비트맵을 모른다
   PortraitManifest      런타임 U12-v1 덤프(formatVersion 1, 불일치는 명시 거부)
   PortraitKey           키 정규화·폴백 — 런타임 PortraitResolver 규칙 이식
   PreviewAssetLibrary   배경 파일명=spriteKey(Ordinal)·초상화 키 인덱스, Problems
Vn.Authoring/Flow     MiniStageFold — 순수 함수 폴드. 계산은 저장하지 않는다
Vn.App                화면과 비트맵 — MiniStagePreview 패널, PreviewImageCache
```

지켜야 하는 것:

- **폴드는 Flow의 계산이다.** 접은 상태를 어디에도 저장하지 않는다. 입력은 발행
  Freeze와 같은 길로 해석된 커맨드(프리셋은 이미 최종 값)이므로 프리뷰용 두 번째
  해석 규칙이 없다. 대사 편집기의 공급 짝도 내보내기와 같은 `NodeExportResolver`로 찾는다.
- **에셋은 App 경계다.** Authoring은 경로와 해석 결과(정확/폴백/누락)까지만 알고,
  Avalonia 비트맵·캐시·화면은 Vn.App에 있다. 파일 변경 감지는 없다 — 새로 고침은
  명시적 동작 하나뿐이다.
- **미처리 가시화 규칙.** 폴드가 인식하지 못한(또는 적용하지 못한) 커맨드는 조용히
  버리지 않고 커맨드명·라인째로 `Unhandled`에 남겨 "반영 안 된 연출 N" 뱃지로 보인다.
  누락 에셋도 키 문자열이 보이는 플레이스홀더와 `Problems`로 남는다. 이 목록이
  2b 확장의 백로그다 — **조용히 버리는 코드는 리뷰 반려 사유다.**
- v1 폴드는 갈래를 가정하지 않는다. 조건·선택 갈래도 문서 순서대로 전부 접고,
  그 근사가 쓰였음을 표시 하나로 알린다. 갈래 인식 폴드는 2b에서.

2a-v2(W17–W20)에서 이 계층 위에 얹힌 것:

```
Vn.Authoring/Assets     AssetExplorerModel — 탐색기 트리(배경=폴더 구조, 초상화=키 구조) 순수 계산
Vn.Authoring/Definition CommandText — 병기 텍스트 조립·텍스트 입력 파싱의 단일 구현(이미터와 같은 규칙)
                        ArgumentTokenCandidates — 파라미터 type별 후보 토큰(제약이 아니라 제안)
Vn.Authoring/Editing    PresentationStageActions — 직접 조작→편집 변환(같은 종류는 수정, 시퀀스는 개별 커맨드)
Vn.App                  AssetExplorerView(드래그 소스) · StageSceneComposer(기준 해상도 배치 계산, 순수)
                        StageSceneView(도킹·분리 창 공용 렌더러+직접 조작) · StagePreviewWindow(따라가기·라인 이동)
```

**편집 경로 수렴 원칙 — 세 입력 경로, 한 통로.** 연출 커맨드를 만드는 길은 셋이다:
갤러리/직접 추가, 텍스트 입력(`CommandText.Parse`), 프리뷰 직접 조작(클릭·드래그 →
`PresentationStageActions`). 어느 길이든 결과는 같은 `ProjectEditor` 편집 명령으로
수렴한다 — 프리뷰용 별도 쓰기 경로는 없고, 되돌리기 한 번이 조작 하나를 원복하며
(배치 추가·병합 갱신이 한 mutate다), 발행→이미터 출력은 경로와 무관하게 같다
(`InputPathEquivalenceTests`가 못박는다). 텍스트가 진실이므로 어떤 방식으로 만들었든
커맨드에는 `<<이름 인자…>>` 텍스트가 병기된다.

---

## 4. 핵심 설계 판단

### 4.0 소유권 — 일곱 가지 질문에 대한 답

| 질문 | 답 |
|---|---|
| 화자와 대사의 권위 있는 원본은 어디인가 | `ScriptDocument`의 `ScriptLocale.Entries`. 바꾸는 코드는 `ProjectEditor.Scripts.cs`에만 있다 |
| LineId는 대본 수정 후 어떻게 유지되는가 | `ScriptSynchronizer`가 유일 문장을 닻으로 삼아 잇는다. 애매하면 멈춘다 (§4.4) |
| 작업 데이터와 발행 결과는 어떻게 구분되는가 | `DialogueNode`는 가변, `DialogueResult`는 불변. 후자는 `ResultIdentity`를 가진다 |
| PresentationNode는 어떤 버전을 읽는가 | `PresentationNode.Source` = {ResultId, Version, ContentHash} |
| 정식 출력을 다시 만들 때 같은 입력을 찾을 수 있는가 | `RuntimeComposition`이 두 결과를 버전까지 지정해 저장한다 |
| 이전 프로젝트는 어떻게 보호되는가 | 형식 버전 2 이하는 읽지 않고 거부한다 (§8) |
| capability node와 실시간 프리뷰는 어디에 붙는가 | §5.2 |

### 4.1 조건은 반복 저장하지 않고 전환만 저장한다

줄마다 "지금 어떤 조건 안인지"를 적으면 같은 사실이 여러 줄에 흩어진다.
한 줄만 고쳤을 때 서로 어긋나고, 그 어긋남을 발견할 방법이 없다.

```
Line 0                                  Line 0
Line 1  BeginIf(호감)          if 호감 { Line 1
Line 2                                    Line 2 }
Line 3  BeginElseIf(신뢰)   elseif 신뢰 { Line 3
Line 4                                    Line 4 }
Line 5  EndIf                           Line 5
Line 6                                  Line 6
```

- `BeginIf` 줄은 자기가 연 갈래 **안**에 있고, `EndIf` 줄은 이미 **바깥**이다.
- `BeginElseIf`는 중첩이 아니라 같은 체인의 형제다. **깊이가 늘지 않는다.**
- 첫 버전의 깊이는 0 또는 1뿐이다.

### 4.2 조건 갈래 출구는 갈래를 *여는 줄*에 매단다

마지막 줄에 매달면 줄을 하나 넣는 순간 출구가 갈래 중간에 파묻히고,
그 아래 대사가 실행되는 모순이 생긴다.

여는 줄은 갈래 그 자체이므로 줄을 아무리 넣고 옮겨도 출구는 그 갈래의 것으로 남고,
화면에는 **언제나 갈래의 현재 마지막 줄**에 표시된다. "출구를 새 마지막 줄로 자동 이동"
정책이 별도 코드 없이 성립한다.

**전환을 지우면** `ProjectEditor.PruneBranchExits`가 출구를 함께 버린다. 그 줄은 여전히
대본에 있는데 더 이상 갈래를 열지 않으므로, 남겨 두면 어느 화면에서도 지울 수 없는 간선이
그래프에 생긴다.

**대본에서 줄이 빠지면** 출구를 버리지 않는다. 그것은 **고아**이지 쓰레기가 아니다.
대본을 되돌리면 살아나야 하고, 그때까지는 `FlowProblemKind.OrphanedLineExtension`으로 보인다.
이 둘의 차이가 §4.2에서 가장 자주 헷갈리는 지점이다.

### 4.3 그래프 포트는 저장하지 않고 계산한다

포트와 간선은 조건 전환에서 나온다. 그래서

- 대사 화면에서 `elseif`를 추가하면 → 그래프에 포트가 하나 는다
- 그래프에서 포트를 끌어 이으면 → 대사 화면의 출구 표시가 바뀐다

**두 화면 사이에 동기화 코드가 한 줄도 없다.** 같은 것을 계산해서 볼 뿐이라 어긋날 자리가 없다.
모든 변경은 `ProjectEditor.SetExitTarget` 하나를 지난다.

### 4.4 대본 재동기화는 확신할 수 없으면 멈춘다

`ScriptSynchronizer`의 절차다.

```
1. 양쪽에서 정확히 한 번씩만 나타나는 문장 → 닻
2. 닻 중 순서가 유지되는 최장 부분열 → Unchanged, 나머지 닻 → Moved
3. 순서가 유지된 닻 사이 구간마다:
     같은 문장끼리 앞에서부터 짝짓기        → Unchanged
     양쪽에 하나씩만 남음                   → Modified (LineId 유지, Revision +1)
     한쪽이 빔                              → Inserted / Deleted
     양쪽에 서로 다른 문장이 둘 이상 남음   → Ambiguous
```

**`Ambiguous`가 하나라도 있으면 계획 전체를 적용하지 않는다.** 도구가 대신 고르는 순간
작가가 쓰지 않은 연출이 다른 대사에 붙는다. 그 어긋남은 최종 출력에서야 드러난다.

**충돌은 저장되지 않는다.** 계획은 일시적인 값이고, 충돌이 있으면 적용되지 않으므로
"해결되지 않은 동기화 충돌을 안은 프로젝트"라는 상태가 구조적으로 존재할 수 없다.
그래서 발행 검증에도 그 항목이 없다.

**은퇴한 줄은 후보에서 제외한다.** 사라졌던 문장이 다시 나타나면 옛 Id를 되살리지 않고
새 Id를 발급한다. 그 사이에 그 Id를 가리키던 연출이 무엇을 뜻하는지 알 수 없기 때문이다.

### 4.5 결과는 불변이고, 호환되지 않으면 합성하지 않는다

```
DialogueResult        PresentationResult
       ▲                      ▲
       └──── RuntimeComposition ────┘
```

대사 결과는 연출 결과를 소유하지 않는다. 소유하면 서로를 가리키는 순환이 생기고,
어느 쪽을 먼저 발행해야 하는지 답할 수 없다. 둘을 잇는 자리는 `RuntimeComposition` 하나다.

합성 전에 세 값을 모두 확인한다.

```
Presentation.Source.ResultId    == Dialogue.Identity.ResultId
Presentation.Source.Version     == Dialogue.Identity.Version
Presentation.Source.ContentHash == Dialogue.Identity.ContentHash
```

하나라도 어긋나면 `ResultDocumentComposer`가 문서를 만들지 않고 거부한다. 어긋난 조합은
`ProjectEditor.AddComposition`에서 저장조차 되지 않는다. 저장된 뒤에 진단하면 파일에 이미
깨진 참조가 들어간 뒤이고, 그것을 고칠 화면이 없다.

**연출이 붙지 않은 LineId는 정상이다.** 대사만 출력된다. 반대로 대상 결과에 없는 LineId에
연출이 붙어 있으면 `OrphanBinding`으로 알리되 합성은 막지 않고, 그 데이터도 지우지 않는다.

---

## 5. 기능을 바꾸려면 어디를 여는가

### 5.1 빠른 조회

**저작 모델**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 줄에 대사 논리 추가(선택지·변수 변경·이벤트) | `Model/DialogueLineExtension.cs` → §5.2의 순서 |
| 줄에 원고 정보 추가(태그·메모) | `Script/ScriptDocument.cs` → `Serialization/ScriptDocumentJson.cs` |
| 노드 종류 추가(연출 노드 등) | `Model/StoryNode.cs` → `StoryNodeJson`의 `kind` 분기 → `Flow/NodeConnections.cs` → 화면 |
| 설정 노드가 담는 것 | `Model/StoryNode.cs`의 `SetNode` |
| 조건의 이름·식 구조 | `Model/StoryNode.cs`의 `ConditionDefinition` |
| 식별자 형식 | `Model/Identifier.cs` |
| 시작 노드 규칙 | `Model/StoryProject.cs`, `ProjectEditor.AddNode` |
| 노드가 어느 파일에 속하는지 | `Model/StoryFile.cs`, `StoryProject.FindFileContainingNode` |
| 새 노드가 어느 파일에 들어가는지 | `App/Services/AuthoringSession.cs`의 `ActiveFileId` |
| 그래프에 어떤 파일을 펼칠지 | 같은 파일의 `ExpandedFileIds` / `SetFileExpanded` |
| 파일 추가·삭제·노드 이동 | `Editing/ProjectEditor.cs`의 `AddFile` / `RemoveFile` / `MoveNodeToFile` |

**조건과 갈래**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| if/elseif/endif의 의미 | `Flow/ConditionFlowResolver.cs` ← **이 파일이 조건 모델 그 자체다** |
| 중첩 조건·깊이 2 이상 지원 | 같은 파일. 지금은 문제로 알리고 같은 깊이로 다룬다 |
| `else` 갈래 추가 | `Model/DialogueLineExtension.cs`의 `ConditionTransitionKind` → 위 해석자 → `Flow/ConditionChoices.cs` |
| 드롭다운에 무엇을 보여 줄지 | `Flow/ConditionChoices.cs` |
| 어떤 조건을 쓸 수 있는지 | `Flow/AvailableConditionResolver.cs` — 연결된 SetNode + 게임 전역 |
| 갈래 색 | 자리 계산은 `Flow/DialogueFlow.cs`의 `PaletteIndex`, 실제 색은 `App/Views/BranchPalette.cs` |
| 잘못된 구조를 알리는 방식 | `Flow/DialogueFlow.cs`의 `FlowProblemKind` |

**출구와 연결**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 출구를 어디에 저장하는가 | `Model/StoryNode.cs`의 `DialogueNode.BranchExits` |
| 실행이 아닌 연결을 저장하는 곳 | `Model/NodeLink.cs`, manifest의 `links` |
| 조건 공급 연결 만들기·끊기 | `Editing/ProjectEditor.cs`의 `AddSettingsLink` / `RemoveLink` / `SetLinkEnabled` |
| 연출 명령 편집 | 같은 파일의 `AddPresentationCommand` / `MovePresentationCommand` / `RemovePresentationCommand` |
| 연출 명령 목록에 항목 추가 | `game.definition.json`의 `presentationCommands` — 코드가 아니다 |
| 포트를 만드는 규칙 | `Flow/NodeConnections.cs`의 `PortsOf` (실행) + `Graph/GraphProjectionBuilder.BuildPorts` (공급·결과) |
| 간선 라벨 문구 | `Flow/NodeConnections.cs`의 `LabelFor` |
| 연결·해제 동작 | `Editing/ProjectEditor.cs`의 `SetExitTarget` |
| 포트를 끌어 잇는 조작 | `App/Views/GraphEditorView.axaml.cs`의 `OnPortPressed` / `OnCanvasPointerReleased` |
| 간선 선택·삭제 | 같은 파일의 `SelectEdge` / `DeleteSelectedEdge` |
| 노드 카드 크기·포트 좌표 | 같은 파일 위쪽 상수와 `PortAnchor` / `InputAnchor` |
| 그래프에 무엇이 나타나는지 | `Graph/GraphProjectionBuilder.cs` — 화면이 아니라 여기가 정한다 |
| 카드 뱃지 문구 | 같은 파일의 `BadgeFor` |
| 접힌 파일 프록시의 모양 | `Graph/GraphProjection.cs`의 `CollapsedFileProjection` |
| 간선이 꺾이는 모양 | `Graph/OrthogonalEdgeRouter.cs` |

**대본과 LineId**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 대본 문법을 넓히기 | `Script/ScriptParser.cs` — 규칙 전체가 이 파일의 XML 주석에 있다 |
| 재동기화 매칭 규칙 | `Script/ScriptSynchronizer.cs` — **애매하면 멈춘다는 규칙을 깨지 말 것** |
| 대본에 항목 추가(태그·메모 등) | `Script/ScriptDocument.cs` → `Serialization/ScriptDocumentJson.cs` |
| 다른 locale 추가 | `ScriptDocument.Locales`. 모델은 이미 준비되어 있고 화면만 없다 |
| 화자·대사를 바꾸는 명령 | `Editing/ProjectEditor.Scripts.cs`의 `SetScriptLineText` — **여기 하나뿐** |
| 대본 가져오기 조작 | `App/Views/DialogueNodeEditor.axaml.cs`의 `OnImportScriptClick` |
| 대본과 노드를 합치는 규칙 | `Flow/DialogueScriptResolver.cs` |

**발행과 합성**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 결과에 항목 추가 | `Results/DialogueResult.cs` → `Serialization/ResultStoreJson.cs`의 `WriteBody` (**해시 입력이다**) |
| 발행 전 검증 규칙 | `Results/DialoguePublisher.cs`의 `AddFlowProblems` / `Results/PresentationPublisher.cs` |
| 중복 발행 정책 | 같은 두 파일의 `Publish` — 내용 해시가 같으면 기존 버전을 돌려준다 |
| 결과 스키마 버전 올리기 | `DialogueResult.CurrentSchemaVersion` / `PresentationResult.CurrentSchemaVersion` |
| 호환성 판정 | `Results/RuntimeComposition.cs`의 `RuntimeCompositionResolver.CheckCompatibility` |
| 결과 파일 배치 | `Serialization/ResultStoreJson.cs` — 나누려면 여기만 고친다 |
| 정식 출력에 무엇이 나오는지 | `Rendering/ResultDocumentComposer.cs` |
| 작업 중 미리 보기 | `Rendering/WorkingDialoguePreview.cs` |
| 출력 프리셋 추가·수정 | `Rendering/DocumentOutputOptions.cs`의 `OutputPresetCatalog` |
| Preview 문자열 모양 | `Rendering/YarnPreviewFormatter.cs`, `Rendering/DocumentPreviewFormatter.cs` |

**편집과 되돌리기**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 새 편집 명령 추가 | `Editing/ProjectEditor.cs` — **모델을 바꾸는 코드는 전부 여기에만** |
| 되돌리기 방식 | 같은 파일. `ProjectSnapshotCodec` 스냅샷을 쌓는다 |
| 어떤 편집이 어떤 종류의 변경인지 | `Editing/ProjectChangedEventArgs.cs` |
| 그 변경이 어느 화면을 다시 만들게 할지 | `App/Services/ProjectRefreshPlanner.cs` |
| 새 노드가 파일 어디에 붙는지 | `ProjectEditor.AddNode` — 지정한 파일의 맨 뒤 |

**저장 형식**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 노드에 항목 추가·삭제 | `Serialization/StoryNodeJson.cs` — 노드 직렬화는 여기 하나뿐 |
| 프로젝트 메타데이터 추가 | `Serialization/ProjectManifestJson.cs` |
| 파일 배치·경로 규칙 | `Serialization/ProjectStore.cs` |
| 어떤 파일을 열지 말지 | `ProjectStore`와 `JsonSupport.ValidateProject` |
| 되돌리기·저장 여부 비교 형식 | `Serialization/ProjectSnapshotCodec.cs` |
| 이전 형식 거부 문구 | `Serialization/ProjectManifestJson.Read` |
| 형식 버전 올리기 | `Model/StoryProject.cs`의 `CurrentFormatVersion` + 위 읽기 경로들 |
| 게임별 정의 스키마 | `Definition/GameDefinition.cs` |

**화면**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 도구 모음, 열기·저장, 창 제목 | `App/MainWindow.axaml{,.cs}` |
| 그래프 화면 | `App/Views/GraphEditorView.axaml{,.cs}` |
| 대사 카드의 내용과 배치 | `App/Views/DialogueNodeEditor.axaml{,.cs}` |
| 설정 노드 화면 | `App/Views/SetNodeEditor.axaml{,.cs}` |
| 무대 조절창(탭·직접 조작) | `App/Views/StageSceneView.cs`의 `BuildStagePopover` — 탭 하나가 함수 하나(`BuildQuickTab`·`BuildBackgroundTab`·`BuildSlotTab`·`BuildCharacterTab`·`BuildAudioTab`) |
| [★ 자주 쓰는] 기본 칩 목록 | `Model/StageQuickCommand.cs`의 `StageQuickCommands.Default` — 화면이 아니라 여기가 정한다 |
| 수치 조절 위젯을 새 대상에 붙이기 | `App/Views/StageSceneView.cs`의 `ArgumentSink` — 슬라이더·선택기는 한 벌이고 "어디에 쓰나"만 갈아 끼운다 |
| 열린 프로젝트·저장 여부·선택 | `App/Services/AuthoringSession.cs` |
| 최근 프로젝트 기억 | `App/Services/AppSettingsService.cs` |
| 시작 오류 로그 | `App/Services/StartupLog.cs` |

**Yarn 분석 (저작과 분리된 세계)**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| Yarn 진단·컴파일·구조 읽기 | `src/Vn.Core` |
| 회귀용 `--format list` 출력 | `Vn.Core/Reporting/ListReportFormatter.cs` |
| 골든 비교 | `build-and-run.ps1`, `tests/Vn.Core.Tests/GoldenText.cs` |

### 5.2 여러 파일에 걸치는 작업의 순서

**줄에 새 대사 논리를 붙인다 (예: 선택지, 변수 변경, 인라인 이벤트)**

붙는 자리는 `DialogueLineExtension`이다. 대본이 아니다. 대본은 작가가 쓴 문장만 담는다.

1. `Model/DialogueLineExtension.cs` — 속성 추가, `Clone()`과 `IsEmpty`에도 반영
2. `Serialization/StoryNodeJson.cs`의 `WriteDialogueNode` / `ReadDialogueNode` — 양쪽
3. `Editing/ProjectEditor.cs` — 편집 명령. 대본에는 절대 쓰지 않는다
4. `Flow/DialogueScriptResolver.cs` — `DialogueLine`에 실어 화면으로 보낸다
5. `Results/DialogueResult.cs` + `Serialization/ResultStoreJson.cs`의 `WriteBody`
   — **결과에 넣으면 해시가 바뀐다.** 스키마 버전을 함께 올린다
6. `Rendering/ResultDocumentComposer.cs` — Segment로 펼친다
7. `App/Views/DialogueNodeEditor.axaml.cs`의 `BuildCard` — UI
8. 테스트: `PublishTests`(결과에 실리는가) + `ResultDocumentComposerTests`(출력에 나오는가)

**capability node를 추가한다 (예: Camera 공급 노드)**

지금은 연출 명령 카탈로그가 `game.definition.json` 전역이다. 노드를 연결해야 특정 명령군이
보이게 하려면:

1. `Model/StoryNode.cs` — 새 파생 클래스와 `Clone()`
2. `Model/NodeLink.cs`의 `NodeLinkKind`에 종류 추가
3. `Flow/`에 `AvailableConditionResolver`와 같은 모양의 범위 계산기
   — 그 계산기가 `PresentationCommandCatalog.For`를 대신한다
4. `Serialization/StoryNodeJson.cs`, `ProjectManifestJson.WriteLink`
5. `Graph/GraphProjectionBuilder.cs` — 공급 포트와 간선
6. `App/Views/`에 편집기 + `MainWindow.ShowSelectedNode` 분기

**결과 구조의 경계는 이미 열려 있다.** `DialogueResult`는 발행 시점의 조건 이름·식까지
얼리므로, 새 capability가 만들어 내는 데이터도 같은 방식으로 얼리면 된다.

**조건 모델을 넓힌다 (예: else, 중첩)**

1. `Model/DialogueLineExtension.cs`의 `ConditionTransitionKind`에 종류 추가
2. `Flow/ConditionFlowResolver.cs`의 상태 기계 수정
   — 깊이가 늘어나면 `ResolvedLine.Depth`의 뜻이 바뀌므로 화면 들여쓰기도 함께 본다
3. `Flow/ConditionChoices.cs` — 드롭다운 규칙
4. `Serialization/ResultStoreJson.cs`의 `KindName` / `ParseKind` — **저장 문자열은 여기 하나뿐**
5. `tests/Vn.Authoring.Tests/ConditionFlowTests.cs` — **예시부터** 추가

**실시간 플레이어를 만든다 (다음 단계)**

`RuntimeComposition` 위에 올린다. 필요한 것은 이미 다 있다.

1. `RuntimeCompositionResolver.Resolve`로 결과 두 개를 얻는다
2. `DialogueResult.Lines`를 그대로 순회한다 — 순서·조건·출구·연출이 모두 얼어 있다
3. 조건 평가만 바깥에서 주입한다. VnTool은 식을 해석하지 않는다
4. 작업 중인 노드를 재생하고 싶다면 `WorkingDialoguePreview`와 같은 방식으로
   `DialoguePublisher.AsWorkingResult`를 쓴다. **발행 결과로 오인되지 않는다** (Version 0)

**Yarn 가져오기를 만든다**

1. 새 프로젝트 `Vn.Import` — `Vn.Core`와 `Vn.Authoring`을 함께 참조
2. Yarn 노드 → `ScriptDocument` + `DialogueNode` 두 벌로 나눈다
   - `StoryLine` → `ScriptLine` + `LocalizedLine`
   - 기존 `#line:` 태그가 있으면 그것을 LineId로 삼는다 — **Yarn이 이미 하던 일이다**
   - `Condition` 블록 → 전환. **중첩이면 변환 불가로 보고한다**
   - 선택지(`->`)는 대응물이 없다. 손실로 보고한다
3. 무엇이 변환되고 무엇이 사라졌는지 목록으로 돌려준다

---

## 6. 깨면 다른 곳이 조용히 부서지는 규칙

0. **화자와 대사를 쓰는 코드는 `ProjectEditor.Scripts.cs`에만 둔다.**
   다른 경로가 하나라도 생기면 "한 줄을 고쳤을 때 무엇이 바뀌는가"에 답할 수 없게 된다.
   `DialogueNode`, `DialogueResult`, 화면 어디에도 편집 가능한 본문 복사본을 만들지 않는다.

0-1. **LineId를 확신 없이 잇지 않는다.**
   `ScriptSynchronizer`가 `Ambiguous`를 하나라도 내면 계획 전체를 적용하지 않는다.
   "그래도 대충 맞겠지"로 이으면 작가가 쓰지 않은 연출이 다른 대사에 붙고,
   그 사실은 최종 출력에서야 드러난다.

0-2. **발행된 결과를 수정하지 않는다.**
   `ResultRepository`에는 추가만 있다. 결과의 속성에는 setter가 없다.
   내용이 바뀌어야 한다면 그것은 새 버전이다.

0-3. **호환되지 않는 결과를 정상 출력처럼 합성하지 않는다.**
   Id·Version·ContentHash 세 값이 모두 맞아야 한다. 어긋난 Runtime Full은 겉보기에 멀쩡하다.

1. **조건 해석은 `ConditionFlowResolver` 하나만 쓴다.**
   각자 계산하면 화면과 그래프가 다른 구조를 보여 주고, 작가는 어느 쪽이 맞는지 알 수 없다.

2. **색·좌표에서 조건을 거꾸로 추론하지 않는다.**
   방향은 언제나 전환 데이터 → 갈래 계산 → 표시다. 색은 데이터가 아니다.

3. **색만으로 정보를 전달하지 않는다.** 갈래에는 언제나 조건 이름이 함께 붙는다.

4. **조건 갈래 출구는 여는 줄에 매단다.** 마지막 줄에 매달면 줄 하나에 의미가 무너진다.

5. **모델 변경은 `ProjectEditor`만 한다.** 화면이 직접 만지면 되돌리기와 알림이 빠진다.

6. **화면끼리 직접 이야기하지 않는다.** 그래프와 노드 편집기는 세션만 본다.
   그래프는 도메인을 직접 순회하지 않고 `GraphProjection`을 그린다.
   무엇이 보이는지의 규칙이 그리는 코드 안에 섞이면 테스트할 자리가 사라진다.

7. **파일 순서와 그래프 좌표는 별개다.** 새 노드는 언제나 파일 맨 뒤에 붙는다.
   시각적 위치를 파일 순서에 반영하면 diff가 매번 크게 흔들린다.

8. **모르는 파일을 관대하게 읽지 않는다.**
   열리지 않는 것은 되돌릴 수 있지만, 덮어써진 원고는 되돌릴 수 없다.
   형식 버전 2 이하도 같은 이유로 거부한다 (§8).

8-1. **작가의 원본 대본 파일에 쓰지 않는다.** 도구는 그 파일을 읽기만 한다.

9. **연출과 조건은 줄 번호가 아니라 `LineId`에 매단다.**
   줄을 옮기면 따라가야 하고, 줄이 대본에서 빠지면 고아로 남아야 한다.
   자동으로 지우면 작가가 쓴 것이 말없이 사라진다.
   (전환 자체를 지우는 것은 다르다 — §4.2)

10. **작가가 고른 조건을 도구가 조용히 바꾸지 않는다.**
   연결이 끊겨 쓸 수 없게 된 조건도 전환은 그대로 두고 "사용할 수 없음"으로 보여 준다.
   말없이 다른 조건으로 갈아 끼우면 이야기가 바뀐 것을 아무도 모른다.

11. **바인딩이 값을 넣을 때 나는 이벤트를 사용자 입력으로 보지 않는다.**
   `_building` 가드와 `ProjectChangeKind.Content`가 그 역할을 한다.

12. **XAML을 읽는 도중에도 컨트롤은 이벤트를 낸다.** `x:Name` 필드는 그 뒤에 채워진다.

13. **골든 파일은 언제나 UTF-8로 읽는다.** PowerShell 5.1은 BOM이 없으면 ANSI로 읽는다.
    한국어를 담은 `.ps1`은 UTF-8 BOM으로 저장해야 한다.

14. **프리뷰가 다루지 못하는 것을 조용히 버리지 않는다.**
    폴드가 인식 못 한 커맨드는 `Unhandled`로, 못 찾은 에셋 키는 플레이스홀더와
    `Problems`로 반드시 화면에 남는다 (§3.10). 조용히 버리면 나중에
    "왜 이 장면만 다르지"로 돌아온다 — 그때는 어디서 사라졌는지 아무도 모른다.

15. **연출 커맨드를 만드는 새 길을 낼 때는 ProjectEditor로 수렴시킨다 (§3.10).**
    직접 조작이든 파서든 프리뷰용 별도 쓰기 경로를 만들면 되돌리기·알림·발행이
    그 길만 비켜 가고, 이미터 출력이 입력 방법에 따라 달라진다. 커맨드 텍스트의
    조립·파싱 규칙도 `CommandText` 하나다 — 화면과 파일이 다른 문장을 만들면 안 된다.

---

## 7. 형식 버전과 파일

| 형식 | 상수 | 파일 |
|---|---|---|
| 프로젝트 | `StoryProject.CurrentFormatVersion` = 3 | `*.vnproject.json` |
| 대본 | `ScriptDocumentJson.CurrentFormatVersion` = 1 | `*.vnscript.json` |
| StoryFile | `StoryFileJson.CurrentFormatVersion` = 1 | `*.vnstory.json` |
| 발행 결과 | `ResultStoreJson.CurrentFormatVersion` = 1 | `results.vnresults.json` |
| 되돌리기 스냅샷 | `ProjectSnapshotCodec.CurrentSnapshotVersion` = 2 | (메모리) |
| 대사 결과 스키마 | `DialogueResult.CurrentSchemaVersion` = 3 | 결과 안의 `schemaVersion` |
| 연출 결과 스키마 | `PresentationResult.CurrentSchemaVersion` = 3 | 결과 안의 `schemaVersion` |

**형식 버전과 결과 스키마 버전은 다르다.** 앞의 것은 파일을 열 수 있는지, 뒤의 것은
이미 발행된 결과를 지금 도구가 이해할 수 있는지를 말한다. 스키마 버전이 미래이면
합성만 막고 파일은 정상적으로 읽는다. 결과는 불변이므로 읽지 못하는 것과 고치는 것은 다르다.

## 8. 이전 프로젝트 정책 — 마이그레이션하지 않고 거부한다

형식 버전 1과 2는 **읽지 않는다.** `ProjectManifestJson.Read`가 이유를 담아 거부한다.

그 형식에서는 `LineBox` 하나가 정체성·화자·대사·조건을 모두 소유했다. 지금 구조로
자동 변환하려면 도구가 다음을 임의로 정해야 한다.

- 그 노드의 줄들을 어느 대본 하나로 묶을 것인가
- 그 대본의 원본 파일은 무엇이라고 할 것인가
- 연출이 실시간으로 읽던 대사를 어느 결과 버전으로 얼릴 것인가

셋 다 근거 없이 정하면 **작가의 원고가 새 의미로 오인된 채 저장된다.** 열리지 않는 것은
되돌릴 수 있지만 덮어써진 원고는 되돌릴 수 없다. 그래서 거부한다.

이전 프로젝트의 내용을 옮기려면 대사를 텍스트로 꺼내 `raw/*.txt`로 저장하고 새 프로젝트에서
가져오면 된다. 그 경로에서는 사람이 무엇이 어디로 가는지 보고 결정한다.

**거부는 파괴적이지 않다.** 열기에 실패해도 원본 파일은 그대로이고 세션도 원래 상태로
남는다 (`AuthoringSessionTests.이전_형식을_열면_거부하고_세션은_그대로_남는다`).

---

## 9. 확인 방법

```bash
dotnet build .\VnTool.sln
```

```bash
dotnet test .\VnTool.sln
```

```bash
powershell -ExecutionPolicy Bypass -File .\build-and-run.ps1
```

```bash
dotnet run --project .\src\Vn.App\Vn.App.csproj
```

- 저작 도메인은 `dotnet test`가 전부 덮는다
- `build-and-run.ps1`은 Yarn 분석 쪽 골든 픽스처까지 확인한다
- 창이 뜨지 않으면 `%LOCALAPPDATA%\VnTool\logs\startup-error.log`를 먼저 본다

### 어느 테스트가 무엇을 지키는가

| 파일 | 지키는 것 |
|---|---|
| `ScriptImportTests` | 지원하지 않는 줄을 조용히 버리지 않는다 |
| `ScriptSyncTests` | 재동기화 여섯 사례 + 애매하면 멈춘다 + 은퇴 Id를 재사용하지 않는다 |
| `ConditionFlowTests` / `ConditionChoiceTests` | 조건 해석과 드롭다운 규칙 |
| `ExitTests` | 출구는 여는 줄에 매달린다 / 대본에서 빠진 줄의 출구는 고아로 남는다 |
| `EditingTests` | 화자·대사의 권위가 대본에만 있다 / 되돌리기 |
| `PublishTests` | 결과 불변성 · 버전 증가 · 중복 발행 · 발행 거부 |
| `PresentationNodeTests` | 연출이 얼어붙은 결과를 읽는다 · orphan 보존 |
| `RuntimeCompositionTests` | 호환되지 않는 조합을 정상 출력으로 만들지 않는다 |
| `ResultDocumentComposerTests` | Segment 순서와 원본 매핑 |
| `YarnBundleEmitterTests` | 트리오 구조 · 계약 규칙(A2·A5·B·C1·C4·D2·D3·D4·E2) · 원자적 쓰기 |
| `YarnBundleVerificationTests` | 골든 트리오 비교 · 수정→재발행→재출력 왕복 · **Vn.Core 실컴파일** |
| `OutputPresetTests` | 다섯 프리셋이 같은 결과에서 나오고 원본 매핑을 잃지 않는다 |
| `SerializationTests` | 결정적 JSON · 원자적 저장 · 이전 형식 거부 |
| `VerticalSliceTests` | **대본 → 출력 → 저장 → 다시 열기 → 같은 문자열** |
| `SampleProjectTests` | 손으로 쓴 샘플이 도구의 계산과 같다 |

손으로 쓴 예제는 `samples/Authoring/`이다. `raw/`의 원본 대본, `script/`의 LineId 산출물,
`story/`의 노드가 각각 무엇을 담는지 눈으로 확인할 수 있다.
저장 형식을 고치면 `SampleProjectTests`가 먼저 깨진다.

**발행 결과는 샘플에 손으로 쓰지 않았다.** 내용 해시를 사람이 계산할 수 없기 때문이다.
결과와 조합의 왕복은 `VerticalSliceTests`가 임시 디렉터리에서 실제로 저장·재개봉하며 확인한다.

---

## 10. 챕터 계층 (기획자)

2026-08-11에 얹힌 층이다. 앞의 §1~§9가 다루는 **작가·연출 계층 위**에 서고, 둘은 데이터도
편집 경로도 공유하지 않는다.

### 10.1 무엇이 다른가 — 원본이 엑셀이다

작가 계층의 원본은 `script/`·`story/`의 JSON이고 도구가 소유한다. **챕터 계층의 원본은
엑셀 워크북이고 사람이 소유한다.** 도구는 읽는 쪽이다.

| | 원본 | 도구가 쓰는가 |
|---|---|---|
| 대사 본문·화자 | `episodes/{챕터}/{Id}.xlsx` | **아니다** (없는 파일을 만들 때만) |
| 에피소드 구조·간선·조건·스탯 | `chapters/{Id}.xlsx` | 그렇다 — 셀에 즉시 |
| 화자(캐스트) | `game.definition.json`의 speakers | 그렇다 — **툴 [화자] 탭이 유일한 창구**다 (2026-08-23). 챕터 엑셀의 어느 시트도 화자를 안 써서 `화자` 시트를 폐지했다. 프로젝트에 하나뿐이고 모든 챕터가 공유한다 |

이 뒤집힘이 이 층의 설계를 거의 다 설명한다. 파일을 엑셀이 잡고 있으면 쓰기가 **거부**되고
(`ChapterWorkbookWriter.IsLockedByAnotherApp`), 감시자가 저장을 잡아 다시 읽는다.

### 10.2 파일 지도 — `Vn.Authoring/Chapters/`

**읽기·쓰기·이행**

| 파일 | 하는 일 |
|---|---|
| `ChapterWorkbookReader.cs` | 챕터 워크북 6시트 → `ChapterGraphModel` |
| `ChapterWorkbookWriter.cs` | 셀 쓰기 전부. 잠금 판정도 여기 |
| `ChapterWorkbookMigrator.cs` | 구판 → 최신 규격 자동 이행 (`.bak`), 앱이 열 때 |
| `EpisodeWorkbookReader.cs` · `EpisodeWorkbookMigrator.cs` | 대본 워크북(6열)의 같은 짝 |
| `EpisodeLibrary.cs` | 대본 파일의 자리(`episodes/{챕터}/`) · 생성 · 개명 · 입양 |
| `ChapterLibrary.cs` | `chapters/` 폴더를 훑어 목록을 만든다 |
| `ChapterFolderWatcher.cs` | 저장 감지 + 디바운스 |

**해석·검증·출력**

| 파일 | 하는 일 |
|---|---|
| `ConditionExpressionParser.cs` | 시트 세 칸(스탯·연산자·값) ↔ 조건식 문자열 |
| `EpisodeFlattener.cs` | 대본 행 → 평평한 줄 + `IF`~`ENDIF` 블록 해석 |
| `ChapterValidator.cs` | 구조 검증 + 도달성 증명을 한 벌로 묶어 돌린다 |
| `ChapterReachabilityProver.cs` | 스탯을 들고 상태공간을 걸어 "닿을 수 있는가"를 증명 |
| `ChapterBranchPlanner.cs` | 배치 = 깊이 레이아웃. 흐름이 바뀌면 자리가 따라온다 |
| `ChapterProgressionExporter.cs` | 런타임 수입용 JSON. **검증을 통과해야 나간다** |
| `EpisodeSyncService.cs` | 대본 워크북 → 판의 대사 노드. 두 계층이 만나는 유일한 지점 |

**화면**: `Vn.App/Views/ChapterGraphView.axaml{,.cs}`

### 10.3 갱신 경로 — 여기가 성능의 급소다

```
엑셀 저장 ─┐
툴이 씀 ───┼─→ QueueReload() ─→ WatchAndReload ─→ Reload()
감시자 ────┘      (합친다)                          ├ 이행
                                                    ├ ChapterLibrary.Load
                                                    ├ AutoExport   ┐ 검증 결과를
                                                    ├ Validate     ┘ 한 벌만 쓴다
                                                    └ Draw
```

**세 가지를 깨면 노드가 늘 때 조용히 느려진다** (2026-08-18에 58초를 1.66초로 되돌린 자리):

1. **`SetStatus`는 프로젝트 변경이 아니다.** 상태줄은 `StatusChanged`로 따로 운다. 예전에는
   상태 한 줄이 `Changed`를 울려 워크북 전체 재읽기를 불렀고, 그 재읽기가 다시 상태를
   적었다 — 스스로를 먹는 고리였다.
2. **재읽기는 `QueueReload()`로 합친다.** 동기화 한 번이 변경을 수십 개 내는데, 마지막
   하나 말고는 전부 버려질 그림이다.
3. **검증은 챕터별 (내용 해시, 결과) 캐시를 지난다.** 순수 함수이고 값이 비싸다(대본
   워크북을 전부 열고 상태공간을 훑는다). 내보내기는 `ExportValidated`로 그 결과를 받아
   쓴다 — 예전에는 같은 증명을 두 번 돌렸다.

**툴이 워크북을 쓰면 `Report()`가 명시적으로 재읽기를 예약한다.** 예전에는 상태 메시지가
그 일을 우연히 대신했다. 쓴 자리가 곧 아는 자리다.

고정은 시간(ms)이 아니라 **일의 횟수**로 건다 — `ChapterGraphWorkAmountTests`.

### 10.4 §5.1에 더하는 빠른 조회

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 엑셀 시트에 열 추가·규격 변경 | `ChapterWorkbookReader` → `Writer` → `Migrator` → 견본 → 테스트 |
| 그래프에 무엇이 그려지는가 | `Graph/GraphProjectionBuilder.cs` — 화면이 아니라 여기가 정한다 |
| 검증 규칙 추가 | `ChapterValidator.cs` (도달성이면 `ChapterReachabilityProver.cs`) |
| 런타임에 나가는 필드 | `ChapterProgressionExporter.cs` + [`docs/runtime-contract.md`](docs/runtime-contract.md) §G |
| 대본 ↔ 노드 동기화 | `EpisodeSyncService.cs` |
| 모델을 바꾸는 유일한 통로 | `Editing/ProjectEditor.cs` (§5.1과 같다) |
