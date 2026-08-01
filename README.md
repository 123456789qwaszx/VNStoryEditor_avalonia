# VnTool

비주얼노벨 스토리 작가가 Unity나 IDE 없이 Yarn 프로젝트의 장면 구조를 탐색하고, 대사를 수정하고, 문제를 확인하기 위한 Avalonia 데스크톱 도구입니다.

## 현재 제공하는 작업 흐름

1. `.yarnproject` 파일 열기
2. 장면·소스 파일·진단 탐색
3. 같은 장면을 구조 카드와 Yarn 원문으로 확인
4. 그래프에서 전체 이야기 흐름 탐색
5. 구조 카드 또는 원문에서 대사 수정
6. 저장 시 인코딩, BOM, 기존 줄바꿈과 수정하지 않은 부분 보존
7. 저장 후 자동 재분석 및 현재 장면 복원
8. 최근 프로젝트, 최근 장면, 그래프 배치 복원

## 안전성 원칙

- `.yarn`이 실행 원본입니다. 분석 결과와 화면 모델은 저장하지 않고 다시 계산합니다.
- 텍스트와 구조 화면은 하나의 `OpenDocumentSession.WorkingText`를 공유합니다.
- 파일이 외부 프로그램에서 변경되었으면 자동으로 덮어쓰지 않습니다.
- 프로젝트·파일·장면 전환과 앱 종료 시 동일한 저장/버리기/취소 절차를 사용합니다.
- 스키마나 Yarn 문법에 문제가 있어도 가능한 원문은 계속 열어 수정할 수 있습니다.
- 그래프 좌표는 프로젝트 옆 `vn.workspace.json`에만 저장합니다.

## 실행

```powershell
dotnet run --project .\src\Vn.App\Vn.App.csproj
```

## 검증

```powershell
dotnet build .\VnTool.sln
dotnet test .\VnTool.sln
powershell -ExecutionPolicy Bypass -File .\build-and-run.ps1
```

## 작가에게 전달할 Windows 휴대용 패키지

```powershell
powershell -ExecutionPolicy Bypass -File .\publish-windows.ps1
```

완료되면 `artifacts\VnTool-win-x64.zip`이 만들어집니다. 이 패키지는 .NET 런타임을 함께 포함하므로 대상 PC에 개발 환경을 설치할 필요가 없습니다.

## 편집 범위

현재 구조 화면은 대사와 화자 한 줄을 안전하게 수정하는 데 집중합니다. 선택지 생성·삭제, 조건식 재작성, 명령 순서 이동처럼 원문 구조를 바꾸는 작업은 Yarn 원문 탭에서 수행합니다. 표시용 `StoryLine`과 `StoryBlock`은 공백·주석·모든 토큰을 보존하는 편집 AST가 아니므로, 이 모델로 파일 전체를 재생성하지 않습니다.
