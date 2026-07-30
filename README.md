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
   ├─ Valid/      오류 없음
   ├─ Invalid/    Yarn 쪽 오타 (변수·명령·jump 대상)
   └─ Malformed/  스키마 자체가 망가진 경우
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

## 진단 코드 체계

진단 코드는 문서와 억제 규칙에 그대로 노출되는 영구 계약입니다. 정의는
[`VnDiagnosticCodes`](src/Vn.Core/Diagnostics/VnDiagnosticCodes.cs) 한 곳에만 있습니다.

| 구간 | 의미 |
| --- | --- |
| `VN1xxx` | 게임 스키마 로드·형식 |
| `VN2xxx` | Yarn 프로젝트·컴파일 인프라 |
| `VN3xxx` | 변수·명령 사용 |
| `VN4xxx` | 노드 그래프 |
| `YS####` | Yarn 컴파일러가 낸 진단을 통과시킨 것 (원본 코드 보존) |

핵심은 접두사만 보고 **우리가 낸 진단(`VN`)** 과 **Yarn이 낸 진단(`YS`)** 을 구분할 수 있다는
점입니다. "Yarn이 낸 건 억제하고 우리가 낸 건 유지" 같은 규칙이 이 구분 위에서 동작합니다.

| 코드 | 뜻 |
| --- | --- |
| `VN1001` | 스키마 파일을 찾을 수 없음 |
| `VN1002` | 스키마 내용이 비어 있음 |
| `VN1003` | 스키마 JSON 파싱 실패 |
| `VN1004` | 스키마 파일 읽기 실패 |
| `VN1010` | `schemaVersion`이 1 미만 |
| `VN1011` | 변수 id 중복 |
| `VN1012` | 명령 id 중복 |
| `VN1013` | 변수 id가 비어 있음 |
| `VN1014` | 지원되지 않는 변수 타입 |
| `VN1015` | 명령 id가 비어 있음 |
| `VN1016` | 같은 id가 `commands`와 `eventTypes` 양쪽에 존재 |
| `VN1017` | `default` 값이 선언된 타입과 불일치 |
| `VN2001` | `.yarnproject`를 찾을 수 없음 |
| `VN2002` | `.yarnproject`에 소스 파일이 없음 |
| `VN2003` | Yarn 처리 중 예상하지 못한 오류 |
| `VN3001` | 알 수 없는 변수 |
| `VN3002` | 알 수 없는 명령 |
| `VN4001` | 알 수 없는 jump 대상 노드 |

억제 문법(`// vn:disable VN3001`)은 아직 구현하지 않았습니다. 다만 구현될 자리를 전제로
코드 체계를 잡아두었습니다.

### 같은 문제에 진단이 둘 나올 수 있습니다

`$affection_an` 같은 오타에는 Yarn의 `YS0003`과 우리 `VN3001`이 같은 위치에 함께 나옵니다.
Yarn의 원본 메시지를 잃지 않기 위해 의도적으로 둘 다 표시합니다.
접두사로 구분되므로 나중에 한쪽만 걸러내는 것은 언제든 가능합니다.

## 회귀 검증

```powershell
./build-and-run.ps1
```

빌드한 뒤 `samples/` 아래 샘플마다 **종료 코드와 전체 출력**을 확인합니다.
기대 출력은 `samples/<이름>/expected.txt`에 고정돼 있고, 절대 경로는 `<root>`로 치환해
어느 머신에서도 같은 결과가 나오도록 정규화합니다.

테스트 프로젝트가 없어도 이 골든 픽스처가 "진단이 조용히 바뀌는 일"을 막아줍니다.
나중에 Unity 쪽과 진단 일치를 검증할 때도 이 파일이 기준이 됩니다.

진단을 의도적으로 바꿨다면 다음으로 갱신하고, **diff는 반드시 눈으로 확인하세요.**

```powershell
./build-and-run.ps1 -Update
```

## 현재 구현 범위

- `.yarnproject` 로딩
- 프로젝트가 포함하는 `.yarn` 파일 탐색
- YarnSpinner.Compiler 전체 컴파일
- 스키마 변수를 외부 Yarn 변수 선언으로 주입
- Yarn 기본 진단을 자체 진단 모델로 변환
- 노드 목록 추출
- jump 관계 추출
- 노드에서 사용한 변수와 명령을 **실제 사용 줄·열과 함께** 추출
- 스키마에 없는 변수·명령 검사
- 존재하지 않는 이동 대상 노드 검사
- Levenshtein 거리 기반 `입력하려던 것인지` 제안
- 진단의 결정론적 정렬
- 오류 유무에 따른 프로세스 종료 코드
- 손으로 편집한 스키마에 대한 방어 (`null` id·타입, 중복 선언, 타입과 안 맞는 default)
- 샘플 출력 골든 픽스처 비교

진단 위치는 노드 헤더가 아니라 이름이 실제로 쓰인 지점을 가리킵니다.
`CompilationResult.ParseResults`의 렉서 토큰에서 위치를 뽑기 때문에
Yarn 내부 API에 의존하지 않으면서도 `Vn.App`의 "오류 클릭 → 캐럿 이동"을 바로 지탱할 수 있습니다.
같은 노드에서 같은 오타를 두 번 쓰면 진단도 두 개 나옵니다. 고칠 곳이 두 군데이기 때문입니다.

## 의도적으로 구현하지 않은 범위

이 단계에서는 아래 기능을 넣지 않았습니다.

- 데스크톱 UI (`Vn.App`)와 에디터 컨트롤
- 명령 파라미터 개수·타입 검증
- 진단 억제 문법 (`// vn:disable VN3001`)
- Yarn definitions JSON 생성
- Unity용 데이터 내보내기
- Unity 패키지 또는 DLL 공유
- 자동 저장
- FileSystemWatcher
- Git
- 그래프 UI
- 테스트 프로젝트 (`build-and-run.ps1`의 골든 픽스처가 그 자리를 대신하고 있습니다)

특히 명령 인자 검증은 단순 문자열 분해로 구현하지 않았습니다. 다음 단계에서 Yarn 파서 결과나 공식 definitions 경로를 사용해 안전하게 추가하는 것이 맞습니다.

## 다음 단계

1. 사용 중인 Yarn Spinner Unity 패키지와 컴파일러 버전을 맞춘다.
   버전을 올릴 때는 [`YarnBuiltIns`](src/Vn.Core/Yarn/YarnBuiltIns.cs)의 내장 명령 목록도 같이 확인한다.
2. 실제 게임의 `.yarnproject`, `.yarn`, 스키마로 CLI를 돌린다.
3. 명령 파라미터 개수·타입 검증을 추가한다.
4. 억제 문법(`// vn:disable VN3001`)을 구현한다.
5. 그다음 `Vn.App` 스파이크를 추가한다.

`Vn.Core`는 UI 프레임워크에 의존하지 않습니다. `net8.0` 단일 TFM이고
`System.Windows.*` 참조나 `UseWPF`가 없으므로 Avalonia로 그대로 진행할 수 있습니다.
