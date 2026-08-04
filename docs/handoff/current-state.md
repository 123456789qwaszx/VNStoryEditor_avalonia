# 현재 실제 구현 상태

기준: T1·T3 완료 시점 (2026-08-04) · 테스트 **475개 전원 통과** (Core 60 / Authoring 351 / App 64) · 작업 트리 clean.
이 문서는 **실제 코드**를 기준으로 쓴다. 계획 문서와 어긋나면 이쪽이 사실이다.

---

## 1. 완료된 단계

| 단계 | 커밋 범위 | 내용 |
|---|---|---|
| Phase 0 (W1–W6) | `a29c25a`…`3cbe920` | 카탈로그 데이터화 · set · Setup 블록 · YarnBundleEmitter · 검증 체계 · UI 최소 연결 |
| Phase 1 (W7–W12) | `db32175`…`1d658d6` | 변수 선언 파일 · 선택지 · CSV 3종 · 연출 공급 노드+프리셋 · `[adv/]` · 3분할+필터 |
| 피드백 개정 | `bcb589b`, `c1b67ee` | 합성→공급 연결, 노드 출구 정리, 선택지 박스 UI, 노드 단위 내보내기 |
| Phase 2a-v1 (W13–W16) | `5029dd1`…`4243ea5` | 에셋 연결 · MiniStageFold · 무대 프리뷰 패널 · ARCHITECTURE 규칙 14 |
| Phase 2a-v2 (W17–W21) | `e57e77d`…`e77df0b` | 에셋 탐색기 · 프리뷰 창 분리 · 갤러리/칩/텍스트 입력 · 직접 조작 · 입력 경로 수렴 골든 |
| UX 그룹 0 | `21b2a7d`, `4514cff` | X1 크래시 방어 · X8 버튼 제거 |
| UX 그룹 2 | `3cabae9`, `c08bb01` | X9 선택지 Speaker 불요 · X10 라벨/분기 시각 구분 |
| UX 그룹 1 | `0e57909`…`f334341` | X2 타입 드롭다운 · X6 슬라이더 · X7 Bool 토글 · X5 Speaker 원천 · X3 스탯 HUD |
| docs 정리 (W-docs-01) | `1fea4a8`…`ca03883` | docs 폴더 구조 재편 + 상호 참조 정리 |
| 에셋 규약 교정 (W-asset-02) | `351b9b7`…`1de93a4` | 초상화 연결 권위를 매니페스트 → 폴더 규약으로 |
| UX 그룹 3 (CompositionNode) | `c204d54`…`439a82c` | X11 Yarn 조건 표기 · X13 양식 선택 · X4 노드 즉시 저작 · X12 붙여넣기+라이브 출력 |

## 2. 코드 지도 — 최근에 생긴 것 위주

```
src/Vn.Authoring/
  Assets/          PortraitKey(경로↔키 단일 구현) · PreviewAssetLibrary(규약 1순위 해석)
                   PortraitManifest(수입 보조) · AssetExplorerModel(트리 계산)
  Flow/            MiniStageFold(무대 폴드) · StatFold(스탯 HUD 값)
                   + 기존 조건/공급/바인딩 해석기들
  Results/         LiveNodeComposer(라이브 합성 — X12c의 심장)
                   Dialogue/PresentationPublisher(Freeze) · NodeExportResolver(공급 짝)
  Rendering/       YarnBundleEmitter(+BundleNameOf — 이름 규칙 한 곳)
                   OutputManifest(출력 기록 + 고아 판정 — 지우지 않는다)
  Script/          ScriptSynchronizer(diff 엔진 — 붙여넣기가 재사용)
                   ScenarioTextParser(붙여넣기 파싱) · ScriptParser · ScriptDocument
  Definition/      CommandText(텍스트↔커맨드 단일 구현) · ArgumentTokenCandidates
                   GameDefinition(+Store — speakers 쓰기) · PresentationCommandCatalog
  Editing/         ProjectEditor(+.Scripts/.Results) — 모델 변경의 유일한 통로
                   PresentationStageActions(직접 조작→편집 변환)

src/Vn.App/
  Services/        LiveOutputService(디바운스 자동 저장) · UiGuard(예외 포획)
                   StageSceneComposer(무대 배치 계산) · PreviewImageCache · AssetRootPicker
  Views/           StageSceneView(무대 렌더 — 도킹/분리 창 공용) · StagePreviewWindow
                   AssetExplorerView · MiniStagePreview · DialogueNodeEditor · PresentationNodeEditor
```

## 3. 지금 동작하는 것 (사용자 관점)

**저작 흐름**
- 대사 편집은 고밀도 한 줄 카드다(`work-orders/dialogue-compact-ui.md`):
  `Index | LineId | 화자 | 대사 | ＋` 열 + 조건/Set 태그 레일(클릭=Flyout 편집) + 출구 레일.
  ▲▼✕는 행에서 사라지고 ＋ Flyout "줄" 섹션으로 이동. [줄 추가]는 선택 줄 바로 아래 삽입.
- 대사노드를 만들면 전용 대본·첫 줄이 함께 생겨 **즉시 타이핑**. 대본 가져오기 트랙은 없다.
- 긴 대본은 Script Preview → **ScenarioOnly에 붙여넣고 [텍스트 반영]**. diff 기반이라 기존 LineId가 보존되고, 확신할 수 없으면 전량 거부한다. 삭제는 같은 텍스트로 두 번 눌러 확인.
- 화자는 설정노드에서 등록(저장은 `game.definition.json`), 대사노드는 콤보박스(드롭다운+자유 입력).
- 변수는 타입 드롭다운(float/bool), set 편집은 등록 변수 드롭다운 + 슬라이더(변수별 범위, 기본 -5~+5) 또는 Bool 토글.

**연출**
- 커맨드를 만드는 길 셋: 갤러리(★프리셋→최근→카테고리(강도)→검색) / 텍스트 직접 입력 / 프리뷰 직접 조작. 전부 `ProjectEditor`로 수렴하고 출력 바이트가 같다(`InputPathEquivalenceTests`).
- 인자는 칩 편집(대상 후보는 그 라인까지 접은 무대 상태, duration은 §23.6 칩). 함정 노트는 ⚠ 툴팁.
- 모든 커맨드 행에 `<<…>>` 병기 텍스트.

**프리뷰**
- 무대 빈 곳 클릭 → **배경/슬롯/캐릭터 3탭 팝오버**. 배경·위치(place)·표시(fade)는 선택 라인에,
  슬롯 생성·캐스팅은 **노드 Setup에**(`PresentationStageActions.ApplyToSetup` — 같은 대상은 수정,
  누적 없음). 표시/숨김 전환은 반대 방향 fade를 걷어낸다(`ApplyVisibility`).
- 무대 위 캐릭터 클릭 → 표정 교체(썸네일 + 키 직접 입력, 에셋 없어도 항상 가능) · variant ·
  **위치 이동(3×3 screenPoint)** · 등장/퇴장 · 좌우 반전. 위치 격자는 슬롯 탭과 같은 것을 쓴다.
- 하단 축소판 + [창으로 열기] 분리 창(1920×1080 레터박스, 따라가기 토글, 이전/다음). 같은 `StageSceneView`.
- 배경·초상화·화자 강조·대사창(boxKind별 근사)·스탯 HUD·"반영 안 된 연출 N" 뱃지·갈래 근사 표시.
- 연출노드에서는 무대 직접 조작 가능, 대사노드(발행본 열람)에서는 잠기고 이유가 뜬다.

**에셋**
- 좌측 탐색기: 배경은 폴더 구조 그대로, 초상화는 캐릭터→variant→emotion 키 구조. 문제 3종 ⚠ 표시.
- **연결 권위는 폴더 규약**: `{root}/{char}/{variant}/{emotion}.png`를 넣기만 하면 등록된다. 매니페스트는 수입 보조.

**출력**
- 라이브: 출력 폴더를 지정하면 편집 후 600ms 디바운스로 자동 재합성·재저장.
- 수동: [내보내기…] / [CSV 내보내기…] / 노드 단위. **발행은 게이트가 아니다** — 라이브와 같은 `LiveNodeComposer`를 지나므로 바이트가 같다.
- [양식…]에서 산출 양식 선택(Yarn 트리오 / Script·Review·Direction CSV), 선택은 프로젝트에 저장.
- **고아 출력**: 노드를 지우거나 이름을 바꿔 쓸모없어진 `.yarn`이 출력 폴더에 남으면 상태줄 요약 +
  [양식…] 전체 목록으로 보인다. **VnTool은 지우지 않는다** — 지우는 건 사람이 한다.

## 4. 저장 형식

`formatVersion 3` — **W13 이후 한 번도 올리지 않았다.** 새 필드는 전부 기본값 생략 직렬화라 기존 프로젝트 파일이 바뀌지 않는다.

```
project.vnproject.json   목차 · links · assetRoots · recentCommands · exportFormats · outputPath
script/<id>.vnscript.json  줄 정체성 + locale별 화자·대사
story/<id>.vnstory.json    노드 · LineId별 조건/set · 출구
results.vnresults.json     발행된 불변 결과
game.definition.json       게임 어휘(변수·커맨드·카테고리·speakers·preview.resolution)
```

## 5. 아직 안 된 것

- **Phase 2b(정지 프레임 렌더러)·2c(실시간 재생)** — 착수 전 소유자의 유니티 실재생 게이트 통과 필요.
- **Phase 2a 전체 에셋 파이프라인** — 런타임 U12-전체(DBSO 프리셋·리그 스키마·기준 해상도)와 짝.
- 런타임 저장소 작업(U1–U17)은 **다른 저장소**다. 지시서만 `docs/ked-presentation-runtime/`에 있다.
- 상세는 [next-tasks.md](next-tasks.md), 결함은 [known-issues.md](known-issues.md).
