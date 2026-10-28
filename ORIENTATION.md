# VNStoryEditor_avalonia — 처음 보는 사람을 위한 지도

> 2026-09-15 작성. 기준 커밋 `master@59b08d6`.
> 이 문서는 레포의 기존 문서를 **대체하지 않는다.** 정본은 `docs/`와 `ARCHITECTURE.md`이고,
> 이 문서는 "그 많은 문서를 어떤 순서로 읽고 무엇부터 건드릴지"를 정리한 안내판이다.

---

## 1. 한 문장으로

**엑셀과 그래프에 흩어진 저작 원본을 읽어, 유니티 런타임이 그대로 먹을 수 있는 검증된
데이터(`progression.json` + Yarn 번들)로 조립하는 Avalonia 데스크톱 도구.**

Yarn 편집기가 아니다. **편집기라기보다 컴파일러에 가깝다** — 원본은 대부분 바깥(엑셀)에
있고, 이 도구의 본질은 *읽고 · 검증하고 · 결정적으로 내보내는 것*이다.

## 2. 왜 이런 모양인가

목적이 기술이 아니라 **협업 구조**다. 기획자·작가·연출자 셋이 서로를 막지 않고 동시에
일하게 하려고 층을 갈랐다.

| 층 | 담당 | 원본이 있는 곳 |
|---|---|---|
| A 계층 — 챕터 그래프 | 기획자 | `chapters/{챕터}.xlsx` (엑셀) |
| B 계층 — 대사 | 작가 | `episodes/{챕터}/{Id}.xlsx` (엑셀) |
| 연출 계층 — 연출 그래프 | 연출자 | 프로젝트의 연출 노드 (도구 안) |

이 설계 전체를 관통하는 규칙이 딱 하나 있고, 코드를 읽다 막히면 대부분 이걸로 설명된다:

> **각 값의 주인은 한 곳뿐이다.**
> 같은 사실이 두 곳에 있으면 어긋나고, 어긋남은 최종 출력에서야 드러난다.

그래서 "도구에서 이걸 왜 못 고치지?" 싶은 자리가 많다 — 주인이 엑셀이기 때문이다.

## 3. 세 저장소 관계

```
  엑셀 · 연출 그래프
        ↓
  VNStoryEditor_avalonia   ← 지금 이 레포. 저작 · 검증 · 내보내기
        ├─ progression.json ─┐
        ├─ Yarn 번들 ────────┼→ ked-presentation-runtime (유니티, 읽기 전용)
        └─ 튜닝 데이터 ──────┘        ↓
                                spring-prepare (서버, 세이브·통계)
```

다른 두 저장소는 **읽어서 맞추되 고치지 않는다.** 계약 변경이 필요하면 담당자에게 넘긴다.

## 4. 프로젝트 6개 지도

| 프로젝트 | 줄 수 | 역할 | 경계 |
|---|---:|---|---|
| **`Vn.Authoring`** | 35k | 저작 도메인 · 엑셀 I/O · 검증 · 내보내기 | **제품 핵심.** 화면을 모른다. 화면 없이 테스트 가능 |
| **`Vn.App`** | 25k | Avalonia 화면과 OS 연결 | 도메인 규칙을 소유하지 않는다 |
| **`Vn.Core`** | 3.5k | Yarn 파싱·진단 (실제 YarnSpinner 컴파일러) | 저작 모델·엑셀·UI를 모른다 |
| **`Vn.Cli`** | 0.3k | `Vn.Core`의 콘솔 진입점 | 골든 검증·자동화용 |
| **`Ked.Presentation.Core`** | 5k | 무대 상태 계산 | **유니티 런타임에서 소스째 복사해 온 사본.** 고치지 않는다 |
| **`Ked.Progression`** | 2.5k | 산출물을 소비자 규칙으로 다시 읽는 **검증 오라클** | 런타임 사본과 동기화되어야 한다 |

참조 방향:

```
Ked.Presentation.Core ─┐
Ked.Progression ───────┴→ Vn.Authoring ─┐
                                         ├→ Vn.App
                       Vn.Core ──────────┘
                          └→ Vn.Cli
```

⚠ `Vn.Authoring`은 **여전히 `Vn.Core`를 모른다**(저작 도메인을 컴파일러에 묶지 않는다).
`Vn.App → Vn.Core` 선만 2026-08-23에 넘었다 — 이미터 산출물을 진짜 Yarn 컴파일러에 걸어
보기 위해서다(`Vn.App/Services/YarnOutputVerification`).

### `Vn.Authoring` 내부 폴더의 뜻

| 폴더 | 무엇이 사는가 |
|---|---|
| `Script/` | 대본 — **화자·대사의 유일한 수정 가능한 원본.** 줄의 *정체성*(`LineId`)과 *본문*을 분리한다 |
| `Model/` | 프로젝트·파일·노드·조건 — 그래프의 저장 모델 |
| `Results/` | **얼어붙은 것.** 발행된 결과는 불변이고 내용 해시로 버전을 판정한다 |
| `Flow/` | 계산. 조건 갈래 해석(`ConditionFlowResolver`)이 여기 산다 |
| `Graph/` | 화면에 *무엇을* 그릴지. 필터링도 화면이 아니라 여기가 한다 |
| `Editing/` | **모델을 바꾸는 유일한 통로**(`ProjectEditor`). 되돌리기·알림을 함께 진다 |
| `Rendering/` | 평평한 문서로 펼치기 — Yarn 이미터가 여기 |
| `Chapters/` | 챕터 계층 — 엑셀 워크북 reader/writer/migrator, 내보내기 |
| `Serialization/` | 저장 형식 |

## 5. 데이터가 흐르는 길 (이걸 외우면 절반은 끝)

```
1. 워크북 읽기        chapters/*.xlsx, episodes/**/*.xlsx
        ↓                → ChapterWorkbookReader / EpisodeWorkbookReader
2. 모델 구성          ChapterGraphModel
        ↓
3. 검증               ChapterValidator + 도달성 증명기
        ↓
4. 내보내기           ChapterProgressionExporter → progression.json
        ↓
5. 재검증(오라클)     Ked.Progression.ProgressionLoader 에 실제로 실어 본다
        ↓                → 거부되면 파일을 내지 않고 "엑셀 시트·행"으로 진단
6. Yarn 번들          YarnBundleEmitter → 실제 Yarn 컴파일러 검증 통과해야 배달
```

**5번이 이 레포의 성격을 가장 잘 보여 준다.** 검증 규칙을 직접 다시 쓰지 않고, 소비자의
실제 코드를 들여와 거기에 통과시킨다. "에디터에서는 통과했는데 게임은 거부" 를 구조적으로
막으려는 것이다.

## 6. 기능을 바꾸려면 어디를 여는가

`ARCHITECTURE.md` §5.1에 **긴 빠른-조회 표**가 있다. 여기가 실질적 시작점이다. 자주 쓰는 것만:

| 하고 싶은 일 | 여는 곳 |
|---|---|
| 모델을 바꾸는 편집 명령 추가 | `Vn.Authoring/Editing/ProjectEditor.cs` — **여기에만** |
| 화자·대사 바꾸는 코드 | `Editing/ProjectEditor.Scripts.cs`의 `SetScriptLineText` — 하나뿐 |
| if/elseif/endif 의미 | `Flow/ConditionFlowResolver.cs` — 이 파일이 조건 모델 그 자체 |
| 그래프에 무엇이 나타나는지 | `Graph/GraphProjectionBuilder.cs` — 화면이 아니라 여기 |
| 노드 직렬화 | `Serialization/StoryNodeJson.cs` — 하나뿐 |
| 연출 명령 목록 추가 | `game.definition.json` — **코드가 아니다** |
| Yarn 노드 이름 규칙 | `Rendering/YarnBundleEmitter.StoryNodeTitleOf` — 밖에서 손으로 조립 금지 |
| 도구 모음·창 제목 | `Vn.App/MainWindow.axaml{,.cs}` |
| 그래프 화면 | `Vn.App/Views/GraphEditorView.axaml.cs` |
| 무대 조절창 | `Vn.App/Views/StageSceneView.cs` |

## 7. 밟으면 조용히 터지는 지뢰 (처음 3개월치 실수 목록)

- **개명은 참조를 끌고 간다.** 화자·조건·아이템 이름은 *이름이 곧 신원*이라, 등록부만 고치면
  그걸 쓰던 자리가 전부 미등록이 된다. 전용 개명 경로(`SpeakerRenamer` 등)를 쓴다.
- **엑셀이 그 파일을 열고 있으면 도구 편집이 저절로 잠긴다.** 붉은 배너가 서고, 엑셀에서
  닫으면 풀린다. 버그가 아니다.
- **결정적 출력이 계약이다.** 같은 입력의 재출력은 공백·순서·인코딩까지 **바이트가 같아야**
  한다(서버가 SHA-256으로 챕터 버전을 식별). 컬렉션 순서를 바꾸는 리팩터링에 주의.
- **`간선` 시트의 행 순서 = 화면 순서 = 서버 이력의 `OptionIndex`.** 출시된 선택지 사이에
  행을 끼워 넣으면 과거 플레이 이력의 뜻이 바뀐다.
- **진행 스탯을 Yarn 변수로 읽으면 안 된다.** 도달성 증명 밖에 숨은 분기가 생긴다.
- **솔루션 단위 `dotnet test`는 빌드 실패한 프로젝트를 조용히 건너뛴다.** 요약 줄이 **4개**인지
  세고 나서 "전부 통과"라고 말할 것.
- **성능 문제는 대부분 "같은 일을 여러 번"이다.** 과거 실측: 첫 화면 58.3초 → 1.66초. 고친 법은
  최적화가 아니라 중복 제거(해시 캐시, 변경 병합)였다. 고정도 시간이 아니라 **일의 횟수**로 건다.

## 8. 지금 어디까지 왔나

- **런타임 정렬 R0~R5 전부 완료** (2026-09-03). CI **1,826/1,826 통과**.
  Scene 수명 계층, 명시적 Auto 간선, 결정적 출력, 프리뷰 정렬까지 닫혔다.
- 계획 문서(`docs/plans/PLAN.md`) 기준으로 **열려 있는 다음 단계가 없다.** 즉 지금은
  "다음에 뭘 할지 정해야 하는" 상태다.
- 명시적으로 **하지 않기로 한 것**: 서버 M8-b 구현, 런타임 세이브 UI, 장면 중간 저장,
  전역 앨범, Scene 사이 무대 승계, 다른 두 저장소 코드 수정.
- 남은 열린 항목은 `docs/handoff/current-state.md` §7과 `docs/runtime-contract.md` 3부에 있다.
  (예: 초상화 매핑이 아직 화자 *이름 문자열*로 찾는 문제 — 번역된 locale에서 초상화를 못 찾는다)

### 브랜치 상황

**`master` 말고는 전부 죽은 가지다.** 원격 6개 브랜치 모두 master보다 앞선 커밋이 **0개**다
(전부 병합됐거나 버려짐). 정리해도 안전하다.

| 브랜치 | 마지막 커밋 | master 대비 |
|---|---|---|
| `dev` | 2026-08-01 | 551 뒤, 0 앞 |
| `fix/diagnostic-codes-and-positions` | 2026-07-30 | 584 뒤, 0 앞 (PR #1) |
| `script-source-and-published-results` | 2026-08-02 | 520 뒤, 0 앞 |
| `stage-quick-commands` | 2026-08-23 | 147 뒤, 0 앞 |
| `hostGlue` | 2026-08-24 | 95 뒤, 0 앞 |
| `dialogue-box-fold` | 2026-09-02 | 47 뒤, 0 앞 |

## 9. 빌드하고 돌리는 법

```powershell
dotnet run  --project .\src\Vn.App\Vn.App.csproj     # 앱 실행
dotnet test .\VnTool.sln                              # 전체 검증 (요약 줄 4개 확인!)
powershell -ExecutionPolicy Bypass -File .\publish-windows.ps1   # 작가 배포용 portable
```

- .NET **10**, Avalonia **12.1.1**, 중앙 패키지 관리(`Directory.Packages.props`)
- `TreatWarningsAsErrors=true`, `Nullable=enable` — 경고 하나가 빌드를 깬다
- **CI는 `windows-latest`가 정본이다.** 파일 잠금·portable 패키지·CRLF 골든을 검증하는
  제품이라 리눅스는 대표 환경이 아니다

## 10. 읽는 순서 (추천)

1. `README.md` — 5분. 무엇을 만드는지
2. `docs/external-overview.md` — **여기가 진짜 진입점.** 20분. 책임·데이터 흐름·런타임 계약
3. `ARCHITECTURE.md` §3(프로젝트 구성) → §5.1(빠른 조회) — 코드 지도
4. 손댈 자리가 정해지면 `ARCHITECTURE.md` §6(깨면 부서지는 규칙)과
   `docs/handoff/current-state.md`의 해당 박스
5. `docs/run-log.md` — **결정의 정본.** "왜 이렇게 됐지?" 는 거의 다 여기 답이 있다

> `docs/handoff/current-state.md` 맨 위의 인용 박스들은 **밀도가 매우 높다.** 처음부터
> 정독하려 들지 말고, 특정 기능을 건드릴 때 그 기능 박스만 찾아 읽는 편이 낫다.

## 11. 규모 감각

| | 파일 | 줄 |
|---|---:|---:|
| `src/` | 261 | 72,119 |
| `tests/` | 207 | 46,495 |
| 문서 | 39 | — (`ARCHITECTURE.md` 혼자 70KB) |

테스트가 프로덕션 코드의 **65%**다. 문서·테스트 밀도가 높은 레포이고, 그게 이 프로젝트의
작업 방식이다 — 새 작업은 `docs/run-log.md`에 항목 하나를 남기는 것으로 끝난다.
