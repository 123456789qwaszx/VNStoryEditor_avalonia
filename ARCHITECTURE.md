# VnTool 구조

이 문서 하나가 구조에 대한 기준이다. 무엇이 어떻게 바뀌었는지, 각 조각이 무슨 일을 하는지,
그리고 어떤 기능을 손보려면 어느 파일을 여는지를 담는다.

제품 사용 흐름은 [README.md](README.md)에 있다.

---

## 1. 무엇이 바뀌었나 — 방향을 뒤집었다

### 이전

```
.yarn 원문  ← 진실
   │  YarnCompilerAdapter로 읽는다
   ▼
StoryLine / StoryBlock  ← 손실 압축된 화면 모델
   │
   ▼
화면에서 한 줄 수정 → StoryLineReplacer가 원문의 그 줄만 갈아 끼운다
```

원문이 진실이었기 때문에 **화면에서 만든 구조를 되쓸 방법이 없었다.**
`StoryLine`과 `StoryBlock`은 표시하려고 가공한 것이라 그것으로 Yarn을 재조립하면
작가의 공백·주석·태그가 파괴된다. 그래서 편집은 "물리적 줄 하나를 안전하게 교체"에 갇혔고,
선택지 추가·조건 갈래 생성·노드 연결 같은 구조 편집은 원리상 불가능했다.

또 이 도구는 특정 Unity 프로젝트의 Yarn DSL에 묶여 있었다. 다음 게임에 가져가려면
DSL부터 맞춰야 했다.

### 지금

```
StoryProject  ← 진실 (project.vnproject.json + story/*.vnstory.json)
   ├─ SetNode        조건과 변수를 정의한다
   └─ DialogueNode
        ├─ LineBox[]      화자·대사·조건 전환
        ├─ BranchExits    조건 갈래 출구
        └─ DefaultExit    노드 전체의 출구
   │
   │  계산 (저장하지 않는다)
   ▼
DialogueFlow (갈래·깊이·출구 위치)  →  화면 카드 / 그래프 포트
```

저작 모델이 원본이고, Yarn을 비롯한 게임별 형식은 **앞으로 내보내기 대상**이 된다.
게임마다 달라지는 변수·이벤트는 코드가 아니라 `game.definition.json`이 공급한다.
그래서 같은 도구를 다음 게임에 그대로 가져갈 수 있다.

### 바뀐 것 한눈에

| 주제 | 이전 | 지금 |
|---|---|---|
| 진실의 원천 | `.yarn` 원문 | `StoryProject` (manifest + StoryFile) |
| 저장 방식 | 원문의 한 줄만 부분 교체 | 프로젝트 전체 직렬화 |
| 편집 범위 | 대사·화자 한 줄 | 노드·줄·조건·연결 전부 |
| 조건 표현 | Yarn `<<if>>` 블록을 읽어 트리로 | 줄의 **전환**만 저장하고 갈래는 계산 |
| 그래프 | 분석 결과를 보는 화면 | 노드를 만들고 잇는 저작 화면 |
| 좌표 저장 | `vn.workspace.json` (별도 파일) | 프로젝트 파일 안 |
| 게임 종속성 | Yarn DSL에 묶임 | `game.definition.json`이 주입 |
| 화면 간 동기화 | 각자 상태를 들고 맞춤 | 같은 계산을 볼 뿐, 동기화 코드 없음 |

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

**남긴 것:** `Vn.Core`(Yarn 분석 엔진 전체), `Vn.Cli`, 골든 픽스처, `AppSettingsService`(최근 프로젝트),
`StartupLog`, `Program`/`App` 시작 경로. 1년치 분석 엔진은 그대로 살아 있고, 앞으로 기존 원고를
새 형식으로 가져오는 통로가 된다.

### 더 이상 지원하지 않는 것

- Yarn 원문 직접 편집, 원문 서식·주석 보존
- Yarn 선택지(`->`)와 중첩 조건 — 새 모델에 대응물이 아직 없다
- `.yarnproject` 열기 — 열려고 하면 **거부하고 알린다** (관대하게 읽으면 저장 시 원본이 덮어써진다)

---

## 3. 프로젝트 구성과 각자의 역할

```
Vn.Authoring ── 저작 도메인. 공식 원본. 화면도 파일 대화상자도 모른다.
   ▲
   └── Vn.App    Avalonia 저작 화면

Vn.Core   ── Yarn 읽기·분석 엔진. 저작 경로에 관여하지 않는다.
   ▲
   └── Vn.Cli    Yarn 검증 콘솔

tests/Vn.Authoring.Tests   조건 흐름·출구·연결·파일·연출·문서 합성 (120)
tests/Vn.Core.Tests        Yarn 분석과 골든 픽스처 (60)
tests/Vn.App.Tests         앱 서비스 — 세션·설정·갱신 범위·시작 로그 (40)
```

**`Vn.App`은 `Vn.Core`를 참조하지 않는다.** 두 세계가 갈라져 있다.

### 3.1 `Vn.Authoring/Model` — 무엇이 있는가

| 타입 | 역할 |
|---|---|
| `StoryProject` | `StoryFile` 목록, 제목, 시작 노드. 조건 조회의 입구 |
| `StoryFile` | 노드를 **소유하는** 단위. Id는 이름·경로와 분리되어 있다 |
| `StoryNode` (추상) | Id, 이름, 그래프 좌표, **기본 출구**. 종류를 가리지 않는 공통부 |
| `SetNode` | 조건 정의와 변수 값. **조건이 태어나는 유일한 자리** |
| `DialogueNode` | `LineBox` 목록, `BranchExits`(갈래 출구) |
| `PresentationNode` | `LineId`별 `PresentationLineBinding`. **대사를 복사하지 않는다** |
| `PresentationCommandInstance` | 명령 하나. 정의 Id, 인자, 사용 여부, 메모. 순서를 보존한다 |
| `LineBox` | 작가가 보는 최소 단위. Id·화자·대사·조건 전환 |
| `LineConditionTransition` | `BeginIf` / `BeginElseIf` / `EndIf` |
| `ConditionDefinition` | Id + 작가용 이름 + 게임이 평가할 식 |
| `NodeLink` | 실행이 아닌 연결. `Settings`는 SetNode가 Dialogue에 조건을 공급한다 |
| `Identifier` | `sf_` / `nd_` / `ln_` / `cd_` / `lk_` 안정 식별자 생성 |

**Id와 이름을 나눈 이유:** 작가는 노드 이름을 바꾸고 줄 순서를 계속 바꾼다.
그때마다 간선과 출구가 끊어지면 저작 도구로 쓸 수 없다. 파일도 같다. 파일 이름과
상대 경로가 바뀌어도 `StoryFile.Id`는 그대로이므로 노드 소유 관계가 끊기지 않는다.

**NodeId는 파일이 아니라 프로젝트 전체에서 유일하다.** 파일을 넘나드는 출구가 있고,
노드가 파일 사이를 옮겨 다니기 때문이다. 프로젝트 전체 순회는 `EnumerateNodes()`
하나이며 순서는 파일 순서 + 파일 안 순서로 고정한다.

### 3.2 `Vn.Authoring/Flow` — 무엇을 계산하는가

| 타입 | 역할 |
|---|---|
| `ConditionFlowResolver` | **조건 모델의 유일한 해석자.** 줄의 전환을 훑어 갈래를 만든다 |
| `DialogueFlow` | 계산 결과: `ResolvedLine[]`, `ConditionBranch[]`, `FlowProblem[]` |
| `ConditionBranch` | 갈래 하나. 여는 줄 Id, 조건, 체인 번호, 색 자리, 범위, 출구 |
| `ResolvedLine` | 줄 하나의 갈래·깊이·출구 여부, 그리고 **전환 적용 전** 갈래 |
| `ConditionChoices` | 조건 드롭다운에 무엇을 보여 줄지 (§6.4 규칙) |
| `AvailableConditionResolver` | **이 DialogueNode가 쓸 수 있는 조건**의 카탈로그 |
| `PresentationBindingResolver` | binding이 지금 어느 줄에 붙는지, 고아인지 |
| `NodeConnections` | 노드의 출력 포트와 프로젝트 전체 간선 |

`ResolvedLine`에 "전환 적용 전 갈래"(`PrecedingBranch`)가 함께 있는 이유는,
드롭다운의 의미가 **바로 앞 줄까지의 상태**로 정해지기 때문이다.

### 3.3 `Vn.Authoring/Graph` — 그래프에 무엇을 그릴지

| 타입 | 역할 |
|---|---|
| `GraphProjectionBuilder` | 프로젝트와 펼침 상태로부터 **화면에 그릴 것**을 계산한다 |
| `ExpandedNodeProjection` | 펼친 파일의 실제 노드 카드 |
| `CollapsedFileProjection` | 접힌 파일 하나. 소유 노드가 `CollapsedNodeEntry` 행으로 들어간다 |
| `GraphConnectionProjection` | 실행·설정 간선 하나와 그 라벨·색 자리 |
| `GraphEndpointProjection` | 간선의 끝이 실제 포트인지 프록시의 몇 번째 행인지 |
| `OrthogonalEdgeRouter` | 두 끝점 사이 ㄱ자 경로 |

**연결의 소스와 대상은 언제나 실제 NodeId다.** 파일을 접어도 대상이 FileId로 바뀌지
않는다. 바뀌는 것은 그 끝을 화면 어디에 붙일지뿐이다. 이 구분을 잃으면 접었다 펴는
동작이 데이터를 바꾸게 된다.

### 3.4 `Vn.Authoring/Editing` — 어떻게 바꾸는가

| 타입 | 역할 |
|---|---|
| `ProjectEditor` | **모델을 바꾸는 유일한 통로.** 되돌리기·알림을 함께 책임진다 |
| `ProjectChangedEventArgs` | 변경 종류: `Structure` / `Content` / `ConditionDefinition` / `Connections` / `NodeMetadata` / `Layout` |

**변경 종류는 영향 범위를 말한다.** 조건 이름을 고치는 일과 조건을 추가하는 일은
화면에 주는 영향이 다르다. 같은 신호를 보내면 화면은 최악의 경우를 가정해 편집 중인
컨트롤까지 다시 만들고, 작가는 입력 도중 포커스를 잃는다.

### 3.5 `Vn.Authoring/Rendering` — 평평한 문서로 펼치기

| 타입 | 역할 |
|---|---|
| `DialogueDocumentComposer` | DialogueNode 하나를 `RenderedSegment` 목록으로 합성한다 |
| `RenderedDocument` / `RenderedSegment` | 문서 한 벌과 그 조각. 종류·레이어·들여쓰기 |
| `RenderSourceReference` | 이 조각이 어느 StoryFile·Node·Line·조건에서 나왔는지 |
| `DocumentOutputOptions` | 어떤 레이어와 연출 category를 담을지. 다섯 프리셋이 여기 있다 |
| `YarnPreviewFormatter` | Runtime Full의 Yarn 스타일 문자열 |
| `DocumentPreviewFormatter` | 시나리오·녹음·번역·연출 지시서 문자열 |
| `ILocalizedLineProvider` | LineId로 번역문을 공급하는 바깥 경계 |
| `ConnectedSetNodeResolver` | 이 Dialogue에 연결된 SetNode와 그 순서를 한 번만 계산 |

**합성이지 파싱이 아니다.** 방향은 언제나 모델 → 문서다. Preview 문자열을 다시 읽어
모델로 되돌리지 않는다. 되파싱을 허용하는 순간 진실이 두 곳이 된다.

**출력 프리셋은 옵션일 뿐 저장 대상이 아니다.** `StoryProject`, 스냅샷, 되돌리기 어디에도
들어가지 않는다. 필터로 빠진 Segment가 있어도 남은 Segment의 원본 참조는 그대로다.

Segment는 문자열만 들고 있지 않다. 어디서 나왔는지를 함께 들고 있어야 미리보기 줄을
눌러 원본으로 갈 수 있고, 녹음 대본과 번역본이 같은 LineId를 공유할 수 있다.

### 3.6 `Vn.Authoring/Serialization`, `Definition`

| 타입 | 역할 |
|---|---|
| `ProjectManifestJson` | `*.vnproject.json` — 제목, 시작 노드, StoryFile의 Id·이름·경로 |
| `StoryFileJson` | `*.vnstory.json` — StoryFile 하나와 그것이 소유한 노드 |
| `StoryNodeJson` | 노드 직렬화 규칙의 **단일 구현**. manifest도 스냅샷도 여기를 지난다 |
| `ProjectStore` | 경로 해석, 전체 읽기·쓰기, 임시 파일 교체 |
| `ProjectSnapshotCodec` | 되돌리기와 저장 여부 비교용 aggregate 문자열 |
| `LegacyProjectJson` | 버전 1 평면 nodes와 inline 버전 2를 새 구조로 들여온다 |
| `JsonSupport` | 공통 읽기 도우미와 형식 검증 |
| `GameDefinition` | 게임별 변수·이벤트 후보. 없으면 빈 정의로 계속 |
| `PresentationCommandCatalog` | 게임별 연출 명령 정의. 정의 파일이 없으면 기본 카탈로그 |

**연출 명령을 C# enum에 박지 않는다.** 게임마다 명령과 프리셋이 다르다. 코드가 특정
게임의 명령 이름을 알기 시작하면 그 게임 전용 도구가 된다. 변수·이벤트와 같은 이유다.

**디스크 배치와 되돌리기 형식을 나눈 이유:** 되돌리기 한 번에 여러 실제 파일을 다시
조립할 이유가 없다. manifest와 StoryFile 구조가 앞으로 또 바뀌어도 편집 기록은
`ProjectSnapshotCodec`의 aggregate 하나로 독립되어 있어야 한다.

**저장 순서:** StoryFile을 먼저 임시 파일로 교체하고 manifest를 마지막에 바꾼다.
중간에 멈춰도 manifest가 가리키는 파일 집합은 언제나 존재한다.

### 3.7 `Vn.App` — 화면

| 타입 | 역할 |
|---|---|
| `AuthoringSession` | 열린 프로젝트 경로, 저장 여부, 선택 상태, `ActiveFileId`와 `ExpandedFileIds`. 편집은 도메인이 한다 |
| `GraphEditorView` | 노드 추가·이동·선택, 포트 드래그 연결, 간선 선택·삭제 |
| `DialogueNodeEditor` | `LineBox` 카드, 조건 드롭다운, 갈래 색·들여쓰기, 출구 표시 |
| `SetNodeEditor` | 조건과 변수 값 정의 |
| `PresentationNodeEditor` | 연결된 대사를 읽기 전용으로 비추고 LineId마다 연출 명령 배정 |
| `BranchPalette` | 갈래 색 표. 데이터가 아니라 표시 수단 |
| `MainWindow` | 도구 모음, 열기·저장, 세션을 두 화면에 물려준다 |
| `ProjectRefreshPlanner` | 변경 알림 하나가 어느 화면을 다시 만들게 할지 정하는 유일한 자리 |
| `AppSettingsService` | 최근 프로젝트 기억 |
| `StartupLog` | `%LOCALAPPDATA%\VnTool\logs\startup-error.log` |

---

## 4. 세 가지 핵심 설계 판단

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

여는 줄이 사라지거나 전환이 바뀌면 `ProjectEditor.PruneBranchExits`가 출구를 함께 버린다.

### 4.3 그래프 포트는 저장하지 않고 계산한다

포트와 간선은 조건 전환에서 나온다. 그래서

- 대사 화면에서 `elseif`를 추가하면 → 그래프에 포트가 하나 는다
- 그래프에서 포트를 끌어 이으면 → 대사 화면의 출구 표시가 바뀐다

**두 화면 사이에 동기화 코드가 한 줄도 없다.** 같은 것을 계산해서 볼 뿐이라 어긋날 자리가 없다.
모든 변경은 `ProjectEditor.SetExitTarget` 하나를 지난다.

---

## 5. 기능을 바꾸려면 어디를 여는가

### 5.1 빠른 조회

**저작 모델**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| LineBox에 항목 추가(BGM·이벤트·메모) | `Model/LineBox.cs` → `Serialization/StoryNodeJson.cs` → `Editing/ProjectEditor.cs` → `App/Views/DialogueNodeEditor.axaml.cs` |
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
| `else` 갈래 추가 | `Model/LineBox.cs`의 `ConditionTransitionKind` → 위 해석자 → `Flow/ConditionChoices.cs` |
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
| 연출 연결과 그 제약 | 같은 파일의 `SetPresentationTarget` — 연출 하나는 대사 하나만 |
| 연출 명령 편집 | 같은 파일의 `AddPresentationCommand` / `MovePresentationCommand` / `RemovePresentationCommand` |
| 줄이 사라진 연출을 찾기 | `Flow/PresentationBindingResolver.cs`의 orphan 판정 |
| 연출 명령 목록에 항목 추가 | `game.definition.json`의 `presentationCommands` — 코드가 아니다 |
| 이 대사에 붙은 연출 노드 찾기 | `Flow/ConnectedPresentationNodeResolver.cs` |
| 포트를 만드는 규칙 | `Flow/NodeConnections.cs`의 `PortsOf` |
| 간선 라벨 문구 | `Flow/NodeConnections.cs`의 `LabelFor` |
| 연결·해제 동작 | `Editing/ProjectEditor.cs`의 `SetExitTarget` |
| 포트를 끌어 잇는 조작 | `App/Views/GraphEditorView.axaml.cs`의 `OnPortPressed` / `OnCanvasPointerReleased` |
| 간선 선택·삭제 | 같은 파일의 `SelectEdge` / `DeleteSelectedEdge` |
| 노드 카드 크기·포트 좌표 | 같은 파일 위쪽 상수와 `PortAnchor` / `InputAnchor` |
| 그래프에 무엇이 나타나는지 | `Graph/GraphProjectionBuilder.cs` — 화면이 아니라 여기가 정한다 |
| Preview에 무엇이 나오는지 | `Rendering/DialogueDocumentComposer.cs` |
| 출력 프리셋 추가·수정 | `Rendering/DocumentOutputOptions.cs`의 `OutputPresetCatalog` |
| Preview 문자열 모양 | `Rendering/YarnPreviewFormatter.cs`, `Rendering/DocumentPreviewFormatter.cs` |
| 접힌 파일 프록시의 모양 | `Graph/GraphProjection.cs`의 `CollapsedFileProjection` |
| 간선이 꺾이는 모양 | `Graph/OrthogonalEdgeRouter.cs` |

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
| 어떤 파일을 열지 말지 | `ProjectStore`와 `JsonSupport`의 검증 |
| 되돌리기·저장 여부 비교 형식 | `Serialization/ProjectSnapshotCodec.cs` |
| 옛 형식 들여오기 | `Serialization/LegacyProjectJson.cs` |
| 형식 버전 올리기 | `Model/StoryProject.cs`의 `CurrentFormatVersion` + 위 세 읽기 경로 |
| 게임별 정의 스키마 | `Definition/GameDefinition.cs` |

**화면**

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 도구 모음, 열기·저장, 창 제목 | `App/MainWindow.axaml{,.cs}` |
| 그래프 화면 | `App/Views/GraphEditorView.axaml{,.cs}` |
| 대사 카드의 내용과 배치 | `App/Views/DialogueNodeEditor.axaml{,.cs}` |
| 설정 노드 화면 | `App/Views/SetNodeEditor.axaml{,.cs}` |
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

**LineBox에 새 항목을 붙인다 (예: BGM)**

1. `Model/LineBox.cs` — 속성 추가, `Clone()`에도 반영
2. `Serialization/StoryNodeJson.cs` — 쓰기·읽기 양쪽. 값이 없으면 키를 생략해 파일이 조용하게
3. `Editing/ProjectEditor.cs` — 편집 명령. 글자만 바뀌면 `ProjectChangeKind.Content`
4. `App/Views/DialogueNodeEditor.axaml.cs`의 `BuildCard` — UI
5. `tests/Vn.Authoring.Tests/SerializationTests.cs` — 왕복 테스트

**노드 종류를 추가한다 (예: 연출 노드)**

1. `Model/StoryNode.cs` — 파생 클래스와 `Clone()`
2. `Serialization/StoryNodeJson.cs` — `kind` 문자열, 쓰기·읽기 분기 양쪽
3. `Flow/NodeConnections.cs` — 이 노드의 포트 규칙
4. `App/Views/`에 편집기 하나 + `MainWindow.ShowSelectedNode`에 분기
5. `App/Views/GraphEditorView.axaml.cs`의 `BuildCard` — 배지와 배경색
6. `Editing/ProjectEditor.cs` — `AddXxxNode` 편의 메서드

**조건 모델을 넓힌다 (예: else, 중첩)**

1. `Model/LineBox.cs`의 `ConditionTransitionKind`에 종류 추가
2. `Flow/ConditionFlowResolver.cs`의 상태 기계 수정
   — 깊이가 늘어나면 `ResolvedLine.Depth`의 뜻이 바뀌므로 화면 들여쓰기도 함께 본다
3. `Flow/ConditionChoices.cs` — 드롭다운 규칙
4. `Serialization/StoryNodeJson.cs` — `kind` 문자열
5. `tests/Vn.Authoring.Tests/ConditionFlowTests.cs` — **예시부터** 추가

**Yarn 가져오기를 만든다 (다음 단계)**

1. 새 프로젝트 `Vn.Import` — `Vn.Core`와 `Vn.Authoring`을 함께 참조
2. `AnalysisReport` → `StoryProject` 변환
   - Yarn 노드 → `DialogueNode`, `StoryLine` → `LineBox`
   - `Condition` 블록 → 전환. **중첩이면 변환 불가로 보고한다**
   - 갈래 끝의 `<<jump>>` → 갈래 출구, 노드 끝의 `<<jump>>` → 기본 출구
   - 선택지(`->`)는 대응물이 없다. 손실로 보고한다
3. 무엇이 변환되고 무엇이 사라졌는지 목록으로 돌려준다

---

## 6. 깨면 다른 곳이 조용히 부서지는 규칙

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

9. **연출은 줄 번호가 아니라 `LineId`에 매단다.**
   줄을 옮기면 연출이 따라가야 하고, 줄이 지워지면 연출은 고아로 남아야 한다.
   자동으로 지우면 작가가 쓴 것이 말없이 사라진다.

10. **작가가 고른 조건을 도구가 조용히 바꾸지 않는다.**
   연결이 끊겨 쓸 수 없게 된 조건도 전환은 그대로 두고 "사용할 수 없음"으로 보여 준다.
   말없이 다른 조건으로 갈아 끼우면 이야기가 바뀐 것을 아무도 모른다.

11. **바인딩이 값을 넣을 때 나는 이벤트를 사용자 입력으로 보지 않는다.**
   `_building` 가드와 `ProjectChangeKind.Content`가 그 역할을 한다.

12. **XAML을 읽는 도중에도 컨트롤은 이벤트를 낸다.** `x:Name` 필드는 그 뒤에 채워진다.

13. **골든 파일은 언제나 UTF-8로 읽는다.** PowerShell 5.1은 BOM이 없으면 ANSI로 읽는다.
    한국어를 담은 `.ps1`은 UTF-8 BOM으로 저장해야 한다.

---

## 7. 확인 방법

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

- 저작 도메인은 `dotnet test`가 전부 덮는다 (조건 흐름·출구·연결·직렬화)
- `build-and-run.ps1`은 Yarn 분석 쪽 골든 픽스처까지 확인한다
- 창이 뜨지 않으면 `%LOCALAPPDATA%\VnTool\logs\startup-error.log`를 먼저 본다

손으로 쓴 예제는 `samples/Authoring/project.vnproject.json`과 `samples/Authoring/story/`다.
저장 형식을 고치면 `SampleProjectTests`가 먼저 깨진다.
