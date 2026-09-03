# VnTool ↔ 런타임 계약서

> **현행 상태 (2026-09-03, R4 완료).** 실제 소비자 기준은
> `ked-presentation-runtime/server_DB@d53cc2f`. R0~R4에서 툴의 `Ked.Progression` 사본,
> `SceneId`, 명시적 `Auto`, 장면 진입 불변식, `ChapterReachability`,
> 결정적 JSON/SHA-256, OptionIndex 기준선, 프리뷰 `Resolve`/`FoldChoices`,
> Yarn 진행 스탯 격리까지 정렬됐다.
> 아래의 2026-08~09-02 "미커밋 진행 중"·"착수 금지"·`§G-10~G-14` 미결 표시는
> 당시 판단을 보존한 **역사 기록**이다. 현재 작업 지시로 읽지 않는다.
> 완료 증거는 [`plans/PLAN.md`](plans/PLAN.md)와 R0~R4 세부 문서가 정본이다.

**표면이 둘로 갈렸다** (2026-08-17):

| | 상대 | VnTool이 내는 것 |
|---|---|---|
| **1부** | `ked-presentation-runtime` (유니티) | `.yarn` 텍스트 + 연출 커맨드 |
| **2부** | `Ked.Progression` (순수 C# 진행 코어 — 2026-09-02 실측: **런타임 안에 복사 반입**, 정본은 형제 저장소 `ked-progression`) | `exported/{챕터}.progression.json` |

이 문서와 어긋나는 출력은 컴파일이 되어도 **조용히 깨진다** — 대부분 즉시 오류가 아니라
어긋난 재생으로 나타나므로 이미터 검증 단계에서 잡아야 한다.

> **개정 이력**
> - 2026-08-03 최초 — 런타임 소스 전수 분석
> - 2026-08-17 §G 신설 — 진행 JSON 수입기 계약
> - **2026-08-18 전면 개정** — 런타임이 크게 단순해졌고, 진행 계층이 **별도 패키지로
>   빠져나갔다.** §0이 무엇이 사라졌는지 먼저 적는다. 근거는 두 저장소 실측이다.
> - **2026-09-02 재편 반영** — 런타임이 진행 코어를 **챕터 안으로 복사 반입**하고 **Scene
>   수명 계층**을 세웠으며 **세이브 층이 돌아왔다.** §0-1이 무엇이 바뀌었는지 먼저 적고
>   §C·§D·§E·§F·§G·§H를 그에 맞췄다. 근거는 09-02 런타임 실측(`server_DB`, G4 미커밋 진행 중).
>   ⛔ 소유자 지시: **저쪽이 굳은 뒤에** 툴 쪽 계획을 다시 점검하고 착수한다(§0-1).

---

## 0. ⚠ 2026-08-17 단순화 — 계약의 절반이 사라졌다

런타임이 구조를 최대한 단순하게 만드는 방향으로 정리됐다. **실측**(`Assets` 전체 grep):

| 개념 | 전 | 지금 | 이 문서에서 |
|---|---|---|---|
| **세이브/로드** | `VNSaveData` · 시킹 복원 | **0건** | 옛 §C가 근거를 잃음 |
| **변수 저장소** | `VariableStorageBehaviour` 공유 | **0건** | 옛 §D1·D2·D4 폐기 |
| **서브 레인(Pres 노드)** | `pres_start`~`pres_end` | **0건** (`pres_*` 전멸) | 옛 §A3~A7·§B 폐기 |
| **SubLane · BeatLane** | 레인 타입 | **0건** | — |
| **진행(Progression)** | `ChapterEpisodeProgressionSO` | **0건** — 별도 저장소로 이사 | 옛 §G의 대상이 바뀜 |
| **선택지 리플레이** | `ChoiceReplay` 어셈블리 | **0건** | 옛 §C3 폐기 |

**추가로 사라진 것** (2026-08-18 실측 — 첫 조사 때 `find`가 낡은 디렉터리 항목을 보여
줘 잘못 적었던 것을 바로잡는다): **원샷 레인**(`OneShot` 0파일, `<<beat>>`는 테스트 픽스처
문자열에만 남음) · **인라인 advance**(`InlineAdvance`·`[adv/]` 0파일).

**살아남은 것**: 메인 레인 **하나** · 롤백 · 백로그 · 선택지와 효과 미리보기 ·
연출 커맨드 · 라인 메타태그.

**진행 계층은 `ked-progression`으로 빠졌다** — 유니티 전용이 아니라 **VnTool과 런타임이
같은 구현을 공유하는** 순수 C# 패키지다. 규칙이 양쪽에 각자 있어 실제로 갈렸던 것이
이유다(조건 종류 5종 vs 2종, 연산자가 양쪽에 각각 둘씩 없고, 관문 위치가 정반대, clamp는
툴에만 있었다). **구현을 하나로 만들면 갈릴 수가 없다.**

```
[VnTool]  ──┐
            ├──► Ked.Progression   (netstandard2.1 · 엔진 의존 0 · JSON 파서 없음)
[Unity]   ──┘
```

## 0-1. ⚠ 2026-09-02 재편 — 진행 코어가 챕터 안으로 들어왔고, Scene이 섰다

런타임 실측(`server_DB`, 09-02 — G4 작업이 **미커밋으로 진행 중**인 트리. 내 두 읽기 사이에
`ProgressionDriver.cs`가 바뀔 만큼 살아 있었다). 바뀐 것 다섯:

| 무엇 | 전 (08-24 기준) | 지금 |
|---|---|---|
| **`Ked.Progression`의 자리** | 별도 저장소 `ked-progression`, UPM `#0.2.0` | **`Assets/Scripts/Ked.Progression/`에 복사 반입** (`Documentation~/vendoring.md`). 정본은 여전히 형제 저장소지만 **실제 작업은 사본에서** 일어나고, 정본은 08-25 커밋에 08-26 역동기화가 미커밋인 채 뒤처져 있다 |
| **수명 계층** | 시나리오 > Chapter > Episode | **시나리오(회차) > Chapter > Scene(신설) > Episode.** 에피소드는 연출적으로 투명하고, Scene이 연출 연속(무대·스코프)·롤백·변수 체크포인트·커밋 확정·저장의 네 경계를 전부 갖는다 — *"장면 안에서는 모든 게 물릴 수 있고, 장면이 끝나면 확정된다."* (`docs/scene-boundary-plan.md`, 관문 G0~G5) |
| **커밋 시점** | 선택 즉시 | **장면 끝에서 fold** — 장면 안 선택은 pending, 롤백이 물리고, 장면을 나가는 순간 원자 확정·보고 1회 (`ProgressionState.Fold` = `Commit` 합성). 장면 중간 멈춤은 pending을 버린다 |
| **[2] 스탯 ↔ Yarn** | `PublishStats`로 Yarn 변수에 투영 | **투영 철거(G0).** Yarn 저장소는 순수하게 [3] 연출 변수의 집. **대사 안 `<<if $스탯>>` 금지** — 스탯 분기는 간선으로 |
| **세이브** | 없음 (§C1·C3 보류) | **돌아왔다** (08-31) — 로컬 우선 슬롯 + 서버 동기화 큐 + 챕터 JSON checksum 대조 (§H-6) |

실행 루프는 셋으로 갈렸다: `ProgressionDriver`(챕터 루프 — 어느 장면 다음에 어느 장면, Yarn
변수 챕터 초기화) → `SceneRunner`(장면 루프 — 노드 재생 → 시청 기록 → `Fold(pending)`으로
판정 → 선택 → Via → … → 장면 끝 fold) → `EpisodePlayer`(장면 진입 묶음·노드 실행·리플레이
준비). 롤백은 복원이 아니라 **장면 루트부터 결정론적 리플레이**이고, 기록된 선택은 시크 중
자동 응답한다. 선택지 대기 중에도 롤백된다.

**이쪽에 생긴 일**은 §G-10~G-14에, **소유자 결정**은 §H-8에 있다. ⛔ **소유자 지시
(2026-09-02): 저쪽 작업이 끝나 완전히 굳은 뒤에 툴 쪽은 계획을 다시 점검하고 착수한다.**
그때까지 이 문서는 "무엇이 바뀌었는가"의 기록이지 작업 지시가 아니다.

**갈림 실측 (2026-09-02)** — 건강한 것과 아닌 것:

| 사본 | 상태 |
|---|---|
| `Ked.Presentation.Core` | **실질 갈림 0** — CRLF를 빼면 `SlideMotion.cs`·`StageReducer.Staging.cs` 79줄, 코드는 삼항식 줄바꿈 1건, 나머지는 이쪽이 더 풍부한 `///` 주석 |
| 커맨드 어휘 | **차이 0** — 런타임 등록 126(리터럴 125 + `$"{frame}fr"` 동적 1) ↔ 카탈로그 126항목. ⚠ §E1·§G-3의 "179"는 낡은 수치였다 |
| `ExportedTuning` | 08-22 이후 변화 없음 |
| **`Ked.Progression`** | ⛔ **갈렸다** — 이쪽 `src/Ked.Progression`(08-25 반입)은 런타임 사본 대비 **12파일 821줄** + 저쪽에서 사라진 파일 셋(`EndingRule.cs`·`ScenarioAdvance.cs`·`ScenarioTransition.cs`)이 남아 있다. 이쪽 로더는 `EndingRules`를 아직 읽고 `SceneId`·`VerifySceneEntries`를 모른다 → **`CoreRefusedChapter` 관문이 저쪽보다 느슨하다.** `SceneId`를 내지 않는 동안은 퇴화 상태라 안 보이지만, 내기 시작하는 순간 "저작 통과 · 게임 로드 실패"가 실전이 된다(§G-11) |

---

# 1부 — 연출 런타임 (`.yarn` 텍스트)

## A. 레인 구조

**A1. 레인은 없다 — 대본 하나다.** Story 노드 하나에 대사·조건·선택지·연출이 모두 선다.
`pres_*`도 `<<beat Set_X>>`도 출력하면 미등록 커맨드 오류가 난다.

**합치는 쪽이 저작 도구다** (2026-08-18 소유자 결정). 런타임이 여러 레인을 읽고 동기화하고
그것을 다시 롤백·세이브에 반영하는 값을 치르지 않기로 했다 — *"디버깅비용이 최소 10배
이상은 드니까."* 그래서 **VNEditor(이 도구)가 내보낼 때 미리 합친다.**

**A2. 연출 커맨드는 인라인이다.** 노드 셋업 커맨드는 대본 머리에, 줄에 붙은 연출은 자기
대사 줄 **바로 앞**에 선다. 실제 출력:

```yarn
title: golden_ep
---
<<camera wide>>                  ← 노드 셋업

<<camera closeup>>               ← 이 줄의 연출
라루: 첫 줄 그대로 #line:ln_004

<<if $__t1_sf_test_favor >= 5>>
    <<character_acting smile>>
    윌로: 갈래 안 대사 #line:ln_005
    <<detour A로_간다>>
<<endif>>
===
```

**A2-0. 노드 머리에 초기값 `set`은 없다** (2026-08-24 — 호스트 실측 요청
`work-orders/chapter-scope-variables-orders.md`). 예전에는 설정노드의 할당이 **모든 대사
노드의 머리**에 `<<set $x = 0>>`으로 박혔고, 그래서 그 초기화의 수명이 **에피소드**가
됐다 — 에피소드1에서 켠 "열쇠를 찾았다"가 에피소드2 머리에서 지워졌다.

연출 실행 변수(`__t1_…`)는 **챕터 단위로 산다.** 그래서 초기값은 `declarations.yarn`의
`<<declare>>`로만 나가고, **되돌리는 일은 런타임이 챕터 진입에서 한 번** 한다
(`ProgressionYarnBridge.BeginChapter` — 저장소를 비우고 `YarnProject.InitialValues`로
다시 심는다). 챕터 안에서는 아무것도 안 지운다 — 에피소드 경계를 그대로 넘는다.

⚠ 작가가 **줄에** 단 `<<set>>`은 그대로 나간다. 그건 이야기 도중의 변화다.

**A2-1. 조건 갈래의 커스텀 씬은 `<<detour>>`다** (2026-08-21). 갈래에 단 커스텀 씬을
재생하고 **갈래로 돌아와** 나머지 대본을 계속한다 — jump면 갈래 뒤의 대사가 전부 죽는다.
`<<jump>>`로 나가는 것은 둘뿐이다: 선택지 옵션의 출구(고르면 그 씬으로 이동)와 기본 출구.
detour는 YarnSpinner 3.x 렉서 키워드라 런타임 등록이 필요 없다.

**A2-2. 커스텀 씬 노드에는 출구가 없다** (2026-08-21 소유자). detour로 재생된 씬은 자기
대본이 끝나면 **호출한 갈래로 돌아가는 것**이 곧 출구다 — 뒤에 `<<jump>>`가 서 있으면
돌아가지 못한다. 그래서 커스텀 씬의 Story 노드는 언제나 `===`로 그냥 끝나고, 커스텀 씬끼리
잇는 것도 씬 안의 조건 갈래(`<<detour>>`)가 맡는다. 기본 출구(`<<jump>>`)를 낼 수 있는 것은
엑셀노드(에피소드)뿐이다. 구판 프로젝트의 커스텀 배선 데이터는 지우지 않고 조용히 무시한다.

**A3. 메인 레인 전용 검사는 없어졌다.** 레인이 하나뿐이라 가릴 대상이 없다.
⚠ 카탈로그의 `mainLaneOnly` 플래그는 이로써 **아무 데서도 안 쓰인다** — 레인이 다시
생기면 그때 검사도 함께 돌아온다.

**A4. `EpisodePlayer`는 Yarn 노드 이름 하나로 시작한다.** `StartGameAsync(string nodeName)` —
챕터도 에피소드 구조도 모른다. 그 결정은 2부가 내리고 런타임은 **결정된 노드 이름만** 받는다.
(`Assets/@Scripts/Game/EpisodePlayer.cs`)

## B. 진행 단위

**락스텝 예산 계산은 사라졌다.** 옛 §B의 "Pres 사본의 라인 수는 Story의 (대사 라인 +
`[adv/]` 마커)와 정확히 같아야 한다"는 규칙도, `[adv/]` 마커 자체도 지킬 대상이 없다
(런타임 실측 0파일 — 이미터도 2026-08-18에 마커 출력을 걷었다).

| 요소 | 소비 |
|---|---|
| 일반 대사 라인 | 1 |
| `#beat` 메타 라인 | 1 (박스·타자기 없음) |
| 선택지(`->`) 블록 전체 | 0 |

`SyncHub`는 남아 있지만 **역할이 바뀌었다** — 레인 간 락스텝이 아니라 **롤백 시킹 동기
조절용**이다(2026-08-17 `253ef6c4`).

## C. 식별자

**C1. `#line:<LineId>` 태그는 이제 요구되지 않는다.** 근거였던 세이브 시킹이 사라졌다.
런타임 자신도 쓰지 않는다 — 코드 주석이 **"이 프로젝트의 yarn 원문에는 `#line` 태그가
없다(실측: 8개 파일 전부 0건)"**라고 적어 두었다
(`Assets/@Scripts/Game/StageEquivalenceHarness.cs:21`). 롤백은 한 세션 안에서만 되감으므로
Yarn 암묵 ID로 충분하다.

> **그래도 LineId를 버리지 말 것.** VnTool 쪽에서 LineId는 **연출이 매달리는 열쇠**이자
> 최상위 불변식이다(architecture-decisions A-2). 런타임이 요구하지 않을 뿐이다.
> ✅ **세이브가 돌아왔는데도 되살아나지 않았다** (2026-09-02) — 스냅샷 기반이라 재개
> 지점이 라인이 아니라 **장면 루트**다(§H-6). `#line:`은 계속 우리 쪽 열쇠로만 산다.

**C2. 노드 타이틀은 여전히 진입 키다.** `EpisodePlayer`가 받는 것이 이 이름이고,
2부의 `EpisodeNode.DialogueEntryId`가 이 값과 문자열로 만난다. **한 번 출시된 노드
타이틀은 동결.** 코드가 강제하는 명명 규칙은 없다 — `Story_`·`Set_` 접두는 저작 관례였고 2026-08-24에 걷혔다.

**C3. ~~선택지 리플레이는 위치 기반~~ → 제약이 다른 모양으로 돌아왔다 (2026-09-02).**
`ChoiceReplay`는 사라졌지만, 런타임의 **서버 동기화 큐가 선택을 `OptionIndex`(원본
`NextOptions`의 서수)로 보낸다**(`Save/SaveData.cs` `PendingChoice`). 즉 `간선` 시트의
**행 순서가 서버 이력의 열쇠**다 — 출시 후 행을 기존 항목 위에 끼우면 화면이 아니라
**서버에 쌓인 이력의 뜻이 바뀐다.** "삽입 금지"가 세이브가 아니라 서버 쪽 근거로 되살아났다.

**C4. 챕터 JSON은 바이트 단위로 대조된다** (2026-09-02). 런타임 `ChapterVersionResolver`가
에셋 바이트의 **SHA-256**을 서버의 버전 checksum과 맞춰 보고, 맞는 버전이 없으면 동기화를
접는다("에셋을 서버에 수입시켜야 동기화 가능"). 그래서 **같은 워크북이면 같은 바이트**여야
한다 — 내보내기의 정렬·포맷이 결정적인지가 계약이 됐다(§G-12).

## D. 선택지

**D1. Yarn 변수는 살아 있다 — 이제 [3] 연출 실행 상태의 집이고, 그것뿐이다.**
`<<declare>>` · `<<set>>` · `<<if>>`는 그대로 컴파일되고 동작한다. 수명은 **챕터**
(`ProgressionYarnBridge.BeginChapter`가 선언 초기값으로 되돌린다 — §A2-0), 그리고
2026-09-02부터 **세션을 넘어선다**: 장면 끝에 통덤프(`YarnVariableSnapshot` — float·string·
bool 세 사전)가 세이브에 들어가고, 이어하기가 `BeginChapter` 위에 덮는다(덤프에 없는 신규
`declare`는 초기값으로 남는다).

→ **작가 계층(B) 변수는 그대로 내도 된다** — 챕터 안에서 살고, 이제 세이브까지 따라간다.
→ ⛔ **A계층 스탯은 Yarn에 없다** (G0, 2026-09-01). 예전에는 진행 층이 `PublishStats`로
[2] 스탯을 Yarn 변수에 투영했는데 그 다리를 걷었다 — **대사 안에서 `<<if $스탯>>`은 금지**,
스탯 분기는 챕터 그래프의 간선으로 올린다. 이쪽 A계층 격리(설정노드에 챕터 스탯이 안 오는
것)와 같은 결이다. 이미터가 `$스탯`을 대사 조건으로 내지 않음을 지키는 검증 한 줄은 §G-14.
→ ~~`<<set>>` 이중 실행 주의~~는 서브 레인이 없어져 함께 사라졌다.

**D2. 옵션 해시태그는 표시 전용이다.** `-> 텍스트 #fatigue:+10`은 미리보기 라벨만 만든다.
키는 소문자 정규화, 정수만(`+1.5`는 조용히 버려짐), `~`로 범위 표기 가능.
(`VNOptionEffectPreviewParser`)

> **⚠ 실제 효과는 이제 여기서 나오지 않는다.** 옛 규칙은 "효과는 옵션 본문의 `<<set>>`이
> 담당"이었는데 그 자리가 사라졌다. **스탯이 변하는 유일한 자리는 2부의
> `EpisodeOption.StatChanges`다.** 해시태그는 순수한 미리보기 문구다.

**D3. ⚠ 옵션 라벨은 원문 그대로 렌더된다.** 접두를 벗기는 코드가 없다 — 이미터는 라벨에
식별 접두(`s1` 등)를 넣지 말 것. (`VNOptionsPresentationFlow`)

**D4. 비활성 옵션은 사라진다.** `<<if>>` 거짓인 옵션은 회색 처리가 아니라 미표시.

## E. 커맨드 어휘

**E1. 등록 지점 179곳** — `YarnBridge/CommandBridge*.cs` 실측(본체 160 · 이모지 프리셋 12 ·
이모지 6 · 컨트롤 1). 옛 계약서의 "명명 커맨드 200개 + `1fr`~`48fr` 별칭 48개 · 카탈로그
201항목"은 **단순화 이전 수치다.**
→ ✅ 2026-08-18 재검증 완료 — 카탈로그가 **179항목**(등록 178 + 동적 별칭 `<N>fr`)이고 런타임에만 있는 것은 0개다. 죽은 22개(`pres_*`·`overlay_*`·`beat`·`beat_fx`·`seq`)를 걷었다.
→ ✅ **2026-09-02 재실측 — 126.** 등록 자리 126(문자열 리터럴 125 + `$"{frame}fr"` 동적 1) ↔ 카탈로그 126항목(`outputCommand` 고유 126, `<N>fr`이 동적 등록과 짝). **런타임에만 0 · 카탈로그에만 0.** 179 → 126은 W65(2026-08-20, 56종 폐지)의 결과이고 이 문서가 그 수치를 안 따라갔던 것이다. ⚠ 등록 이름은 `runner.AddCommandHandler<…>(` **다음 줄**의 문자열이라 한 줄 grep으로는 0건이 나온다 — 다중행으로 뽑을 것.

**E2. 레인 구분이 사라져 "메인 레인 전용"도 뜻을 잃었다.** 커맨드가 갈 곳이 대본 하나뿐이라 가릴 대상이 없다(§A3). `pres_*`는 전멸했고 `<<beat Set_X>>`도 마찬가지다.

**E3. 커맨드 기본값은 여러 층에 있다** — 스펙 클래스 필드 초기값 · 브리지 파싱 기본 인자
(**가장 큰 덩어리, 툴이 명시 인자를 출력하면 완전 우회 가능**) · 토큰 파서 폴백 ·
프리셋(키가 곧 어휘).

**E4. 매크로 커맨드.** `show`→4스펙, `cast`→4, `emoji`→6 등 1:N 확장이 있다. 툴은 텍스트
커맨드 하나로 출력하면 된다.

**E5. 라인 메타태그 어휘** (`DialogueBoxMetadataResolver` — 소문자 정규화, 첫 일치 우선).
**실측으로 다시 뽑았다**:

- 비트: `#beat` `#stage` `#stage_only` `#present`
- 대기 수정자: `#stay` `#beat_stay` `#no_auto` `#hold`
- 박스 종류: `#surface` `#surface_box` `#surfacebox` · `#portrait` · `#speaker` ·
  `#letterbox` `#letter_box` · `#onlytext` `#only_text` · `#blackbook` `#black_book`
- 박스 전환: `#box_keep` `#boxkeep` · `#box_cut` `#boxcut` · `#box_fade` `#boxfade` ·
  `#box_fade_in` `#boxfadein` · `#box_hide` `#boxhide`
- **`#main_free`는 없어졌다** — 서브 레인 전용이었다.

---

# 2부 — 진행 계약 (`progression.json` → `Ked.Progression`)

**대상이 바뀌었다 — 두 번.** 옛 §G는 유니티의 `ChapterEpisodeProgressionSO`를 겨눴고,
2026-08-23에는 `ked-progression` UPM 패키지(`#0.2.0`)를 겨눴다. **2026-09-02 실측: 저쪽이
UPM을 버리고 `Assets/Scripts/Ked.Progression/`에 복사 반입했다**("매일 바뀌는 중이라
커밋→태그→푸시→재해결 왕복이 가장 큰 비용" — `Documentation~/vendoring.md`). 그래서 지금
이 문서가 겨누는 **로더·DTO의 실물은 런타임 사본**이다.

| 사본 | 어디 | 상태 (2026-09-02) |
|---|---|---|
| 정본 | `C:\Users\river\Documents\GitHub\ked-progression` `feat/host-integration` | 마지막 커밋 08-25 + **08-26 역동기화가 미커밋** — `Fold`·`SceneId` 없음. **사본보다 뒤처져 있다** |
| 런타임 사본 | `ked-presentation-runtime/Assets/Scripts/Ked.Progression/` | **실제 작업이 여기서** — Scene·fold·`VerifySceneEntries`. 유니티 EditMode 테스트 14개(09-02 신설) |
| 이쪽 사본 | `src/Ked.Progression/` | 08-25 반입. 런타임 대비 **12파일 821줄** 뒤 + 사라진 파일 셋 잔존 (§G-11) |

**Gate D는 닫혔다 (2026-08-23)** — 로더·DTO·전이기가 서서 실제로 몰아 봤다(H1·H2·H4).
그 뒤 저쪽이 세이브를 코어에서 걷어 호스트 `Save/` 층으로 옮겼다(§H-6).

## F. 타입 대응 — JSON ↔ 모델

`ChapterProgressionExporter`가 내는 것과 코어 타입의 대응. **키는 PascalCase다.**
**2026-09-02 실측 — 런타임 DTO가 08-26에 파일 단위로 쪼개지고(`7eedce6b`) 레거시가 걷혔다.**
저쪽이 지금 읽는 칸 전부:

```
{ ChapterId, DisplayName, StartEpisodeId, Stats[], Nodes[] }

Stat   : { Key, DisplayName, Type("Number"|"Bool"), Initial, Minimum, Maximum }

Node   : { EpisodeId, DialogueEntryId, Title, EventKey, SceneId(선택), NextOptions[] }

Option : { TargetEpisodeId, ChoiceLabel, VisibleConditions[], Conditions[],
           LockedReasonText, ViaNodeId, StatChanges[{Key, Amount, Op("Add"|"Set")}] }

조건   : { Kind("Stat"), Key, Op, IntValue }
```

| JSON | 모델 | 비고 |
|---|---|---|
| `ChapterId` · `DisplayName` · `StartEpisodeId` | `ChapterProgression` | 시작은 실재 검사됨 |
| **`Stats[]`** | `ChapterProgression.Stats` | ✅ 2026-08-18부터 나간다. ⚠ `Type`은 **`"Number"`**다(이쪽 `Int`를 번역) |
| `Nodes[]` | `EpisodeNode` | |
| `NextOptions[]` | `EpisodeOption` | **배열 순서 = 화면 순서 = 서버 이력의 `OptionIndex`**(§C3). 정렬 금지 |
| **`Node.EventKey`** | `EpisodeNode.EventKey` | ✅ **양쪽 다 섰다** — 이쪽 v14(08-26)부터 내고, 저쪽 DTO 칸도 같은 날(`665e47e1`). 해석 없이 실어 나르고, 장면 끝 fold에서 시청 보고(`PendingEvent`)의 열쇠가 된다. "칸 부탁 중"은 끝 |
| **`Node.SceneId`** | `EpisodeNode.SceneId` | ⛔ **이쪽이 아직 안 낸다 (§G-10, 대기).** 선택 칸 — 비면 저쪽이 `__scene_{EpisodeId}`를 발급해 **에피소드 하나 = 장면 하나인 퇴화 상태**가 된다(매 에피소드 무대 클리어, 에피소드를 넘는 롤백 없음). 불변식: 장면마다 밖에서 들어오는 자리 하나(재진입 허용). 이어하기는 장면 루트에서만 |
| ~~`EndingRules`~~ | **저쪽에서 폐지** (`5e424840`) | 이쪽은 여전히 빈 배열을 낸다 — Newtonsoft가 모르는 칸을 버려 무해. 재반입(§G-11)하면 이쪽 DTO에서도 사라진다 |
| ~~`Option.Kind`~~ | **저쪽에서 폐지** (`adfda627` — 자동 진행 자체가 폐지) | §G-7 종결. 이쪽이 내는 값은 버려진다 |
| ~~`Option.HideWhenLocked`~~ | **저쪽 DTO에 없다** | 옛 F4의 "저쪽 DTO는 그대로 둔다"는 낡았다 — 저쪽이 걷었다 |
| ~~`Node.EndingKey`·`IsChapterEndingCandidate`·`IndexText`·`Position`·`DesignerNote`·`VisibleConditions`·`UnlockConditions`·`Attachments`~~ | **저쪽 DTO에 없다** (`7503462f`) | 이쪽이 내는 빈 값은 버려진다(저쪽 견본 `qwer.progression.json`이 이쪽 08-27 산출물이고 이 칸들이 8개씩 들어 있다 — 무해). 재반입 때 함께 걷는다 |

**enum은 이름 문자열로 나간다** — 순서를 재배열해도 안 깨진다. 로더가 이름으로 맵핑하고,
**알 수 없는 이름은 로더만의 검사 대상이다**(모델은 이미 enum이라 못 잡는다).

**F1. `IntValue`는 0이면 키 자체가 없다.** `JsonIgnoreCondition.WhenWritingDefault` —
`trust >= 0`은 `{ "Kind":"Stat", "Key":"trust", "Op":"GreaterOrEqual" }`로 나간다.
**DTO 필드는 반드시 `int`** — `int?`로 두면 가장 흔한 조건인 `flag == false`가 통째로
어긋난다.

**F2. 비교 연산 5종 (+`Exists`는 죽었다).** `GreaterOrEqual` · `LessOrEqual` · `Equal` ·
`GreaterThan` · `LessThan`(2026-08-16 개방). **`NotEqual`은 저작 파서가 닫아 두어 나오지
않는다 — 넣지 말 것.** `Exists`는 `EpisodeCleared` 조건의 것이었고 그 종류가 2026-08-25에
양쪽에서 폐지됐다(`cleared:` 문법 → `ClearedRetired` 진단) — 나올 일이 없다.

**F3. bool 스탯은 0/1 + `Equal`뿐이다.** 경계 0·1 고정, **크기 비교와 증감은 오류**.
양쪽이 같은 자리에서 막는다(`ChapterWorkbookReader.VerifyBoolStatUsage` ↔
`ChapterProgression`). `== false`는 F1 때문에 `IntValue` 키가 없는 모양으로 나간다.

**F4. 관문은 간선의 것이다** (v8). `Option.VisibleConditions` 미달 → **목록에 만들지
않는다**(`ChapterAdvance.HiddenCount`로 개수만 센다 — 디버그용). `Option.Conditions` 미달 →
**잠긴 채 보인다**(`ResolvedOption.Locked` — `LockedReasonText`와 첫 미달 조건을 든다).
규칙의 주인은 `ChapterTransition.Resolve` 하나다.

~~`HideWhenLocked`~~ — 2026-08-24에 이쪽이 `잠금시 숨김`을 폐지했고(표시조건과 해금조건이
이미 그 둘을 다 말한다), **저쪽도 DTO에서 칸을 걷었다**(2026-09-02 실측 — `EpisodeOptionDto`에
없다). 이쪽이 아직 `false`를 내지만 Newtonsoft가 버린다. 재반입(§G-11) 때 이쪽 출력도 걷는다.

**F5. 전이 규칙 2갈래 — 그리고 커밋은 장면 끝이다** (2026-09-02 정정).
① 고를 수 있는 선택지가 하나라도 있으면 **`AwaitPlayerChoice`** — 고른 간선의
`TargetEpisodeId`로 이동. `StatChanges`는 **즉시 커밋되지 않는다**: 장면 안의 선택은 pending으로
쌓이고(`SceneRunner._picks`), 롤백이 그것을 물리며, **장면을 나가는 순간 `ProgressionState.Fold`가
순서대로 `Commit`을 접어 원자 확정**한다(fold = Commit 합성이라 clamp·도달 확인 규칙이 갈릴 수
없다). 장면 중간 멈춤은 pending을 **버린다**(확정도 보고도 안 한다).
② 고를 수 있는 것이 없으면 **`ChapterEnded`** — 잠긴 것만 남아도 종료다.

⛔ **"보이지 않는 기본"은 없다.** `ChoiceLabel`이 빈 간선은 **로드 오류**다
(`ProgressionLoader` — *"간선의 문구가 비어 있다. 자동 진행은 폐지됐다 — 문구를 줄 것"*).
이쪽 v12 리더(`HasNoOptionLabel` 오류)와 같은 자리에서 막는다.

**판정은 작업 상태로** — 진입 상태에 지금까지의 pending을 접은 값 = 플레이어가 선택지를
보는 시점의 값. "커밋 전 값으로 판정"이라는 옛 문장과 결과가 같다.

**F6. 생성자를 통과했다는 것의 뜻.** 에피소드 ID·스탯 키 중복 없음 · 시작 실재 ·
**모든 간선이 실재하는 에피소드에 착지** · 조건과 스탯변화가 **정의된 스탯만** 가리킴 ·
bool 어휘 준수 · **장면마다 밖에서 들어오는 자리가 하나**(`VerifySceneEntries`, 2026-09-02 —
같은 자리로 여러 간선이 들어오는 것과 장면을 나갔다 되돌아오는 재진입은 통과). 그래서
전이기와 도달성 증명은 "허공으로 가는 간선"이나 "없는 키를 읽는 조건"을 다시 걱정하지 않고,
이어하기는 **장면 루트에서만** 재개할 수 있다(무대 기준선이 거기 선다).

**F7. 침묵 금지가 규율이다.** 없는 스탯 키를 0으로 읽지 않고 던진다(구 런타임은 조용히
`false`였다). 초기값이 경계 밖이면 생성 거부 — 조용히 clamp하면 작가가 쓴 값과 다르게
시작한다. 문구 없는 간선·엉터리 장면 데이터도 **로드에서** 죽인다 — 재생 중에 발견되면
디버깅 비용이 크다.

---

# 3부 — 열린 항목

## G. VnTool이 할 일

**G-1. `Stats[]` 내보내기 — ✅ 2026-08-18 완료.**

`ChapterJson.Stats`로 나간다. ⚠ **타입 이름을 번역한다** — 이쪽 `Int`가 저쪽 `Number`다
(enum이 이름 문자열로 나가므로 그대로 내면 수입기가 모르는 이름을 만난다).
고정: `스탯_정의가_최상위로_실려_나간다`. 아래는 그때의 사정 기록이다.

`ChapterJson`이 `ChapterId · DisplayName · StartEpisodeId · Nodes · EndingRules`뿐이라
`Stats`가 없다. 그래서 **스탯 관문이 있는 실데이터로는 `ChapterProgression`을 만들 수
없다** — "정의되지 않은 스탯"으로 거부된다. 저쪽이 **의도적으로** 막아 둔 것이고(F7),
그 압력이 이 항목을 가리킨다.

값의 주인은 챕터 워크북 `스탯` 시트다 — 같은 `trust`라도 챕터마다 초기값이 다를 수 있어
게임 단위가 아니라 챕터에 실린다. 툴의 도달성 증명은 이미 `Math.Clamp(값, 최소, 최대)`로
걷는데 런타임은 경계를 모른다. **경계가 실려 나가지 않으면 증명과 실제 플레이가 갈린다.**

→ `ChapterProgressionExporter.ChapterJson`에 **`Stats` 한 줄 + 매핑 하나.**
한 행 = `(Key, DisplayName, Initial, Minimum, Maximum, Type)`.

**G-2. 서브 레인 출력 경로 제거 — ✅ 확인 완료 (2026-08-19).** 이미터가 내는 것은
`StoryFileName` 하나뿐이다. `SetPrefix`·`PresPrefix` 상수는 **파일 인식에만** 남아 있고
(고아 정리가 옛 프로젝트에 남은 `Set_*`·`Pres_*`를 "우리 것"으로 알아봐야 지운다) 생성
경로는 없다 — `FileNameOf`를 부르는 자리가 그 둘뿐인 것으로 확인했다.

**G-5. `Option.ViaNodeId` 내보내기 — ✅ 2026-08-19 완료.** 간선의 `연출` 칸이
`NextOptions[].ViaNodeId`로 나간다. 자동 진행 간선에도 붙고, 없으면 빈 문자열로 언제나
낸다. ⚠ 저작 이름(`PresentationNodeName`)과 계약 이름이 갈리는 자리라 **키 이름을 글자
그대로** 붙드는 테스트를 걸었다 — 틀리면 역직렬화기가 조용히 버려 오류 없이 연출만 사라진다.

**G-6. 깃발(bool 스탯) 지정 — ✅ 2026-08-23 완료.** `ked-progression` `0.2.0`에
`StatChangeDto.Op`가 서서 `BoolSetNotCarried` 거부를 지웠다. `StatChanges`의 각 항목이
`Op`를 함께 싣는다 — `"Set"`은 깃발 지정(값은 0/1), `"Add"`는 정수 증감.
⚠ **`"Add"`를 비우지 않고 명시한다**: 저쪽은 빈 문자열도 더하기로 읽지만(구 JSON 호환),
적어 두면 "아무도 안 정한 것"과 "더하기로 정한 것"이 JSON에서 구별된다.
규격: [`work-orders/bool-stat-orders.md`](work-orders/bool-stat-orders.md).

**G-7. ~~`Option.Kind` 내보내기~~ — 종결 (2026-09-02).** 저쪽이 칸을 세우는 대신
**자동 진행 개념을 통째로 폐지했다**(`adfda627`, 08-26 — 문구 없는 간선은 로드 오류, §F5).
이쪽이 내는 `Kind`는 Newtonsoft가 버린다. 재반입(§G-11) 때 이쪽 출력에서도 걷는다 — 간선
`종류` 열 자체는 이미 v12에서 폐지됐다.

**G-9. 내보내기가 코어 로더를 지난다 — ✅ 2026-08-23 완료.** `ChapterProgressionExporter`가
낸 JSON을 `Ked.Progression`의 `ProgressionLoader`에 **실제로 실어 보고**, 오류 진단이 하나라도
나오면 파일을 내지 않는다(`CoreRefusedChapter`).

⚠ **왜 검증 규칙을 베끼지 않았나** — 툴 검증이 경고로 넘긴 것을 코어가 거부하는 자리가
실재했다(문구 없는 간선에 걸린 관문). 그때 이쪽 심각도를 올려 맞추는 길은 버렸다: 같은
규칙이 두 곳에 살면 저쪽이 하나 늘릴 때마다 또 갈리고, 그것이 이 저장소 셋이 없애려는
바로 그 병이다. **저쪽 판정을 그대로 받는다.**

코어 진단의 경로(`Nodes[ep].NextOptions[i]`)는 엑셀 시트·행으로 번역해 싣는다 —
"어딘가 잘못됐다"는 보고는 이 레이어에서 실패다. 코어의 **경고는 막지 않는다.**

⛔ **2026-09-02 — 이 관문이 저쪽보다 느슨해졌다.** 이쪽 로더가 08-25 사본이라
`VerifySceneEntries`를 모르고 `EndingRules`를 아직 읽는다. `SceneId`를 내지 않는 동안은
퇴화 상태(에피소드 = 장면)라 갈림이 안 보이지만, 내기 시작하는 순간 "저작 통과 · 게임 로드
실패"가 실전이 된다 — 08-24에 하루 겪은 그 모양. 답은 §G-11이다.

**G-8. `DialogueEntryId` 이름 규칙 — ✅ 2026-08-23 완료.** ⚠ **`대사엔트리`가 적힌 글자
그대로 나가지 않는다.** `YarnBundleEmitter.StoryNodeTitleOf`를 통과한다:

```
DialogueEntryId = SanitizeNodeName(대사엔트리)
                  └ 영숫자·밑줄이 아닌 글자를 전부 '_'로  (`장면 1` → `장면_1`)
```

⛔ **`Story_` 접두는 2026-08-24에 폐지됐다** (소유자 — 런타임도 같은 날 `Story_*` 필터를
걷었다). 접두가 **두 번** 붙고 있었다: 기획자가 `대사엔트리`에 이미 `Story_ch05_01`이라
적는데 이미터가 또 붙여 **`Story_Story_ch05_01`**이 나갔다(견본 여섯 줄 전부). 이제
**타이틀은 곧 대사엔트리**다 — 대사엔트리가 `Story_`로 시작하면 그 글자가 그대로 간다.

1부의 yarn 타이틀과 **한 글자도 달라선 안 된다** — 런타임이 이 글자로 `YarnProject`에서
노드를 찾는다. 2026-08-23까지 이 자리만 규칙 밖에 있어서 진행 JSON은 `new01`, yarn은
`Story_new01`로 갈렸다(로드·검증·증명은 전부 통과하고 재생만 안 됐다. 호스트의
`ProgressionContentPreflight`가 잡았다). **바깥에서 이름을 손으로 조립하지 말 것** —
접두가 없어진 지금도 **정규화가 남아 있어** 규칙은 여전히 두 단계이고, 주인은 이미터
하나다(`StoryNodeTitleOf`).

⚠ **파일 이름도 접두가 없다**: `{대사엔트리}.yarn`. 그래서 `Story_*` 같은 이름 필터로
산출물을 고르면 안 된다 — 폴더의 `.yarn` 전부가 대상이고, 무엇을 썼는지는 툴이
`.vntool-output.json`에 적어 둔다. 선언 파일만 이름이 고정이다(`declarations.yarn`).
규격: [`work-orders/dialogue-entry-naming-orders.md`](work-orders/dialogue-entry-naming-orders.md).

**G-3. 커맨드 카탈로그 재검증 — ✅ 2026-08-18 완료.** 죽은 22개를 걷어 **179항목**으로 맞췄다(등록 178 + 동적 별칭 `<N>fr`). `docs/game.definition.json`은 문서가 아니라 **내장 리소스로 링크된 실물 팔레트**다 — 낡은 항목은 곧 작가가 고를 수 있는 unknown command다.
→ ✅ **2026-09-02 재실측 — 126 ↔ 126, 차이 0** (§E1). W65 이후 수치가 안 따라갔던 것뿐이고 어휘는 맞다.

**G-4. `Ked.Presentation.Core` — 패키지화를 기다린다 (소유자 방침, 2026-08-18).** 소스째
복사하는 지금 방식은 **최종적으로 패키지로 가져오기로 했으므로 재복사로 때우지 않는다.**

**실측 갱신 (2026-08-23, 동기화 후)** — 줄바꿈을 무시하고 파일 단위로 대조한 결과:

| | |
|---|---|
| 같음 | **39 파일 (전부)** |
| 실질 갈림 | **0** ✅ |
| 저쪽에만 | `Tests/EditMode/` 24 파일 — **테스트는 안 옮긴다.** 정상 |

⚠ **코드만으로는 반쪽이었다.** 런타임이 2026-08-21에 초상 에셋을 폴더 규약으로 옮기며
**코드와 튜닝 덤프를 함께** 바꿨는데, 툴은 둘 다 못 받았다. `PortraitDimensionsDto.cs`만
맞추면 덤프가 옛 규약(`"variant": "parkeunseol_a"`)이라 조회가 전부 실패한다. 그래서
`tests/Vn.Authoring.Tests/TuningFixtures/ExportedTuning/`의 덤프 넷도 같은 내보내기
(런타임 `ExportedTuning/`, 2026-08-21T14:00)로 맞췄다 —
`portrait-dimensions.json` · `rig-schemas.json` · `export-report.json` · `schema.md`.

**교훈: 이 사본의 단위는 파일이 아니라 "한 번의 내보내기"다.** 코드와 덤프는 같이 움직인다.

이 문서가 적어 두었던 *"런타임에만 있는 파일 7개"*(`StageReducer.{Placement,Portrait,Shot,Show,Slot,Staging}.cs`·
`PortraitSizingReduction.cs`)는 **낡았다 — 일곱 다 넘어와 있다.**

갈린 하나가 무는 자리:

```
이쪽   변형 키 정규화 = 마지막 글자만    'school' → 'l',  'casual' → 'l'
저쪽   변형 키 정규화 = 문자열 전체      'school' ≠ 'casual'   (런타임 38fef522, 2026-08-21)
```

`school`과 `casual`이 **툴에서만 같은 키가 된다** — 미리보기가 엉뚱한 초상 치수를 집고
게임은 올바른 것을 집는데 **오류는 하나도 안 난다.** 소비자는 `Flow/CoreStageFold.cs` ·
`Flow/MotionInspection.cs` · `Flow/StageMotionPlan.cs` · `Views/MiniStagePreview.axaml.cs`.

⚠ **재복사는 해결이 아니다.** 이 사본이 갈리는 것은 사건이 아니라 구조다 — `ked-progression`은
같은 문제를 **UPM 패키지 + 태그 고정**으로 끝냈고(`#0.2.0`, 2026-08-23) 그 절차가 이미
검증됐다. 같은 수를 여기 한 번 더 두는 것이 답이다.

✅ **2026-09-02 재실측 — 실질 갈림 0 유지.** CRLF를 빼면 `SlideMotion.cs`·
`StageReducer.Staging.cs` 79줄이 다르고 코드는 삼항식 줄바꿈 1건, 나머지는 이쪽이 더 풍부한
`///` 주석이다. `ExportedTuning`도 08-22 이후 변화 없음. ⚠ 바로 위 문단의 "UPM 패키지 +
태그 고정으로 끝냈다"는 **낡았다** — 저쪽이 그 방식을 버리고 복사 반입으로 돌아갔다(2부 머리).
"같은 수"는 이제 패키지가 아니라 **한 번의 통째 반입 + 대조 절차**다.

**G-10. `Node.SceneId` 내보내기 — ⏸ 대기 (2026-09-02 소유자: 저쪽이 굳은 뒤 계획 점검).**
접점은 확정됐다: JSON `Nodes[].SceneId`(문자열, 생략 가능), 불변식은 "장면마다 밖에서 들어오는
자리 하나"(재진입 허용), 비면 `__scene_{EpisodeId}` 자동 발급(퇴화). 저쪽 계획서가 **"툴 쪽
Scene 저작 UI·Exporter는 후속"**으로 명시했다(`docs/scene-boundary-plan.md` §5). 정할 것:
에피소드 시트의 `장면` 열(v15?) · 검증(반입한 코어의 `VerifySceneEntries`를 그대로 쓴다 — 규칙을
베끼지 않는다, §G-9) · 챕터 그래프의 장면 묶음 표시 · 프리뷰의 장면 경계(장면 시작 = 무대
클리어 — 무대 승계는 저쪽도 v1 범위 밖).

**G-11. `Ked.Progression` 재반입 — ⏸ 대기, 원천 문제 있음.** 규율은 "파일이 아니라 한 번의
통째 교체"(`src/Ked.Progression/Ked.Progression.csproj` 머리). ⚠ 정본 저장소가 사본보다
뒤처지고 미커밋이라 **원천을 런타임 사본으로 잡거나, 소유자가 정본을 먼저 커밋**해야 한다.
반입하면 이쪽에서 `EndingRule`·`ScenarioAdvance`·`ScenarioTransition`·단일 `ProgressionDto.cs`
참조 자리가 컴파일에서 드러나고, 내보내기의 레거시 칸(`EndingRules`·`Kind`·`HideWhenLocked`·
`IndexText`·`Position`·`DesignerNote`·노드 관문)을 함께 걷는다. 저쪽 Scene 작업이 굳은 뒤
§G-10과 한 번에.

**G-12. 내보내기 결정성 · 선택지 순서 규율 — ⏸ 대기 (§C3·C4).** 같은 워크북이면 같은 바이트
(checksum 대조)인지, `NextOptions` 순서를 바꾸는 편집이 서버 이력(`OptionIndex`)을 갈리게 함을
저작이 알리는지. 지금은 둘 다 확인 안 됨.

**G-13. 프리뷰 챕터 런 ↔ 런타임 판정 대조 — 작은 갈림 하나.** 무대 프리뷰의 에피소드 끝
선택지(2026-08-27)는 `ChapterTransition.Resolve`와 같은 규칙으로 가른다(표시 미달 = 목록 제외 ·
해금 미달 = 잠긴 채 표시). 다른 점 둘: 런타임은 장면 끝 fold, 프리뷰는 즉시 커밋(결과 동일 —
fold가 Commit 합성) · **고를 수 있는 것이 없으면 런타임은 챕터 종료인데 프리뷰는 전부 잠긴
목록을 보여 주고 기다린다.** 후자는 맞추는 편이 맞다.

**G-14. `$스탯`을 대사 조건으로 내지 않음을 검증 — ⏸ 대기 (§D1).** 이쪽 A계층 격리가
설정노드에 챕터 스탯을 안 주므로 지금은 나갈 길이 없지만, 저쪽이 금지를 명시한 이상
이미터 검증 한 줄이 있어야 "조용히 깨지지" 않는다.

## H. 소유자가 정해야 하는 것

**H-1. `Stats` 배달 경로 — ✅ 정해졌다 (가).** 챕터 JSON 최상위에 `Stats[]`, 로더는
`Load(dto)` 하나. 값의 주인이 챕터 워크북이므로 챕터 JSON에 실리는 것이 자연스럽다.

**H-2. 툴에 없는 세 개념을 살릴 것인가** — `Flag`는 **사실상 답이 나왔다**: 2026-08-19의
깃발 지정이 `Stat` 0/1 + `Equal` 위에 섰으므로 별도 조건 종류가 필요 없다.
남은 둘 — `ChapterCleared`(챕터 간 진행이 생기면 필요. **언제 넣을지를 적어 둘 것**) ·
`Token`(아이템/열쇠 보유 — 작가 계층에 개념은 있지만 Yarn 변수로 살고 진행 JSON에 안 나간다).

**H-3. `Option.ViaNodeId` — ✅ 2026-08-19 완료.** §G-5 참조. 저쪽 모델에는 커밋 `eb1786d`로
이미 있었다 — 이쪽이 "저쪽 대기"라고 적어 둔 것은 **저장소를 안 열어 보고 자체 작업지시서
한 줄을 옮긴 탓**이었고, 없는 병목을 하나 만들었다. 남의 저장소 상태를 적을 때는 남의
저장소를 본다.

**H-4. ~~`EndingRules`의 모양~~ — 종결 (2026-09-02).** 저쪽이 `EndingRule`·`EndingRuleDto`·
`ScenarioAdvance`·`ScenarioTransition`을 **폐지했다**(`5e424840`·`947d6c9b`, 08-26 — 이쪽 v14가
간선 엔딩키를 걷은 것과 같은 날). 진행 상태는 **챕터 수명**이고 [1] 영구 계층은 아직 없다
(`ProgressionState` 머리 주석). 챕터를 잇는 판이 생기면 그때 새 모양으로 — 옛 넷
(`progression-handoff.md` §6)은 참고일 뿐 규격이 아니다.

**H-7. 깃발의 수명 — 답이 나왔다 (2026-09-02).** **챕터다.** `ProgressionState`가 챕터
수명 객체가 됐고(`7970e352`), [1] 영구 계층은 정본에서도 제거됐다(`ba0528b`). 챕터를 넘어
사는 값은 지금 없다 — 세이브에도 `Stats`가 챕터 단위로 실린다(§H-6). 앨범/전역 진행은 여전히
빈자리.

**H-5. 부착(Attachment) 표시 조건** — v8에서 관문이 간선으로 내려가며 부착의 표시 조건이
갈 곳을 잃었다(들어오는 간선이 없다). v1 비범위. 쓸 때 `EpisodeNode`의 표시 조건을
**부착에 한해** 되살리고, "Main은 언제나 빈 배열"이라는 비대칭을 로더가 검사하게 하는 것이
자연스럽다.

**H-6. 세이브 — 돌아왔다 (2026-08-31 · G4 진행 중).** 코어의 `ProgressionSave`는 걷혔고
(`e5ce1e04`), 대신 호스트 `Assets/Scripts/Save/`(14파일)가 섰다: **로컬 우선**
`saves/slot{n}.json` + **서버 동기화 큐** `sync_queue.json` + 게스트 계정 + 챕터 JSON
checksum 대조(§C4). 세이브의 뜻은 둘뿐 — **장면 진입 스냅샷** `{ ChapterId, CurrentEpisodeId
(= 장면 루트), Stats, Variables([3] 통덤프), ChapterCompleted }` 아니면 **챕터 완료**.
장면 중간 세이브는 만들지 않고, 구세이브·콘텐츠 불일치·장면 중간 지점은 **새 게임**
("이어 가는 척하지 않는다", D-017).

옛 §C1(`#line:` 필수)·§C3(순서 동결)은 **되살아나지 않았다** — 스냅샷 기반이라 라인이
아니라 장면 루트에서 재개한다. 대신 §C3·C4가 **서버 쪽 근거**로 다른 모양의 제약을 세웠다
(`OptionIndex` · checksum).

⚠ **앨범은 이 세이브가 아니다.** "무엇을 본 적 있나"(엔딩·CG·읽은 줄)는 슬롯이 아니라
**판을 넘는 전역 진행**에 산다. 서버 큐의 `PendingEvent`(EventKey 시청 보고)가 그 씨앗이지만
슬롯 세이브와는 별개다.

**H-8. Scene 저작의 규격 — 소유자 결정 대기 (2026-09-02).** 저쪽이 굳은 뒤 툴 쪽 계획을
다시 점검하며 정한다(§G-10). 지금 적어 둘 것은 저쪽이 이미 확정한 셋뿐이다: 칸 이름
`SceneId` · 장면 진입점 하나 · 재진입 허용(금지하면 장면을 넘는 순환이 전부 막혀 표현력이
후퇴한다 — 저쪽 G1 §5).

---

## 참조 위치

| 무엇 | 어디 |
|---|---|
| 진행 패키지 인수인계 | `ked-progression/docs/handoff.md` |
| 진행 모델 스케치 + 미결 | `ked-progression/docs/model-draft.md` |
| 연출 런타임 사용법 | `ked-presentation-runtime/README.md` |
| **Scene 재편 정본 + 관문 계획서** | `ked-presentation-runtime/docs/scene-boundary-plan.md` · `docs/plans/G0~G5.md` (09-02 기준 저쪽 미추적) |
| 저쪽 저장소 경계 · 어셈블리 경계 | `ked-presentation-runtime/SCOPE-BOUNDARY.md` · `Assets/Scripts/Ked.Presentation.Runtime/ASSEMBLY-BOUNDARY.md` |
| 진행 코어 복사 반입 절차 · 대조 명령 | `ked-presentation-runtime/Assets/Scripts/Ked.Progression/Documentation~/vendoring.md` |
| 내보내기 (여기에 `Stats` 추가) | `src/Vn.Authoring/Chapters/ChapterProgressionExporter.cs` |
| 챕터 모델 (`ChapterStat`) | `src/Vn.Authoring/Chapters/ChapterGraphModel.cs` |
| 도달성 증명 (이관 원본) | `src/Vn.Authoring/Chapters/ChapterReachabilityProver.cs` |
| 저작 쪽 최신 규격 | [`handoff/current-state.md`](handoff/current-state.md) |
