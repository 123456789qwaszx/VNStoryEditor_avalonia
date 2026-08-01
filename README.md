# VnTool

비주얼노벨 시나리오 작가가 Unity나 IDE 없이 이야기 구조를 만들고, 대사를 쓰고, 흐름을 잇기 위한 Avalonia 데스크톱 저작 도구입니다.

특정 게임의 보조 도구가 아니라 여러 게임에서 반복해서 쓰는 공통 저작 기반을 목표로 합니다. 게임마다 달라지는 변수·이벤트는 프로젝트 옆의 `game.definition.json`이 공급하고, 도구 코드에는 특정 게임의 이름이 들어가지 않습니다.

코드를 고칠 때는 [ARCHITECTURE.md](ARCHITECTURE.md)를 먼저 봅니다. 구조가 왜 이렇게 되었는지, 각 조각이 무슨 일을 하는지, 어떤 기능을 손보려면 어느 파일을 여는지 정리한 문서입니다.

## 저작 흐름

1. 그래프에서 설정 노드를 만들고 조건을 정의합니다.
2. 대사 노드를 만들고 LineBox를 위에서 아래로 씁니다.
3. 줄의 조건 드롭다운으로 `if` 갈래를 열고, 다른 조건을 고르면 같은 깊이의 `elseif`로 넘어갑니다.
4. 갈래마다 다른 노드로 가는 출구를, 노드 전체에는 기본 출구를 답니다.
5. 그래프에서 포트를 끌어 노드를 잇습니다. 간선에는 조건 이름이 표시됩니다.
6. Script Preview 탭에서 그 노드가 어떤 문서로 펼쳐지는지 읽기 전용으로 확인합니다.
7. 저장하면 프로젝트 manifest와 StoryFile별 `*.vnstory.json`으로 나뉘어 들어갑니다.

## 저장 구조

```text
MyStory/
├─ project.vnproject.json
├─ game.definition.json
└─ story/
   ├─ chapter01.vnstory.json
   └─ side-events.vnstory.json
```

- manifest에는 제목, 시작 노드, StoryFile의 Id·이름·상대 경로만 들어갑니다.
- 각 StoryFile은 자신이 소유한 노드만 담습니다. 한 장을 고치면 그 파일만 바뀝니다.
- 되돌리기와 저장 여부 비교는 디스크 배치와 무관한 `ProjectSnapshotCodec`이 맡습니다.
- 예전 `*.vnstory.json` 한 덩어리 파일도 열 수 있고, 다음 저장에서 새 구조로 옮겨집니다.
- JSON은 BOM 없는 UTF-8, LF, 결정적인 키·목록 순서를 씁니다.

## 설계 원칙

- **저작 모델이 공식 원본입니다.** 화면과 그래프는 그것을 보는 두 가지 방법일 뿐입니다.
- **조건은 줄마다 반복 저장하지 않습니다.** 흐름이 바뀌는 지점만 적고 나머지는 계산합니다.
- **조건 갈래와 그래프 포트는 저장되지 않습니다.** 매번 계산하므로 두 화면이 어긋날 수 없습니다.
- **조건 갈래 출구는 갈래를 여는 줄에 매답니다.** 줄을 넣거나 옮겨도 출구가 갈래 중간에 파묻히지 않습니다.
- **색은 데이터가 아닙니다.** 갈래에는 언제나 조건 이름이 함께 표시됩니다.
- **파일 순서와 그래프 좌표는 별개입니다.** 새 노드는 언제나 파일 맨 뒤에 붙습니다.
- **게임별 정보는 코드에 넣지 않습니다.** 정의 파일이 없으면 후보 없이 직접 적을 뿐, 저작은 계속됩니다.

## 첫 버전의 범위

지원합니다.

- 설정 노드의 조건·변수 정의, 대사 노드의 LineBox 편집
- `if` / `elseif` / `endif` 한 단계 조건 갈래 (깊이 0 또는 1)
- 조건 갈래 출구와 노드 기본 출구
- 그래프에서 노드 추가·이동·연결·간선 삭제
- 대사 노드를 평평한 Yarn 스타일 문서로 펼쳐 보는 읽기 전용 Preview
- 되돌리기와 다시 실행, 저장과 열기

아직 지원하지 않습니다.

- 중첩 조건, `else` 갈래, 줄마다 임의 조건식 입력
- 연출 노드, BGM·사운드·카메라 같은 LineBox 확장 항목
- Yarn 가져오기와 실제 내보내기 (Preview는 읽기 전용이며 파일을 만들지 않습니다)

## 실행

```powershell
dotnet run --project .\src\Vn.App\Vn.App.csproj
```

예제 프로젝트는 `samples/Authoring/project.vnproject.json`입니다. 손으로 쓴 파일이며 저장 형식이 사람이 읽고 고칠 수 있다는 것을 보여 줍니다.

## 검증

```powershell
dotnet build .\VnTool.sln
dotnet test .\VnTool.sln
powershell -ExecutionPolicy Bypass -File .\build-and-run.ps1
```

## Yarn 분석 도구

`Vn.Core`와 `Vn.Cli`는 Yarn 프로젝트를 읽고 검증하는 별도의 도구로 남아 있습니다. 저작 경로와 분리되어 있으며, 앞으로 기존 원고를 새 형식으로 가져오는 통로가 됩니다.

```powershell
dotnet run --project .\src\Vn.Cli\Vn.Cli.csproj -- .\samples\Real\Demo.yarnproject .\samples\Real\game.schema.json
```

## 작가에게 전달할 Windows 휴대용 패키지

```powershell
powershell -ExecutionPolicy Bypass -File .\publish-windows.ps1
```

완료되면 `artifacts\VnTool-win-x64.zip`이 만들어집니다. .NET 런타임을 함께 포함하므로 대상 PC에 개발 환경이 필요 없습니다.
