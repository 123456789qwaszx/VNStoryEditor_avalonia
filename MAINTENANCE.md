# VnTool 수정 지점 지도

이 문서는 "무엇을 고치고 싶을 때 어느 파일을 여는가"에만 답한다.
세부 코드를 읽기 전에 이 문서에서 대상 클래스를 먼저 특정하는 용도다.

제품 개요와 원칙은 [README.md](README.md)에 있다. 여기서는 구조만 다룬다.

---

## 1. 프로젝트 네 개와 의존 방향

```
Vn.Core   ── Yarn을 읽고 분석한다. 화면도 파일 쓰기도 모른다.
   ▲
   ├── Vn.Cli    콘솔 출력. 골든 픽스처의 생산자.
   └── Vn.App    Avalonia 데스크톱 앱. 파일 쓰기는 전부 여기.

tests/Vn.Core.Tests   분석 결과와 골든 픽스처
tests/Vn.App.Tests    앱 서비스와 뷰 모델
```

의존은 한 방향뿐이다. **Vn.Core는 Vn.App을 절대 참조하지 않는다.**
분석은 CLI에서도 테스트에서도 창 없이 돌아가야 하기 때문이다.

| 무엇이 어디 있나 | 위치 |
|---|---|
| 읽기·분석·진단 | `src/Vn.Core` |
| 파일 쓰기·저장·설정 | `src/Vn.App/Services` |
| 화면 | `src/Vn.App/Views`, `src/Vn.App/MainWindow.axaml*` |
| 회귀 출력 형식 | `src/Vn.Core/Reporting` |

---

## 2. 데이터가 흐르는 길

```
.yarnproject + game.schema.json
        │
        │  GameSchemaLoader ─────────────► GameSchema        (+ VN1xxx 진단)
        │  YarnCompilerAdapter ──────────► CompilationResult (+ YS/VN2xxx 진단)
        │        ├ YarnSymbolIndex   변수·명령이 쓰인 줄·열
        │        ├ YarnLineIndex     재생되는 라인 (평평한 목록)
        │        └ YarnBlockIndex    같은 본문을 분기 트리로   ← YarnBlockScanner
        │  SchemaUsageValidator ────────► VN3xxx
        │  WritingConventionValidator ──► VN5xxx
        ▼
   AnalysisReport { SourceFiles, Nodes, Diagnostics }
        │
        ├─► Vn.Cli          ListReportFormatter → samples/Real/expected.txt 비교
        └─► ProjectSession  ─┬─► AnalysisView  장면·파일·문제 목록 + 구조 카드 + 원문
                             ├─► BoxListView   BoxItem / BlockItem / BranchItem
                             └─► GraphView     노드 상자와 점프 간선
                                    │
                       편집 ────────┘
                        │
        OpenDocumentSession.WorkingText  (텍스트 탭과 구조 탭이 공유하는 단 하나의 문자열)
                        │
        StoryLineReplacer → StoryFileService.Write → .yarn  (인코딩·BOM·줄바꿈 보존)
```

**핵심 한 줄:** 분석 결과는 저장하지 않는다. 언제나 `.yarn`에서 다시 계산한다.
화면 모델(`StoryLine`, `StoryBlock`, `BoxItem`)은 전부 **손실 압축**이며 이것으로 파일을 재생성하지 않는다.

---

## 3. 역인덱스 — "이걸 고치고 싶다" → 여는 파일

### 분석·진단

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 진단 코드 새로 추가 | `Vn.Core/Diagnostics/VnDiagnosticCodes.cs` (코드 정의) → 실제로 내는 검사기 |
| 진단 메시지 문구 수정 | 그 진단을 만드는 검사기. 문구는 골든 픽스처에 걸려 있지 않으므로 자유롭게 고쳐도 된다 |
| 진단 정렬 순서 변경 | `Vn.Core/VnProjectAnalyzer.cs` `SortDiagnostics` |
| 진단 심각도 변경 | 그 진단을 만드는 자리. Error는 CLI 종료 코드 1을 만든다 |
| "알 수 없는 변수/명령" 규칙 | `Vn.Core/Validation/SchemaUsageValidator.cs` |
| 오타 추천("~를 입력하려던 것인지") | `Vn.Core/Validation/NameSuggester.cs` |
| 작성 규약 경고(VN5xxx) 추가·완화 | `Vn.Core/Validation/WritingConventionValidator.cs` |
| 선언 없이 써도 되는 내장 명령 | `Vn.Core/Yarn/YarnBuiltIns.cs` |
| Yarn 컴파일러 진단을 우리 형태로 옮기는 규칙 | `Vn.Core/Yarn/YarnDiagnosticMapper.cs` |

### Yarn 읽기

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 화자·해시태그·본문을 잘라내는 규칙 | `Vn.Core/Yarn/YarnLineIndex.cs` |
| 들여쓰기 몇 칸이 깊이 1인가 | `YarnLineIndex.SpacesPerDepth`와 `YarnBlockScanner.SpacesPerDepth` **둘 다** |
| 줄을 대사/선택지/명령/조건으로 분류 | `Vn.Core/Yarn/YarnBlockScanner.cs` (`YarnLineKind`) |
| 선택지·조건 블록을 트리로 접는 규칙 | `Vn.Core/Yarn/YarnBlockIndex.cs` |
| 갈래의 `Destination`을 올리는 조건 | `Vn.Core/Yarn/YarnBlockIndex.cs` |
| 변수·명령의 줄·열 위치 | `Vn.Core/Yarn/YarnSymbolIndex.cs` |
| 컴파일 옵션, 노드 추출, 점프 목록 | `Vn.Core/Yarn/YarnCompilerAdapter.cs` |
| `(External)` 같은 비-파일 경로 처리 | `Vn.Core/Yarn/YarnPaths.cs` |
| YarnSpinner 버전 올리기 | `Directory.Packages.props` → `YarnDiagnosticMapper`, `YarnBuiltIns` 함께 확인 |

### 게임 스키마

| 하고 싶은 일 | 여는 곳 |
|---|---|
| `game.schema.json`에 필드 추가 | `Vn.Core/Schema/GameSchema.cs` |
| 스키마 파일 검증 규칙(VN1xxx) | `Vn.Core/Schema/GameSchemaLoader.cs` |
| 변수 타입 추가(`bool`/`number`/…) | `Vn.Core/Schema/SchemaTypeMapper.cs` |

### CLI와 골든 픽스처

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 사람이 읽는 `--format text` 출력 | `src/Vn.Cli/Program.cs` `PrintText` |
| 회귀용 `--format list` 출력 항목 | `Vn.Core/Reporting/ListReportFormatter.cs` ← **여기가 골든 픽스처의 정의다** |
| 경로 표기(`/` 통일, 상대 경로) | `Vn.Core/Reporting/StablePath.cs` |
| CLI 인자·종료 코드 | `src/Vn.Cli/Program.cs` `Run`, `TryParseArguments` |
| 골든 비교 방식·실패 메시지 | `build-and-run.ps1`, `tests/Vn.Core.Tests/GoldenText.cs` |
| 기준선 자체를 갱신 | `samples/Real/expected.txt` — **원인을 먼저 확인한 뒤에만** |

### 앱 상태와 저장

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 프로젝트 열기·재분석·선택 상태 | `Vn.App/Services/ProjectSession.cs` |
| 상태 줄 문구 | `ProjectSession` (상태 문자열의 유일한 소유자) |
| 편집 중 텍스트, dirty 판정, 외부 변경 감지 | `Vn.App/Services/OpenDocumentSession.cs` |
| 저장 시 인코딩·BOM·줄바꿈 보존 | `Vn.App/Services/StoryFileService.cs` |
| 한 줄만 갈아 끼우는 부분 교체 | `Vn.App/Services/StoryLineReplacer.cs` |
| 최근 프로젝트·최근 장면 기억 | `Vn.App/Services/AppSettingsService.cs` |
| 그래프 좌표 저장 | `Vn.App/Services/WorkspaceService.cs` (`vn.workspace.json`) |
| 시작 오류 로그 | `Vn.App/Services/StartupLog.cs` |

### 화면

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 상단 도구 모음, 창 제목, 종료 확인 | `Vn.App/MainWindow.axaml{,.cs}` |
| 장면·파일·문제 목록의 내용 | `Vn.App/Views/AnalysisView.axaml.cs` — `NodeListItem` / `SourceFileListItem` / `DiagnosticListItem` |
| 목록·탭의 배치와 모양 | `Vn.App/Views/AnalysisView.axaml` |
| 장면 검색 필터 | `AnalysisView.ApplyNodeFilter` |
| 구조 카드에 무엇이 보이는가 | `Vn.App/Views/BoxListView.axaml.cs` — `BoxItem` / `BlockItem` / `BranchItem` |
| 구조 카드의 모양 | `Vn.App/Views/BoxListView.axaml` |
| 카드 잠금 사유 문구 | `BoxItem` 생성자 |
| 그래프 배치·간선·드래그 | `Vn.App/Views/GraphView.axaml{,.cs}` |
| 저장 안 함 / 외부 변경 대화상자 | `AnalysisView.ShowUnsavedDialogAsync`, `ConfirmExternalOverwriteAsync` |
| 시작 실패 안내 창 | `Vn.App/App.axaml.cs` |
| 앱 시작 순서, 최상위 예외 | `Vn.App/Program.cs` |

---

## 4. 시스템 카드

### 4.1 Yarn 읽기 (`Vn.Core/Yarn`)

컴파일러 결과를 우리 모델로 옮기는 층. 이 저장소에서 가장 조심스러운 곳이다.

- 진입점 `YarnCompilerAdapter.Compile`
- 세 인덱스가 **같은 `CompilationResult`를 각자 훑는다.** 일부러 합치지 않았다.
  `YarnSymbolIndex`는 진단 위치의 원본이라 건드려 어긋나면 모든 진단이 엉뚱한 곳을 가리킨다.
  `YarnBlockScanner`는 라인 뒤에 남은 명령을 버리지 않아야 해서 `YarnLineIndex`와 규칙이 다르다.
- **`CompilationResult.StringTable`을 쓰지 않는다.** 텍스트가 이미 가공되어 있어
  (`{$favor}` → `{0}`, 조건부 선택지의 조건식 제거) 그대로 저장하면 원고가 파괴된다.
  원본은 토큰 위치로 잘라 쓴다.
- 테스트: `StoryLineTests`, `StoryBlockTests`, `StoryBlockShapeTests`, `YarnBlockScannerTests`,
  `BranchCommandScopeTests`, `BranchCommandDuplicationTests`, `BlankLineAfterOptionTests`

### 4.2 이야기 모델 (`Vn.Core/Story`)

`Vn.Core`의 공개 계약. 화면·CLI·테스트가 전부 이 타입에 의존한다.

- `StoryNode.Lines` — 평평한 라인 목록
- `StoryNode.Body` — **같은 본문**을 분기 트리로 본 것 (`StoryElement` = 라인 | 블록)
- 두 표현은 **같은 `StoryLine` 객체를 공유한다.** 한쪽만 고치면 두 모델이 다른 말을 한다.
- `StoryLine.CommandsSincePreviousLine`은 텍스트 순서일 뿐 실행 순서가 아니다.
  실행 관계는 `Body` 트리가 정한다.
- 필드를 추가하면 `ListReportFormatter`에 낼지 결정해야 하고, 내면 `expected.txt`가 바뀐다.

### 4.3 진단 (`Vn.Core/Diagnostics`)

- 코드 문자열은 `VnDiagnosticCodes`에만 정의한다. 문서·억제 규칙에 노출되는 영구 계약이다.
- 형태는 `접두사 두 글자 + 숫자` 한 가지뿐. 형태가 갈라지면 문서·검색·억제가 함께 갈라진다.
- 대역: `VN1` 스키마 / `VN2` Yarn 인프라 / `VN3` 변수·명령 / `VN4` 노드 그래프 /
  `VN5` 작성 규약(전부 Warning) / `YS` Yarn 컴파일러 통과

### 4.4 회귀 출력 (`Vn.Core/Reporting`)

- `ListReportFormatter.Format`이 골든 픽스처의 **정의 자체**다. CLI는 받아 적기만 한다.
- 메시지 문구는 일부러 빼 놓았다. 문구를 픽스처에 걸면 문구를 고칠 때마다 깨지고,
  결국 문구를 안 고치게 된다.
- 이 파일을 고치면 `samples/Real/expected.txt`가 반드시 바뀐다. 의도한 변경인지 확인할 것.

### 4.5 앱 상태 (`ProjectSession`)

앱에서 유일하게 "지금 무엇이 열려 있는가"를 아는 객체.

- 뷰는 상태를 복제하지 않는다. `StateChanged` / `AnalysisChanged` 두 이벤트로만 받는다.
- 분석은 `Task.Run`으로 나가고 **세대 번호**로 늦게 끝난 결과가 최신 상태를 덮지 못하게 한다.
- 분석에 실패해도 원문은 계속 열어 준다. 저작 도구에서 검사 실패가 편집을 막으면 안 된다.
- 테스트: `ProjectSessionTests`

### 4.6 저장 (`OpenDocumentSession` → `StoryLineReplacer` → `StoryFileService`)

- 텍스트 탭과 구조 탭은 `WorkingText` **하나**를 공유한다. 변경 목록을 따로 들지 않으므로
  한쪽 수정이 저장 때 사라질 수 없다.
- 쓰기는 언제나 원본 문자열의 **부분 교체**다. 모델로 파일을 재생성하지 않는다.
- `StoryFileService`는 읽을 때 본 인코딩·BOM·줄바꿈을 그대로 복원한다.
  줄바꿈은 정규화조차 하지 않는다 — 한 파일에 CRLF와 LF가 섞여 있으면 반드시 깨지기 때문이다.
- 임시 파일에 다 쓰고 교체한다. 도중에 죽어도 반만 남은 원고가 생기지 않는다.
- 외부에서 파일이 바뀌었으면 SHA-256 지문으로 잡아내고 묻기 전에는 덮어쓰지 않는다.
- 테스트: `OpenDocumentSessionTests`, `StoryFileServiceTests`, `StoryLineEditorTests`

### 4.7 화면 (`Vn.App/Views`)

- `AnalysisView` — 왼쪽 목록 3종 + 구조/원문 탭. 목록에 보이는 문자열은 `*ListItem` 클래스가 만든다.
- `BoxListView` — 구조 카드. `BoxItem`(대사) / `BlockItem`(분기) / `BranchItem`(갈래).
  `BranchItem.Children`이 다시 `BoxListView.BuildChildren`을 부르는 재귀 구조다.
- `GraphView` — 캔버스에 노드 상자와 점프 간선. `<<jump>>`만 간선으로 그린다.
- 테스트: `BoxItemTests`, `BoxEditLoopTests`

### 4.8 시작과 실패 처리 (`Program`, `App`, `StartupLog`)

- `Program.Main` → `App.OnFrameworkInitializationCompleted` → `MainWindow` → `OnOpened`에서 최근 프로젝트 복원.
- 어느 단계가 실패해도 조용히 사라지지 않는다. `%LOCALAPPDATA%\VnTool\logs\startup-error.log`에 남기고,
  주 창을 못 만들면 대신 안내 창을 띄운다.
- 최근 프로젝트 복원은 실패해도 빈 창을 유지한다.
- 테스트: `StartupLogTests`, `AppSettingsServiceTests`

---

## 5. 깨면 다른 곳이 조용히 부서지는 규칙

1. **경로 정규화는 `YarnPaths.Normalize` 하나만 쓴다.**
   진단·노드·점프·심볼 인덱스가 전부 같은 문자열로 서로를 찾는다. 한쪽만 다르게 정규화하면 조회가 어긋난다.

2. **`SpacesPerDepth`는 두 곳에 있고 값이 같아야 한다.**
   `YarnLineIndex`와 `YarnBlockScanner`. 어긋나면 평평한 목록과 트리가 다른 깊이를 말한다.

3. **화면 모델로 Yarn을 재조립하지 않는다.**
   `StoryLine`·`StoryBlock`·`BoxItem`은 표시용 손실 압축이다. 쓰기는 부분 교체 경로여야 한다.

4. **바인딩이 값을 넣을 때 나는 이벤트를 사용자 입력으로 보지 않는다.**
   `TextChanged`는 초기 바인딩에서도 난다. `BoxItem.HasPendingChange`처럼 실제 변화만 편집으로 취급하지 않으면
   "편집 → 거부 → 다시 그리기 → 또 편집" 무한 루프로 창이 굳는다.

5. **XAML을 읽는 도중에도 컨트롤은 이벤트를 낸다.**
   `x:Name` 필드는 XAML을 다 읽은 뒤 채워지므로, 그전에 핸들러가 필드를 만지면 창이 생기기 전에 앱이 죽는다.
   `AnalysisView.IsReady` 가드가 그것을 막는다. 새 뷰에 `SelectionChanged`류 핸들러를 붙일 때 같은 가드가 필요하다.

6. **`async void` 핸들러에서 예외를 새게 두지 않는다.**
   잡아 줄 곳이 없어 곧장 프로세스를 죽인다. 창이 뜬 뒤라도 마찬가지다.

7. **골든 파일은 언제나 UTF-8로 읽는다.**
   Windows PowerShell 5.1은 BOM이 없으면 시스템 ANSI(한국어 949)로 읽어 한글을 망가뜨린다.
   `.ps1` 파일 자체도 한국어를 담으면 **UTF-8 BOM으로 저장**해야 한다.

8. **`samples/Real/expected.txt`는 LF로 고정된다** (`.gitattributes`).
   체크아웃 환경 때문에 바이트가 달라지면 "분석 결과가 바뀐 것"과 구분할 수 없다.

---

## 6. 자주 하는 작업 레시피

**새 진단을 추가한다**
1. `VnDiagnosticCodes`에 상수 추가 (대역 규칙에 맞게)
2. 검사기에서 `VnDiagnostic`을 만든다 — 스키마면 `GameSchemaLoader`, 사용이면 `SchemaUsageValidator`, 규약이면 `WritingConventionValidator`
3. Warning인지 Error인지 정한다. Error는 CLI 종료 코드를 1로 만든다
4. `Vn.Core.Tests`에 케이스 추가
5. `samples/Real`에 걸리면 `expected.txt`의 `diag` 줄이 늘어난다 — 늘어난 것이 맞는지 확인하고 갱신

**구조 카드에 정보를 하나 더 보여준다**
1. `BoxItem`(또는 `BranchItem`)에 읽기 전용 속성 추가
2. `BoxListView.axaml`의 `DataTemplate`에 바인딩 추가
3. 편집 가능한 값이면 `HasPendingChange`가 그 값도 보게 할지 판단한다

**새 화면 탭을 추가한다**
1. `Views/`에 `UserControl` 추가
2. `MainWindow.axaml`의 `WorkspaceTabs`에 `TabItem`으로 붙인다
3. `MainWindow`에서 `_session`의 이벤트를 구독해 갱신한다. 화면이 상태를 따로 들지 않게 한다
4. 초기화 중 이벤트 가드(규칙 5)를 넣는다

**저장 형식과 관련된 것을 만진다**
→ `StoryFileServiceTests`를 먼저 읽는다. 인코딩·BOM·줄바꿈 조합이 이미 케이스로 고정되어 있다.

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

`build-and-run.ps1`은 빌드·테스트에 더해 Valid/Invalid 샘플의 종료 코드와
Real 샘플의 골든 픽스처까지 확인한다. 픽스처가 어긋나면 몇 번째 줄이 어떻게 다른지,
몇 바이트째인지까지 출력한다(탭은 `»`로 보인다).

앱을 실제로 띄워 확인할 때:

```bash
dotnet run --project .\src\Vn.App\Vn.App.csproj
```

창이 뜨지 않거나 즉시 사라지면 `%LOCALAPPDATA%\VnTool\logs\startup-error.log`를 먼저 본다.
