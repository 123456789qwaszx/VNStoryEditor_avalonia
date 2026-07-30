# VnTool Step 1 — Vn.Core + Vn.Cli

Yarn Spinner를 스토리의 단일 원본으로 유지하면서, UI 없이 다음 수직 흐름을 검증하는 첫 단계입니다.

```text
.yarnproject + .yarn + game.schema.json
                    ↓
          YarnSpinner.Compiler
                    ↓
     노드 / jump / 변수 / 명령 추출
                    ↓
     게임 스키마 기반 추가 검증
                    ↓
      파일·줄·열이 있는 CLI 진단
```

## 포함된 결과물

```text
VnTool.sln
├─ src/
│  ├─ Vn.Core/
│  │  ├─ Analysis/
│  │  ├─ Diagnostics/
│  │  ├─ Schema/
│  │  ├─ Story/
│  │  ├─ Validation/
│  │  └─ Yarn/
│  └─ Vn.Cli/
└─ samples/
   ├─ Valid/
   └─ Invalid/
```

`Vn.Core`의 공개 모델은 Yarn 컴파일러 타입을 노출하지 않습니다. Yarn 타입은 `Yarn/` 경계 안에서 자체 `VnDiagnostic`, `StoryNode`, `StoryJump`로 변환됩니다.

## 요구 환경

- .NET 8 SDK
- 인터넷 연결: 최초 `dotnet restore`에서 NuGet 패키지를 받을 때 필요

## 실행

저장소 루트에서:

```powershell
dotnet restore
dotnet build

dotnet run --project src/Vn.Cli -- `
  samples/Valid/Demo.yarnproject `
  samples/Valid/game.schema.json
```

오류 예제:

```powershell
dotnet run --project src/Vn.Cli -- `
  samples/Invalid/Demo.yarnproject `
  samples/Invalid/game.schema.json
```

스키마 경로를 생략하면 `.yarnproject` 옆의 `game.schema.json`을 찾습니다.

```powershell
dotnet run --project src/Vn.Cli -- samples/Valid/Demo.yarnproject
```

## 종료 코드

- `0`: 오류 없음
- `1`: 오류 진단 존재
- `64`: 잘못된 CLI 인자

CI나 Unity 외부 빌드 검사에서도 사용할 수 있습니다.

## 현재 구현 범위

- `.yarnproject` 로딩
- 프로젝트가 포함하는 `.yarn` 파일 탐색
- YarnSpinner.Compiler 전체 컴파일
- 스키마 변수를 외부 Yarn 변수 선언으로 주입
- Yarn 기본 진단을 자체 진단 모델로 변환
- 노드 목록 추출
- jump 관계 추출
- 노드에서 사용한 변수와 명령 이름 추출
- 스키마에 없는 변수·명령 검사
- 존재하지 않는 이동 대상 노드 검사
- Levenshtein 거리 기반 `입력하려던 것인지` 제안
- 진단의 결정론적 정렬
- 오류 유무에 따른 프로세스 종료 코드

## 의도적으로 구현하지 않은 범위

이 단계에서는 아래 기능을 넣지 않았습니다.

- WPF UI
- AvalonEdit
- 명령 파라미터 개수·타입 검증
- Yarn definitions JSON 생성
- Unity용 데이터 내보내기
- Unity 패키지 또는 DLL 공유
- 자동 저장
- FileSystemWatcher
- Git
- 그래프 UI
- 테스트 프로젝트

특히 명령 인자 검증은 단순 문자열 분해로 구현하지 않았습니다. 다음 단계에서 Yarn 파서 결과나 공식 definitions 경로를 사용해 안전하게 추가하는 것이 맞습니다.

## 다음 단계

1. 이 솔루션을 실제 Windows/.NET 환경에서 빌드한다.
2. 사용 중인 Yarn Spinner Unity 패키지와 컴파일러 버전을 맞춘다.
3. 실제 게임의 `.yarnproject`, `.yarn`, 스키마로 CLI를 돌린다.
4. 진단 위치와 메시지 품질을 조정한다.
5. 그다음 `Vn.App` WPF 스파이크를 추가한다.
