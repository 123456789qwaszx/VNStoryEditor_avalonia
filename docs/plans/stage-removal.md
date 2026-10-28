# 무대 프리뷰 · 에셋 탐색기 제거 계획

> 2026-10-28 소유자 결정: "무대 프리뷰를 완전 삭제합시다. 에셋 탐색기 역시 지웁시다. 작가는 그런 거 안 씁니다."
> 흔적 없이 지운다 — 모델·편집기·화면·테스트·문서. 별도 툴로 떼어내지 않는다(떼어내면 같은 프로젝트
> 파일을 쓰는 두 번째 편집기가 된다. 나중에 무대를 눈으로 볼 일이 생기면 내보낸 결과물을 읽기만 하는
> 플레이어를 반입 Core 위에 새로 만든다).

## 1. 조사 결과 — 무엇이 어디에 얽혀 있나

### 1.1 무대 프리뷰 탭이 가진 것

| 자리 | 파일 | 줄 | 내용 |
|---|---|---|---|
| 탭 | `MainWindow.axaml` `StageTabItem` · `MainWindow.axaml.cs` | 약 150 | 탭 진입·복귀(`EnterStageTab`·`ReturnFromStage`·`OpenOnStage`), 씬 선택기, 라인 이동·선택·detour 신호 배선, `RefreshStagePreview` 계획 |
| 패널 | `Views/MiniStagePreview.axaml(.cs)` | 1,291 | 씬 선택기 · 터미널(`PresentationScriptPanel`) · 무대 · 타임라인/재생 · 작업대 · 조절창 호스트 · 에셋 탐색기 호스트 |
| 무대 | `Views/StageSceneView.cs` | 4,982 | 렌더러 · 직접 조작(클릭·드래그) · 조절창 다섯 탭(자주 쓰는·슬롯·캐릭터·배경·오디오) · 전이 보간·페이드 · ease 곡선 창 호출 |
| 곁 | `Views/PresentationScriptPanel.cs` · `StagePlaybackControls.cs` · `StageIndicators.cs` · `DetourReturnPlayback.cs` · `EaseCurveEditor.cs` · `EaseCurveWindow.cs` | 2,048 | 터미널 · 재생 컨트롤 · 뱃지/진단 · detour 복귀 · ease 곡선 편집(무대 조절창에서만 열린다) |
| 서비스 | `Services/PreviewImageCache.cs` · `AudioPreview.cs` · `AudioCueRouter.cs` · `AssetRootPicker.cs` | | 비트맵 캐시 · 오디오 미리듣기 · 에셋 루트 선택(프리뷰·탐색기만 쓴다) |
| 계산 | `Vn.Authoring/Flow`: `MiniStageFold` · `CoreStageFold` · `StageMotionPlan` · `MotionInspection` · `StagePlayback` · `StageSceneComposer` · `StageFades` · `StageTransitions` · `EaseCurveBaker` | 약 2,500 | 폴드·궤적·재생·배치 — 프리뷰 밖 소비자 없음(아래 1.3) |
| 편집 | `Vn.Authoring/Editing/PresentationStageActions.cs` · `Model/StageQuickCommand.cs` · `ProjectEditor`의 자주 쓰는 칩 명령 7개 | 약 600 | 직접 조작 → 편집 변환 · [★ 자주 쓰는] 칩 |
| 에셋 | `Vn.Authoring/Assets`: `PreviewAssetLibrary` · `PortraitManifest` · `RuntimeTuningLibrary` · `SurfaceLayouts` · `AssetExplorerModel` · `Views/AssetExplorerView` | 약 1,500 | 배경/초상 색인 · 런타임 덤프(U12 초상 매니페스트, 튜닝 폴더) · 탐색기 트리 |

### 1.2 ⚠ 라인별 연출 커맨드 편집이 이 탭 안에만 있다

2026-08-20 결정으로 **라인별 커맨드 편집(터미널 · 연출 추가 갤러리 · 작업대 인스펙터 · Setup 편집)은 전부
무대 프리뷰 탭으로 갔다.** 연출 그래프의 연출 편집기(`PresentationNodeEditor`)에는 노드 수준(입력 결과 ·
채널 상태 · 발행 · 고아 연출)만 남아 있다. 편집 로직 자체는 `PresentationNodeEditor`의 메서드
(`BuildCommandInspector` · `BuildAddConsole` · `AddCommand` · `BuildCommandRow`)에 살고, 무대 탭은
`CommandDetailProvider`·`AddConsoleProvider`로 그것을 **호스팅**한다.

즉 무대 프리뷰를 지우면, 바꾸지 않는 한 **이 툴에서 `<<place>>`·`<<shot_to>>` 같은 연출 커맨드를 라인에
붙이는 화면이 사라진다.** 내보내기(`YarnBundleEmitter`)는 프로젝트에 이미 든 커맨드를 그대로 낸다.

→ **결정 필요(§2).**

### 1.3 프리뷰 밖에서도 쓰는 것 — 남긴다

| 것 | 쓰는 곳 | 처리 |
|---|---|---|
| 반입 `Ked.Presentation.Core` | `YarnBundleEmitter`(내보내기) · `ProjectEditor`(ease 곡선 키) · `ProjectManifestJson` · `CommandText` · `ArgumentTokenCandidates` · `EaseCurve` 모델 | **남긴다.** 내보내기 규약의 주인이다 |
| `PortraitKey` · `PortraitSpriteImporter` | `SetNodeEditor`(설정 노드의 초상 가져오기) | 남긴다 |
| `EaseCurve` 모델 · 직렬화 · 내보내기 · `EaseCurveCommandActions` | 프로젝트 형식(`easeCurves`)과 Yarn 번들 | 모델·직렬화·내보내기는 남긴다(런타임 계약). **편집 창은 무대 조절창에서만 열리므로 화면은 지운다** — 곡선을 새로 만들 화면이 없어진다(§3 열린 항목) |
| `AssetRootSettings`(프로젝트의 에셋 루트 넷) | 프로젝트 형식 · `AuthoringSession`(bgm/sfx 루트) | 형식은 남긴다. 배경·초상 루트를 읽는 소비자가 사라지면 읽기만 유지 |
| `BranchFlow.FoldLines`(W35 갈래 폴드 입력) | 프리뷰의 갈래 인식 폴드만 | 폴드 입력 조립(`MiniStageFoldLine`)만 걷어낸다. 갈래 해석 자체는 그대로 |
| `ProjectRefreshPlanner.RefreshPreview/RefreshStagePreview` | 셸 | 깃발 둘을 지운다 |

### 1.4 프로젝트 파일 형식

`project.vnproject.json`의 `quickCommands`(자주 쓰는 칩)·`assetRoots.backgrounds/portraits`는 프리뷰 전용 값이다.
작가들의 기존 프로젝트가 있으므로 **읽을 때 모르는 키를 거부하지 않는다**(지금도 `ReadQuickCommands`는 없으면
빈 값). 쓰기에서 `quickCommands`는 더 이상 나가지 않는다. 형식 버전은 올리지 않는다(없어진 값은 무시해도
의미가 안 바뀐다). `easeCurves`는 남는다(1.3).

### 1.5 테스트 · 문서

- 테스트: Vn.App.Tests 17파일 132건(394 중) · Vn.Authoring.Tests 16파일 165건(963 중)이 지워지는 코드를 본다.
  `MainChromeTests`(탐색기 위치 3건)·`BranchFlowTests`(FoldLines 7곳)·`InputPathEquivalenceTests`(직접 조작 경로)는
  해당 부분만 걷는다. `Ked.Presentation.Core.Tests` 356건은 그대로(Core가 남는다).
- 문서: README(탭 둘) · ARCHITECTURE(§3.10, 표의 프리뷰 행, §5.2) · ORIENTATION · `docs/writer-guide.md`(4번 "연출 붙이기") ·
  `docs/handoff/*` · `docs/runtime-contract.md`(프리뷰 판정 항목) · `docs/project-boundaries.md`. `docs/plans/R4.md`·
  `docs/delivery/*`·`docs/run-log.md`·`docs/work-orders/*`는 이력이라 손대지 않는다.

## 2. 결정 — 연출 커맨드 편집을 어디에 둘 것인가

| | A. 통째로 지운다 | B. 글 편집만 연출 그래프로 옮긴다 |
|---|---|---|
| 남는 것 | 연출 편집기 = 입력 결과 · 발행 · 고아. 커맨드는 이미 든 것만 내보낸다 | 연출 편집기 곁기둥에 터미널(`PresentationScriptPanel`) + 연출 추가(갤러리·텍스트) + 인스펙터(수치 칩). 무대 그림 없음 |
| 지우는 양 | 전부(약 14,000줄 + 테스트 297건) | 터미널·추가·인스펙터와 그 테스트(약 2,000줄·30여 건)는 남는다. 인스펙터의 슬롯 후보(`StageTargetCandidates`)가 폴드에 기대므로 카탈로그 후보로 바꾼다 |
| 뜻 | 이 툴은 연출 커맨드를 **저작하지 않는다** | 연출은 글로 저작한다(세 입력 경로 중 둘이 남는다) |

소유자 답에 따라 §3의 1단계가 갈린다.

## 3. 단계

각 단계는 커밋 하나, CI(Windows) 녹색을 확인하고 다음으로 간다.

1. **셸에서 탭을 뗀다** — `StageTabItem`·`MiniStagePreview` 호스팅·씬 선택기·진입/복귀·신호 배선·`RefreshStagePreview`·탐색기 호스트.
   (B라면 같은 단계에서 연출 편집기 곁기둥에 터미널·추가·인스펙터를 세운다.)
2. **화면을 지운다** — `MiniStagePreview`·`StageSceneView`·`StagePlaybackControls`·`StageIndicators`·`DetourReturnPlayback`·
   `EaseCurveEditor/Window`·`AssetExplorerView`·`PreviewImageCache`·`AudioPreview`·`AudioCueRouter`·`AssetRootPicker`,
   대사·연출 편집기의 `StagePreview` 손잡이와 폴드 호출, 그래프의 더블클릭 "무대에서 보기".
3. **계산·편집·에셋을 지운다** — Flow 아홉 파일 · `PresentationStageActions` · `StageQuickCommand`와 `ProjectEditor`의 칩 명령 ·
   `ProjectManifestJson`의 `quickCommands` 쓰기 · Assets 다섯 파일 · `AuthoringSession`의 AssetLibrary/TuningLibrary/튜닝 폴더 명령.
4. **테스트를 걷는다** — 1.5의 파일들. 남는 테스트가 전부 녹색.
5. **문서** — 1.5의 목록. README의 "두 계층, 두 화면"은 그대로이고 셸 규칙에서 세 번째 탭을 지운다.

## 4. 결정과 결과 (2026-10-28)

소유자: **A.** "글 편집, 커맨드 목록, 수치 인스펙터 등은 별도 툴로 제작한다. 이곳은 진짜 작가가
시나리오 문구, 선택지 문구를 작성하는 글을 쓰는 곳으로만 남긴다."

지운 것(흔적 없이): 무대 프리뷰 탭과 패널·무대 렌더러·조절창·직접 조작·타임라인/재생·detour 복귀 ·
터미널·연출 추가 콘솔·작업대 인스펙터·Setup 편집 · 에셋 탐색기 · 설정 노드의 [표정] 관리(에셋 폴더
규약에 기댄다) · ease 곡선 편집 창과 커맨드 소유 곡선 액션(모델·직렬화·내보내기는 남는다) ·
Flow의 폴드·모션 계획·재생 경로·대본 패널 모델·선택지 묶음·갈래 폴드 입력 · `PresentationStageActions` ·
자주 쓰는 칩(`quickCommands`)·최근 커맨드(`recentCommands`)·에셋 루트(`assetRoots`) 모델과 저장 키 ·
`ProjectEditor`의 칩 명령·배치 편집·에셋 루트·발행 점검 API · Assets 폴더 전부(초상 키·매니페스트·
스프라이트 가져오기·튜닝 덤프·표면 배치) · 내장 기본 튜닝 리소스 · `game.definition.json`의
`preview.resolution`·`runtimeTuningPath` · 세션의 에셋/튜닝/오디오/가져오기/시뮬 값 · 그래프 카드
더블클릭 "무대에서 보기"와 화면 가운데 옮기기 · 테스트 36파일과 `TuningFixtures/ExportedTuning`.

남긴 것: 대사 편집기의 갈래 선택·카드 흐림(`StageBranchSelection`·`BranchFlow`·`ConditionSimulation`·
`StatFold`), `ProjectEditor`의 연출 커맨드 편집 API(추가·인자·이동·토글·제거)와 `CommandText`,
연출 노드 편집기의 노드 수준(자동 채널·입력 결과·고아), 반입 Core와 ease 곡선 데이터·내보내기.

구판 프로젝트 파일의 `assetRoots`·`quickCommands`·`recentCommands`는 읽을 때 무시되고 저장할 때
다시 쓰지 않는다. 형식 버전은 그대로다. 첫 저장은 이제 정의 파일만 준비하고 `assets/`·`ExportedTuning/`을
만들지 않는다. 다른 이름으로 저장은 챕터·대본·정의만 옮긴다.

## 5. 열린 항목(소유자 확인)

- ease 곡선: 모델·내보내기는 남고 **편집 화면이 없어진다.** 기존 프로젝트의 곡선은 그대로 나간다. 새로 만들 길이 필요하면 별도 작업.
- `assets/backgrounds`·`assets/portraits` 루트와 런타임 튜닝 폴더(`ExportedTuning`) 연결 명령은 프리뷰가 유일한 소비자였다 — 같이 지운다.
  첫 저장 때 넷을 기본값으로 채우던 것은 bgm/sfx만 남긴다.
