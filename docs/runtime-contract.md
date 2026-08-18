# VnTool ↔ 런타임 계약서

**표면이 둘로 갈렸다** (2026-08-17):

| | 상대 | VnTool이 내는 것 |
|---|---|---|
| **1부** | `ked-presentation-runtime` (유니티) | `.yarn` 텍스트 + 연출 커맨드 |
| **2부** | `ked-progression` (순수 C# 패키지) | `exported/{챕터}.progression.json` |

이 문서와 어긋나는 출력은 컴파일이 되어도 **조용히 깨진다** — 대부분 즉시 오류가 아니라
어긋난 재생으로 나타나므로 이미터 검증 단계에서 잡아야 한다.

> **개정 이력**
> - 2026-08-03 최초 — 런타임 소스 전수 분석
> - 2026-08-17 §G 신설 — 진행 JSON 수입기 계약
> - **2026-08-18 전면 개정** — 런타임이 크게 단순해졌고, 진행 계층이 **별도 패키지로
>   빠져나갔다.** §0이 무엇이 사라졌는지 먼저 적는다. 근거는 두 저장소 실측이다.

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
title: Story_golden_ep
---
<<set $__t1_sf_test_favor = 0>>
<<camera wide>>                  ← 노드 셋업

<<camera closeup>>               ← 이 줄의 연출
라루: 첫 줄 그대로 #line:ln_004

<<if $__t1_sf_test_favor >= 5>>
    <<character_acting smile>>
    윌로: 갈래 안 대사 #line:ln_005
    <<jump Story_A로_간다>>
<<endif>>
===
```

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
> 최상위 불변식이다(architecture-decisions A-2). 런타임이 요구하지 않을 뿐이고,
> 세이브가 돌아오면 이 항목이 제일 먼저 되살아난다(§H-6).

**C2. 노드 타이틀은 여전히 진입 키다.** `EpisodePlayer`가 받는 것이 이 이름이고,
2부의 `EpisodeNode.DialogueEntryId`가 이 값과 문자열로 만난다. **한 번 출시된 노드
타이틀은 동결.** 코드가 강제하는 명명 규칙은 없다 — `Story_`/`Set_` 접두는 저작 관례다.

**C3. ~~선택지 리플레이는 위치 기반~~** — `ChoiceReplay`가 사라져 근거가 없다.
"출시 후 옵션을 기존 항목 위에 삽입하지 말 것"이라는 제약도 함께 풀렸다.

## D. 선택지

**D1. Yarn 변수는 살아 있다 — 없어진 것은 게임 상태로 잇는 다리다.**
`<<declare>>` · `<<set>>` · `<<if>>`는 그대로 컴파일되고 동작한다(씬에 `variableStorage`가
배선돼 있다). 사라진 것은 **런타임 코드가 그 저장소를 잡는 자리**(`VariableStorage` 참조
0건)와 **세션을 넘어선 지속**(세이브 없음)이다.

→ **작가 계층(B) 변수는 그대로 내도 된다** — 에피소드 안에서만 살고 밖으로 새지 않는다.
→ **A계층 스탯을 Yarn 변수로 내면 안 된다.** 그 값의 주인은 2부이고, Yarn에 적으면
세션과 함께 사라진다. (원래 규칙과 같다 — "대본에서 스탯 조작 = 설계 미스".)
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

**대상이 바뀌었다.** 옛 §G는 유니티의 `ChapterEpisodeProgressionSO`를 겨눴는데 그 타입은
런타임에서 사라졌다. 지금 수입 대상은 **`ked-progression` 패키지의 순수 C# 타입**이다.

저장소: `C:\Users\river\Documents\GitHub\ked-progression` · 모델 테스트 52 통과(2026-08-17).
**로더(`ProgressionLoader`)와 DTO는 아직 없다** — 그것이 Gate D의 현재 위치다.

## F. 타입 대응 — JSON ↔ 모델

`ChapterProgressionExporter`가 내는 것과 패키지 타입의 대응. **키는 PascalCase다.**

```
{ ChapterId, DisplayName, StartEpisodeId, Stats[], Nodes[], EndingRules[] }

Stat   : { Key, DisplayName, Type("Number"|"Bool"), Initial, Minimum, Maximum }

Node   : { EpisodeId, Title, IndexText, Kind("Main"|"Attachment"), DialogueEntryId,
           VisibleConditions[], UnlockConditions[], NextOptions[], Attachments[],
           IsChapterEndingCandidate, EndingKey, DesignerNote, Position{X,Y} }

Option : { TargetEpisodeId, ChoiceLabel, VisibleConditions[], Conditions[],
           HideWhenLocked, LockedReasonText, StatChanges[{Key, Amount}] }

조건   : { Kind("Stat"|"EpisodeCleared"), Key, Op, IntValue }
```

| JSON | 모델 | 비고 |
|---|---|---|
| `ChapterId` · `DisplayName` · `StartEpisodeId` | `ChapterProgression` | 시작은 실재 검사됨 |
| **`Stats[]`** | `ChapterProgression.Stats` | ✅ 2026-08-18부터 나간다. ⚠ `Type`은 **`"Number"`**다(이쪽 `Int`를 번역) |
| `Nodes[]` | `EpisodeNode` | |
| `NextOptions[]` | `EpisodeOption` | **배열 순서 = 화면 순서.** 정렬 금지 |
| `EndingRules` | **없음** | 모양이 없어 모델에 넣지 않았다. 언제나 빈 배열 |
| `Node.VisibleConditions`·`UnlockConditions` | **없음** | v8에서 간선으로 내려갔다. 언제나 빈 배열 |
| `Node.IndexText` | **없음** | v5 폐지. 언제나 빈 문자열 |
| `Node.Position` | **없음** | 저작 레이아웃이다. 평가 입력이 아니다 |
| `Node.Attachments` | **없음** | v1 비범위. 언제나 빈 배열 |

**enum은 이름 문자열로 나간다** — 순서를 재배열해도 안 깨진다. 로더가 이름으로 맵핑하고,
**알 수 없는 이름은 로더만의 검사 대상이다**(모델은 이미 enum이라 못 잡는다).

**F1. `IntValue`는 0이면 키 자체가 없다.** `JsonIgnoreCondition.WhenWritingDefault` —
`trust >= 0`은 `{ "Kind":"Stat", "Key":"trust", "Op":"GreaterOrEqual" }`로 나간다.
**DTO 필드는 반드시 `int`** — `int?`로 두면 가장 흔한 조건인 `flag == false`가 통째로
어긋난다.

**F2. 비교 연산 6종.** `GreaterOrEqual` · `LessOrEqual` · `Equal` · `Exists` ·
`GreaterThan` · `LessThan`(2026-08-16 개방). **`NotEqual`은 저작 파서가 닫아 두어 나오지
않는다 — 넣지 말 것.**

**F3. bool 스탯은 0/1 + `Equal`뿐이다.** 경계 0·1 고정, **크기 비교와 증감은 오류**.
양쪽이 같은 자리에서 막는다(`ChapterWorkbookReader.VerifyBoolStatUsage` ↔
`ChapterProgression`). `== false`는 F1 때문에 `IntValue` 키가 없는 모양으로 나간다.

**F4. 관문은 간선의 것이다** (v8). `Option.VisibleConditions` 미달 → **목록에 만들지
않는다**(플레이어는 그런 선택지가 있었다는 것조차 모른다). `Option.Conditions` 미달 →
**잠긴 채 보인다**(`LockedReasonText` 표시), 단 `HideWhenLocked`면 숨긴다.

**F5. 전이 규칙 3갈래** (v9). ① 고른 선택지 → `StatChanges` **원자적 1회 커밋**(clamp) 후
`TargetEpisodeId`로 이동 ② 고를 수 있는 것이 없으면 **`ChoiceLabel`이 빈 Option**(보이지
않는 기본 — 에피소드당 하나, **관문 금지**) ③ 그것도 없으면 **챕터 런 종료**.
**조건 판정은 커밋 전 값으로** — 플레이어가 선택지를 보는 시점의 값이다.

**F6. 생성자를 통과했다는 것의 뜻.** 에피소드 ID·스탯 키 중복 없음 · 시작 실재 ·
**모든 간선이 실재하는 에피소드에 착지** · 조건과 스탯변화가 **정의된 스탯만** 가리킴 ·
bool 어휘 준수. 그래서 전이기와 도달성 증명은 "허공으로 가는 간선"이나 "없는 키를 읽는
조건"을 다시 걱정하지 않는다.

**F7. 침묵 금지가 규율이다.** 없는 스탯 키를 0으로 읽지 않고 던진다(구 런타임은 조용히
`false`였다). 초기값이 경계 밖이면 생성 거부 — 조용히 clamp하면 작가가 쓴 값과 다르게
시작한다. `EpisodeCleared`에 `Exists` 아닌 연산이 오면 예외.

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

**G-2. 서브 레인 출력 경로 제거.** Pres 사본을 만드는 코드가 남아 있으면 걷는다(§A1).

**G-3. 커맨드 카탈로그 재검증 — ✅ 2026-08-18 완료.** 죽은 22개를 걷어 **179항목**으로 맞췄다(등록 178 + 동적 별칭 `<N>fr`). `docs/game.definition.json`은 문서가 아니라 **내장 리소스로 링크된 실물 팔레트**다 — 낡은 항목은 곧 작가가 고를 수 있는 unknown command다.

**G-4. `Ked.Presentation.Core` — 패키지화를 기다린다 (소유자 방침, 2026-08-18).** 소스째
복사하는 지금 방식은 이미 벌어져 있지만(아래), **최종적으로 패키지로 가져오기로 했으므로
재복사하지 않는다.** 그때까지 무대 프리뷰는 런타임과 다른 그림을 그릴 수 있다. 우리 `src/Ked.Presentation.Core`와
런타임 `Assets/Ked.Presentation.Core`를 비교하면 여러 파일이 다르고, **런타임에만 있는
파일이 7개**다(`StageReducer.{Placement,Portrait,Shot,Show,Slot,Staging}.cs` ·
`PortraitSizingReduction.cs`). architecture-decisions **H-4**의 "소스째 복사해 온 한 벌"이
낡았다 — 다시 들여와야 무대 프리뷰가 런타임과 같은 그림을 그린다.

## H. 소유자가 정해야 하는 것

**H-1. `Stats` 배달 경로** — (가) `progression.json` 최상위에 `Stats[]`, 로더는
`Load(dto)` / (나) 로더에 별도 인자, `Load(chapter, stats)`.
**양쪽 문서 모두 (가)를 권한다** — 값의 주인이 챕터 워크북이므로 챕터 JSON에 실려 나가는
것이 자연스럽다.

**H-2. 툴에 없는 세 개념을 살릴 것인가** — `Flag`(bool 전용 조건 종류. v9의 `Stat` 0/1 +
`Equal`이 더 단순해 **버려도 될 듯**) · `ChapterCleared`(챕터 간 진행이 생기면 필요 —
**언제 넣을지를 적어 둘 것**) · `Token`(아이템/열쇠 보유 — 지금 쓰는지 확인 필요).

**H-3. `Option.ViaNodeId`** — v9에서 작가는 **선택지 문구를 열쇠로** 자유 씬을 매다는데
(`DialogueNode.ChoiceExits`), 발행 경로의 점프는 대본의 **줄**에 매여 있어 실을 자리가
없다. **저작은 되는데 내보내기에는 안 나간다.** 옛 계약서가 "스탯 경계와 함께 정하라"고
했고 그 경계가 G-1로 풀렸으므로 **지금이 그 자리다.** 모델에 칸 하나 더하는 비용은 0이고,
실제 작업은 저작 쪽 발행 경로다.

**H-4. `EndingRules`의 모양** — 이름만 있고 모양이 없다. 데이터에 없는 것을 타입으로 먼저
만들면 영원히 안 타는 분기가 생기므로 모델에 넣지 않았다(`NotEqual`을 뺀 것과 같은 판단).
엔딩을 실제로 쓸 때 한 행이 무엇인지, `EndingKey`와 어떻게 맞물리는지 정해야 한다.
그전까지는 노드의 `IsChapterEndingCandidate`·`EndingKey`가 전부다.

**H-5. 부착(Attachment) 표시 조건** — v8에서 관문이 간선으로 내려가며 부착의 표시 조건이
갈 곳을 잃었다(들어오는 간선이 없다). v1 비범위. 쓸 때 `EpisodeNode`의 표시 조건을
**부착에 한해** 되살리고, "Main은 언제나 빈 배열"이라는 비대칭을 로더가 검사하게 하는 것이
자연스럽다.

**H-6. 세이브가 돌아올 것인가** — 지금은 없다. 돌아오면 옛 §C1(`#line:` 태그 필수)과
§C3(선택지 리플레이가 위치 기반이라 옵션 순서 동결)이 **그대로 되살아난다.**
2026-08-18 이전 판(git 이력)에 그때의 규칙이 근거 코드 경로와 함께 남아 있다.

---

## 참조 위치

| 무엇 | 어디 |
|---|---|
| 진행 패키지 인수인계 | `ked-progression/docs/handoff.md` |
| 진행 모델 스케치 + 미결 | `ked-progression/docs/model-draft.md` |
| 연출 런타임 사용법 | `ked-presentation-runtime/README.md` |
| 내보내기 (여기에 `Stats` 추가) | `src/Vn.Authoring/Chapters/ChapterProgressionExporter.cs` |
| 챕터 모델 (`ChapterStat`) | `src/Vn.Authoring/Chapters/ChapterGraphModel.cs` |
| 도달성 증명 (이관 원본) | `src/Vn.Authoring/Chapters/ChapterReachabilityProver.cs` |
| 저작 쪽 최신 규격 | [`handoff/current-state.md`](handoff/current-state.md) |
