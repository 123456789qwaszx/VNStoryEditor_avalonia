# VnTool 수정 지점 지도

이 문서는 "무엇을 고치고 싶을 때 어느 파일을 여는가"에만 답한다.
세부 코드를 읽기 전에 이 문서에서 대상 클래스를 먼저 특정하는 용도다.

제품 개요는 [README.md](README.md)에 있다.

---

## 1. 프로젝트 다섯 개와 의존 방향

```
Vn.Authoring ── 저작 도메인. 공식 원본이다. 화면도 파일 대화상자도 모른다.
   ▲
   └── Vn.App    Avalonia 저작 화면. 그래프와 노드 편집기.

Vn.Core   ── Yarn 읽기·분석 엔진. 저작 경로에 관여하지 않는다.
   ▲
   └── Vn.Cli    Yarn 검증 콘솔. 골든 픽스처의 생산자.

tests/Vn.Authoring.Tests   조건 흐름·출구·연결·직렬화
tests/Vn.Core.Tests        Yarn 분석과 골든 픽스처
tests/Vn.App.Tests         앱 서비스(설정·시작 로그)
```

**두 세계가 분리되어 있다.** 저작 도구는 `Vn.Authoring`만 보고, Yarn 분석은 `Vn.Core`에 남아 있다.
`Vn.App`은 `Vn.Core`를 참조하지 않는다. 저작 모델이 원본이고 Yarn은 앞으로 가져오기·내보내기 형식이 되기 때문이다.

| 무엇이 어디 있나 | 위치 |
|---|---|
| 노드·줄·조건·출구 모델 | `src/Vn.Authoring/Model` |
| 조건 갈래 계산, 그래프 포트 | `src/Vn.Authoring/Flow` |
| 편집(모든 변경의 유일한 통로) | `src/Vn.Authoring/Editing` |
| 저장 형식 | `src/Vn.Authoring/Serialization` |
| 게임별 정의 | `src/Vn.Authoring/Definition` |
| 화면 | `src/Vn.App/Views` |
| Yarn 분석 | `src/Vn.Core` |

---

## 2. 데이터가 흐르는 길

```
story.vnstory.json  +  game.definition.json
        │                    │
        │  ProjectJson.Load  │  GameDefinition.LoadBeside
        ▼                    ▼
   StoryProject          변수·이벤트 후보
   ├─ SetNode      조건 정의를 공급한다
   └─ DialogueNode
        ├─ LineBox[]        화자·대사·조건 전환
        ├─ BranchExits      갈래를 여는 줄 Id → 대상 노드
        └─ DefaultExit
        │
        │  ConditionFlowResolver.Resolve   ← 조건 모델의 유일한 해석자
        ▼
   DialogueFlow { ResolvedLine[], ConditionBranch[], FlowProblem[] }
        │
        ├─► NodeConnections.PortsOf ─► 그래프의 출력 포트와 간선
        └─► DialogueNodeEditor ─────► 카드의 들여쓰기·색·출구 표시

   모든 편집 ──► ProjectEditor ──► Changed(Structure|Content|Layout) ──► 두 화면이 다시 읽는다
```

**핵심 한 줄:** 조건 갈래도, 그래프 포트도 저장되지 않는다. 줄에 적힌 전환에서 매번 계산된다.
그래서 대사 화면과 그래프가 어긋날 자리가 없다.

---

## 3. 역인덱스 — "이걸 고치고 싶다" → 여는 파일

### 저작 모델

| 하고 싶은 일 | 여는 곳 |
|---|---|
| LineBox에 항목 추가(BGM·연출·메모 등) | `Vn.Authoring/Model/LineBox.cs` + 저장은 `Serialization/ProjectJson.cs` + 화면은 `Views/DialogueNodeEditor.axaml.cs` |
| 노드 종류 추가(연출 노드 등) | `Model/StoryNode.cs`에 파생 클래스 → `ProjectJson`의 `kind` 분기 → `NodeConnections.PortsOf` → 화면 |
| 설정 노드가 담는 것 | `Model/StoryNode.cs`의 `SetNode` |
| 조건의 이름·식 구조 | `Model/StoryNode.cs`의 `ConditionDefinition` |
| 식별자 형식(`nd_`, `ln_`, `cd_`) | `Model/Identifier.cs` |

### 조건과 갈래

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 조건 전환의 의미(if/elseif/endif) | `Flow/ConditionFlowResolver.cs` ← **이 파일이 조건 모델 그 자체다** |
| 중첩 조건 지원, 깊이 2 이상 | 같은 파일. 지금은 중첩을 문제로 알리고 같은 깊이로 다룬다 |
| `else` 갈래 추가 | `Model/LineBox.cs`의 `ConditionTransitionKind` + 위 해석자 + `Flow/ConditionChoices.cs` |
| 조건 드롭다운에 무엇을 보여 줄지 | `Flow/ConditionChoices.cs` |
| 갈래 색을 고르는 규칙 | `Flow/DialogueFlow.cs`의 `PaletteIndex` (자리) / `App/Views/BranchPalette.cs` (실제 색) |
| 갈래·출구가 잘못됐을 때의 알림 | `Flow/DialogueFlow.cs`의 `FlowProblemKind` |

### 출구와 그래프 연결

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 출구를 어디에 저장하는가 | `Model/StoryNode.cs`의 `DialogueNode.BranchExits` (여는 줄 Id를 열쇠로 쓴다) |
| 그래프 포트를 만드는 규칙 | `Flow/NodeConnections.cs` |
| 간선 라벨 문구 | `Flow/NodeConnections.cs`의 `LabelFor` |
| 연결·해제 동작 | `Editing/ProjectEditor.cs`의 `SetExitTarget` ← 그래프와 노드 화면이 함께 부른다 |
| 포트를 끌어 잇는 조작 | `App/Views/GraphEditorView.axaml.cs`의 `OnPortPressed` / `OnCanvasPointerReleased` |
| 간선 선택·삭제 | 같은 파일의 `SelectEdge` / `DeleteSelectedEdge` |
| 노드 카드 크기·포트 위치 계산 | 같은 파일 위쪽 상수와 `PortAnchor` / `InputAnchor` |

### 편집과 되돌리기

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 새 편집 명령 추가 | `Editing/ProjectEditor.cs` — **모델을 바꾸는 코드는 전부 여기에만 둔다** |
| 되돌리기 방식 | 같은 파일. 스냅샷(전체 직렬화)을 쌓는다 |
| 어떤 편집이 화면을 다시 만들게 할지 | `Editing/ProjectChangedEventArgs.cs`의 `ProjectChangeKind` |
| 새 노드가 파일 어디에 붙는지 | `ProjectEditor.AddNode` — 언제나 맨 뒤 |

### 저장 형식

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 파일에 항목 추가·삭제 | `Serialization/ProjectJson.cs` |
| 형식 버전을 올린다 | `Model/StoryProject.cs`의 `CurrentFormatVersion` + `ProjectJson.Read`의 버전 검사 |
| 게임별 정의 파일의 스키마 | `Definition/GameDefinition.cs` |

### 화면

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 도구 모음, 열기·저장, 창 제목 | `App/MainWindow.axaml{,.cs}` |
| 그래프 화면 전체 | `App/Views/GraphEditorView.axaml{,.cs}` |
| 대사 카드의 내용과 배치 | `App/Views/DialogueNodeEditor.axaml{,.cs}` |
| 설정 노드 화면 | `App/Views/SetNodeEditor.axaml{,.cs}` |
| 갈래·출구 색 | `App/Views/BranchPalette.cs` |
| 열린 프로젝트·저장 여부·선택 상태 | `App/Services/AuthoringSession.cs` |
| 최근 프로젝트 기억 | `App/Services/AppSettingsService.cs` |
| 시작 오류 로그 | `App/Services/StartupLog.cs` |

### Yarn 분석 (저작과 분리된 세계)

| 하고 싶은 일 | 여는 곳 |
|---|---|
| Yarn 진단·컴파일·구조 읽기 | `src/Vn.Core` — 자세한 것은 이 문서의 이전 판과 코드 주석 |
| 회귀용 `--format list` 출력 | `Vn.Core/Reporting/ListReportFormatter.cs` |
| 골든 비교 | `build-and-run.ps1`, `tests/Vn.Core.Tests/GoldenText.cs` |

---

## 4. 시스템 카드

### 4.1 조건 전환 모델 (`Vn.Authoring/Model`, `Flow`)

줄마다 "지금 어떤 조건 안인지"를 반복 저장하지 않는다. **바뀌는 지점만** 적고 나머지는 계산한다.

- `LineConditionTransition` — `BeginIf` / `BeginElseIf` / `EndIf`. 없으면 앞 줄 상태를 물려받는다.
- `BeginIf` 줄은 자기가 연 갈래 **안**에 있고, `EndIf` 줄은 이미 **바깥**이다.
- `BeginElseIf`는 중첩이 아니라 같은 체인의 형제다. **깊이가 늘지 않는다.**
- 첫 버전의 깊이는 0 또는 1뿐이다.
- 테스트: `ConditionFlowTests`, `ConditionChoiceTests`

### 4.2 출구 (`DialogueNode.BranchExits`)

조건 갈래 출구는 **갈래를 여는 줄의 Id**에 매단다. 마지막 줄에 매달지 않는다.

- 갈래에 줄을 더해도 출구는 갈래의 것으로 남고, 표시만 새 마지막 줄로 옮겨 간다.
- 그래서 "출구가 갈래 중간에 파묻히고 그 아래 대사가 실행되는" 모순이 생길 수 없다.
- 여는 줄이 사라지거나 전환이 바뀌면 `ProjectEditor.PruneBranchExits`가 출구를 함께 버린다.
- 기본 출구는 `StoryNode.DefaultExitTargetNodeId`이며 노드 종류를 가리지 않는다.
- 테스트: `ExitTests`

### 4.3 그래프 포트 (`Flow/NodeConnections`)

포트는 저장되지 않는다. 조건 전환에서 계산된다.

- 조건 갈래마다 포트 하나 + 노드마다 기본 포트 하나.
- 연결되지 않은 포트도 보인다. 그래야 작가가 그래프에서 끌어다 이을 수 있다.
- 그래프와 노드 화면 사이에 동기화 코드가 **없다.** 둘 다 같은 계산을 볼 뿐이다.
- 테스트: `ConnectionTests`

### 4.4 편집 (`Editing/ProjectEditor`)

모델을 바꾸는 유일한 통로. 화면끼리는 서로를 모른다.

- 되돌리기는 스냅샷 방식. 명령마다 역연산을 만들면 짝이 안 맞는 자리가 생긴다.
- 노드 드래그(`MoveNode`)는 되돌리기 기록을 남기지 않는다. 드래그 한 번에 수십 번 불린다.
- 변경 종류(`Structure`/`Content`/`Layout`)를 함께 알린다. 글자 한 자마다 카드 목록을
  다시 만들면 편집 중인 칸이 사라진다.
- 테스트: `EditingTests`

### 4.5 저장 형식 (`Serialization/ProjectJson`)

`*.vnstory.json`. 사람이 읽고 손으로 고칠 수 있는 것이 목적이다.

- 줄바꿈 LF 고정, BOM 없는 UTF-8, 한글은 이스케이프하지 않는다.
- 같은 상태는 언제나 같은 문자열이 된다. 그래야 diff가 편집한 곳에만 뜬다.
- 조건 출구는 갈래를 여는 줄 옆에 함께 적는다. 파일만 읽어도 갈래의 끝이 어디로 가는지 안다.
- 임시 파일에 쓰고 옮긴다. 저장 도중 죽어도 반쪽 원고가 남지 않는다.
- 테스트: `SerializationTests`, `SampleProjectTests`

### 4.6 게임별 정의 (`Definition/GameDefinition`)

`game.definition.json`이 변수·이벤트 후보를 공급한다.

- VnTool은 `favor` 같은 이름을 코드로 알지 못한다. 아는 순간 그 게임 전용이 된다.
- 파일이 없어도 저작은 계속된다. 후보가 없을 뿐이다.

---

## 5. 깨면 다른 곳이 조용히 부서지는 규칙

1. **조건 해석은 `ConditionFlowResolver` 하나만 쓴다.**
   화면·그래프·검증이 각자 계산하면 서로 다른 구조를 보여 주고, 작가는 어느 쪽이 맞는지 알 수 없다.

2. **색이나 좌표에서 조건을 거꾸로 추론하지 않는다.**
   방향은 언제나 전환 데이터 → 갈래 계산 → 표시다. 색은 데이터가 아니다.

3. **색만으로 정보를 전달하지 않는다.** 갈래에는 언제나 조건 이름이 함께 붙는다.

4. **조건 갈래 출구는 여는 줄에 매단다.** 마지막 줄에 매달면 줄을 하나 넣는 순간 의미가 무너진다.

5. **모델 변경은 `ProjectEditor`만 한다.** 화면이 모델을 직접 만지면 되돌리기와 알림이 빠진다.

6. **화면끼리 직접 이야기하지 않는다.** 그래프와 노드 편집기는 세션만 본다.

7. **파일 순서와 그래프 좌표는 별개다.** 새 노드는 언제나 파일 맨 뒤에 붙는다.
   시각적으로 위에 놓았다고 파일에서도 위로 가면 diff가 매번 크게 흔들린다.

8. **바인딩이 값을 넣을 때 나는 이벤트를 사용자 입력으로 보지 않는다.**
   `_building` 가드와 `ProjectChangeKind.Content`가 그 역할을 한다.

9. **XAML을 읽는 도중에도 컨트롤은 이벤트를 낸다.** `x:Name` 필드는 그 뒤에 채워진다.

10. **골든 파일은 언제나 UTF-8로 읽는다.** PowerShell 5.1은 BOM이 없으면 ANSI로 읽는다.
    한국어를 담은 `.ps1`은 UTF-8 BOM으로 저장해야 한다.

---

## 6. 자주 하는 작업 레시피

**LineBox에 새 항목을 붙인다 (예: BGM)**
1. `Model/LineBox.cs`에 속성 추가
2. `Serialization/ProjectJson.cs`의 쓰기·읽기에 항목 추가 (없으면 생략되도록)
3. `Editing/ProjectEditor.cs`에 편집 명령 추가 — 글자만 바뀌면 `ProjectChangeKind.Content`
4. `Views/DialogueNodeEditor.axaml.cs`의 `BuildCard`에 UI 추가
5. `SerializationTests`에 왕복 테스트 추가

**노드 종류를 추가한다 (예: 연출 노드)**
1. `Model/StoryNode.cs`에 파생 클래스와 `Clone()`
2. `ProjectJson`의 `kind` 분기 양쪽
3. `NodeConnections.PortsOf`에 포트 규칙
4. `Views/`에 편집기 하나, `MainWindow.ShowSelectedNode`에 분기
5. `GraphEditorView.BuildCard`의 배지·색

**조건 모델을 넓힌다 (예: else, 중첩)**
1. `ConditionTransitionKind`에 종류 추가
2. `ConditionFlowResolver`의 상태 기계 수정 — 깊이가 늘어나면 `ResolvedLine.Depth`의 뜻이 바뀐다
3. `ConditionChoices`에 드롭다운 규칙
4. `ProjectJson`의 `kind` 문자열
5. `ConditionFlowTests`에 예시부터 추가

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

`build-and-run.ps1`은 Yarn 분석 쪽 골든 픽스처까지 확인한다.
저작 도메인은 `dotnet test`가 전부 덮는다.

창이 뜨지 않으면 `%LOCALAPPDATA%\VnTool\logs\startup-error.log`를 먼저 본다.

손으로 쓴 예제 프로젝트는 `samples/Authoring/story.vnstory.json`이다.
저장 형식을 고치면 `SampleProjectTests`가 먼저 깨진다.
