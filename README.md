# VnTool

비주얼 노벨 저작 도구. C#/.NET 10 + Avalonia, 한국어 코드베이스.

**세 사람이 서로를 막지 않고 동시에 일하게 하는 것**이 이 도구의 목적입니다. 기획자는
챕터 구조를, 작가는 대사를, 연출자는 무대를 각자 자기 자리에서 만들고, 도구는 그것들이
어긋나지 않는지 지킵니다.

만들어 내는 것은 유니티 런타임(별도 저장소)이 먹는 데이터입니다 —
`exported/{챕터}.progression.json` + Yarn 번들.

## 두 계층, 두 화면

작업 순서가 곧 탭 순서입니다. 왼쪽에서 오른쪽으로 밟으면 챕터가 완성됩니다.

| | **① 챕터 그래프** (기획자) | **② 연출 그래프** (연출) |
|---|---|---|
| 만드는 것 | 에피소드 구조 · 선택지 · 조건 · 스탯 | 커맨드 · 무대 · 초상화 |
| 값이 사는 곳 | **엑셀 워크북** (`chapters/`·`episodes/`) | 프로젝트의 연출 노드 |
| 도구의 역할 | 읽고 검증하고 그린다 | 편집한다 |

기획자가 사실상 시나리오 작가여서 **스토리는 챕터 그래프에서 끝납니다.** 연출 그래프에
남는 일은 연출뿐이고, 그래서 그 판은 기능을 더 늘리지 않습니다(2026-08-18 결정).

## 편집의 기본은 엑셀이다

대사·구조의 **원본은 엑셀 파일**이고 도구는 그것을 읽는 쪽입니다. 기획자와 작가가 익숙한
도구(엑셀·구글 시트)를 그대로 쓰고, 저장하면 도구가 0.25초 안에 따라옵니다.

- **대사 본문·화자** → `episodes/{챕터}/{Id}.xlsx` — 도구는 **여기에 쓰지 않습니다.**
- **에피소드 구조·간선·조건·스탯** → `chapters/{Id}.xlsx` — 도구 편집이 셀에 즉시 저장됩니다.
- 우측 위 **"엑셀에서만 편집"** 체크(기본 켬)를 풀면 도구에서도 고칠 수 있습니다.
- ⚠ **엑셀이 그 파일을 열고 있으면 도구의 모든 쓰기가 거부**되고 붉은 배너가 섭니다.

**각 값의 주인은 한 곳뿐**이라는 것이 설계 전체를 관통하는 규칙입니다. 같은 사실이 두 곳에
있으면 어긋나고, 어긋남은 최종 출력에서야 드러납니다.

## 저장 구조

```text
MyStory/
├─ 예제.vnproject.json         목차 · 결과 조합
├─ game.definition.json        초상화 매핑 · 연출 커맨드 어휘 (기획자 전용)
├─ chapters/
│  └─ ch01.xlsx                에피소드 · 간선 · 선택지 · 조건 · 스탯 · 화자
├─ episodes/
│  └─ ch01/                    챕터별로 갈린다 (EpisodeId는 챕터 안에서만 유일)
│     ├─ 시작.xlsx             인덱스 · LineId · 유형 · 조건라벨 · 화자 · 내용
│     └─ 끝.xlsx
├─ exported/
│  └─ ch01.progression.json    런타임 수입물 — 검증을 통과하면 자동으로 나간다
├─ story/                      연출 노드
└─ assets/                     backgrounds · portraits · bgm · sfx
```

구판 워크북은 **열 때 자동으로 이행**됩니다(`.bak`을 남깁니다).

## 실행

```powershell
dotnet run --project .\src\Vn.App\Vn.App.csproj
```

## 검증

```powershell
dotnet test .\VnTool.sln
```

## 작가에게 전달할 Windows 휴대용 패키지

```powershell
powershell -ExecutionPolicy Bypass -File .\publish-windows.ps1
```

.NET 런타임을 함께 담으므로 받는 PC에 개발 환경이 필요 없습니다.

## 문서

**이어받는 작업은 [`docs/handoff/current-state.md`](docs/handoff/current-state.md)에서
시작합니다** — 맨 위 계약 박스가 최신 규격의 정본이고, 다른 문서와 충돌하면 그쪽이 이깁니다.

| 문서 | 무엇이 있나 |
|---|---|
| [`docs/handoff/current-state.md`](docs/handoff/current-state.md) | **진입점.** 계층 규칙 · 화면 · 코드 지도 · 진행 상태 |
| [`docs/run-log.md`](docs/run-log.md) | **결정의 정본.** 한 일 · 근거 · 되돌리는 법 |
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | 코드 구조 — "이 기능은 어느 파일" |
| [`docs/handoff/architecture-decisions.md`](docs/handoff/architecture-decisions.md) | 확정된 설계와 **그 이유** |
| [`docs/runtime-contract.md`](docs/runtime-contract.md) | 유니티 런타임과의 계약 (Gate D) |
| [`docs/work-orders/chapter-graph-orders.md`](docs/work-orders/chapter-graph-orders.md) | 챕터 계층 규격 원본 |
| [`docs/chapter-layer-guide.md`](docs/chapter-layer-guide.md) | 기획자용 사용 안내 |
| [`docs/writer-guide.md`](docs/writer-guide.md) | 작가용 사용 안내 |
| [`docs/handoff/io-reference.md`](docs/handoff/io-reference.md) | 모든 입출력의 형식 |
| [`docs/runtime-ui-tooling-principles.md`](docs/runtime-ui-tooling-principles.md) | 도구 설계 원칙 (`원칙 §…` 인용의 출처) |

## Yarn 분석 도구

`Vn.Core`와 `Vn.Cli`는 Yarn 프로젝트를 읽고 검증하는 별도 도구입니다. 저작 경로와 분리돼
있습니다.

```powershell
dotnet run --project .\src\Vn.Cli\Vn.Cli.csproj -- .\samples\Real\Demo.yarnproject .\samples\Real\game.schema.json
```
