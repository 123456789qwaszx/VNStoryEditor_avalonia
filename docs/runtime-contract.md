# VnTool ↔ ked-presentation-runtime 계약서

VnTool이 내보내는 .yarn 텍스트가 지켜야 하는 런타임 규약. 2026-08-03, 런타임 소스(@Scripts, 504파일) 전수 분석으로 확정. 각 규칙에 근거 코드 경로를 병기한다. **이 문서와 어긋나는 출력은 컴파일이 되어도 런타임에서 조용히 깨진다** — 대부분 즉시 오류가 아니라 어긋난 재생·세이브 파손·행(hang)으로 나타나기 때문에 이미터 검증 단계에서 잡아야 한다.

---

## A. 노드 구조와 서브 레인 수명

**A1. 트리오 구조.** Story 노드(메인 레인) + Set 노드(원샷 레인, `<<beat Set_X>>`) + Pres 노드(서브 레인, `<<pres_start Pres_X>>`).

**A2. Set 노드는 커맨드 전용이다.** 원샷 레인은 대사 라인을 경고와 함께 건너뛴다. (`OneShotPresentationPresenter.cs:31`)

**A3. `pres_start`의 락스텝 앵커.** `pres_start`는 `Hold(1)`을 걸어 그 커맨드를 실은 메인 라인이 advance 0을 소비하게 한다 — 그래서 Pres 1행이 다음 메인 라인과 짝지어진다. (`VNSideRunnerSyncHub.cs:55`)

**A4. Pres 자연 소진은 안전하다.** 서브 레인이 라인을 다 쓰면 `CompleteRun()`으로 닫히고 이후 메인 라인은 빈 플랜으로 즉시 통과한다. `pres_end` 없이 끝나도 된다. (`SubPresentationPresenter.cs:37`, `SyncGateAdvancer.cs:66`)

**A5. ⚠ jump는 서브 레인을 정리하지 않는다.** `<<jump>>`로 노드를 옮겨도 서브 레인은 살아서 다음 노드의 라인마다 advance를 1씩 계속 소비하며 조용히 어긋난다. 노드 변경 시 훅 없음 확인. (`VNSideRunnerSyncHub` — `ResetPresentationLane` 호출처는 `EpisodePlayer.StopYarnRunnersAsync`와 `VNLoadSeekDriver.BeginSeek` 뿐)
→ **규칙: 활성 `pres_start`가 있는 Story 노드에서 나가는 모든 `<<jump>>` 직전에 `<<pres_end>>`를 출력한다. 레인이 필요한 Story 노드는 각자 자기 `<<pres_start>>`로 연다.**

**A6. ⚠ 재-`pres_start`는 무방비다.** 실행 중인 서브 러너에 `StartDialogue`를 그대로 호출한다(원샷 레인과 달리 선행 Stop 없음). 반드시 `pres_end` 선행. 또 `pres_hold`는 덮어쓰기라 보류 중인 hold가 `pres_start`의 Hold(1)로 리셋된다. (`VNSideRunnerSyncHub.cs:55` vs `OneShotPresentationLane.cs:26`)

**A7. `<<pres_end>>`는 블로킹·멱등이다.** IEnumerator 핸들러라 메인이 종료 완료까지 대기하고, 남은 Pres 라인은 조용히 버려진다. (`VNSideRunnerSyncHub.cs:62`)

---

## B. 라인 동기화 예산 — Pres 사본의 라인 수 계산 규칙

메인 레인에서 advance를 소비하는 단위와 개수:

| 메인 레인 요소 | 서브 advance | 근거 |
|---|---|---|
| 일반 대사 라인 1개 | 1 | `SyncGatePlanBuilder.ConsumeForwardPlan` |
| `#beat` 메타 라인 1개 | 1 (박스·타자기 없음) | `VNLinePresentationFlow.Beat.cs` |
| 선택지(`->`) 블록 전체 | **0** | `VNOptionsPresenter` — SyncHub 참조 없음 (grep 확인) |
| 본문 내 `[adv/]` 마커 1개 | +1 (타자기 도중 디스패치) | `InlineAdvanceManifest.DefaultMarkerName = "adv"` |
| `<<pres_hold N>>` | 다음 N개 메인 라인이 0 소비. **재호출 시 덮어쓰기** | `SyncGatePlanBuilder.Hold` |
| `<<pres_advance N>>` | 다음 메인 라인에 +N. **누적** | `SyncGatePlanBuilder.AddExtraAdvance` |

→ **이미터 규칙: Pres 사본의 라인 시퀀스는 Story의 (대사 라인 + [adv/] 마커 수)와 정확히 같은 개수·순서여야 한다. 선택지 블록에는 Pres 대응 라인을 만들지 않는다.** 같은 DialogueResult에서 생성하므로 구조적으로 보장 가능하다. `[adv/]`는 Phase 0에서는 미지원 — 본문에 있으면 발행 검증에서 알린다.

**B1. Pres 라인 메타 `#main_free`.** 기본은 해당 Pres 라인의 wait 커맨드 배치가 닫힐 때까지 메인이 블록. `#main_free`를 붙이면 즉시 해제(비차단 스테이징). 툴에서 연출 라인별 토글로 노출할 가치가 있다. (`SubPresentationPresenter.cs:161`)

**B2. 행 위험.** Pres 라인의 커맨드 배치가 닫히지 않으면 메인 레인이 무한 대기한다(타임아웃 없음). 이미터가 만드는 정상 배치에서는 발생하지 않지만, 수동 편집 유입 시 주의. (`RunSyncGatePlanAsync`)

---

## C. 식별자와 세이브 호환 — 가장 무거운 계약

**C1. Story 대사 라인에 `#line:<LineId>` 필수.** 세이브는 `{nodeName, lineId}`를 Ordinal 문자열 비교로 시킹한다. 태그가 없으면 Yarn implicit ID가 익스포트마다 바뀌고, 로드 시 타깃 라인을 영원히 못 찾아 **노드 끝까지 대사창 없이 빨리감기 + 입력 전면 거부(silent hang)** 상태가 된다. 실패 콜백은 존재하지만 아무도 호출하지 않는다. (`VNSeekState.cs:27`, `VNSaveData.cs`, `VnAdvanceGate.cs:49`, `VNLoadSeekDriver` — `Fail()` 무호출)

**C2. 노드 타이틀은 세이브 키이자 에피소드 진입 키다.** `nodeName`이 세이브에 저장되고 `EpisodeNodeDefinition.DialogueEntryId`(SO)와 문자열 일치로 연결된다. **한 번 출시된 노드 타이틀은 동결.** 코드가 강제하는 명명 규칙은 없다 — `Story_/Set_/Pres_` 접두는 순수 저작 관례. (`EpisodeSelectionSystem.cs:42`)

**C3. 선택지 리플레이는 위치 기반이다.** 세이브된 선택 기록은 `{시킹 기점부터의 블록 서수, 옵션 인덱스}`다. 옵션의 LineId는 생성자에서 받고 **버려진다**. → **재익스포트 시 선택지 블록의 등장 순서와 블록 내 옵션 순서가 바뀌면 기존 세이브가 다른 선택지를 리플레이한다.** 출시 후에는 옵션을 기존 항목 위에 삽입하지 말 것. (`VNChoiceRecord.cs:12-23`, `VNChoiceBoundary.cs:18`)

**C4. Pres 사본 라인과 `#beat` 라인은 태그 불요.** 롤백 포인트에 기록되지 않아 세이브 타깃이 될 수 없다. 단 Yarn의 전역 라인 ID 유일성 때문에 Story와 같은 `#line:` 태그를 중복 출력하면 컴파일 오류 — Pres 사본은 무태그로 둔다. (`VNLinePresentationFlow.Beat.cs:17` recordToHistory:false)

**C5. 세이브에 변수 스냅샷이 없다.** 플래그 저장소는 현재 Empty 스텁. 로드는 노드 처음부터 시킹 리플레이하며 그 과정에서 `<<set>>`이 재실행되어 변수가 재구축된다. → **이미터가 출력하는 set은 결정적이어야 하며(랜덤·외부 부작용 금지), 같은 경로 리플레이 시 같은 결과가 나와야 한다.** 시킹 중에도 커맨드는 압축 실행된다(스테이징 상태 보존). (`VnAppBootstrap.cs:482`, `CommandRunScope.cs:53`)

---

## D. 변수·조건·선택지

**D1. 변수 저장소 공유는 씬 배선이다.** 코드에는 세 러너의 `VariableStorage` 배선이 없다 — 씬에서 같은 `VariableStorageBehaviour`를 세 러너에 지정해야 공유된다. 소유자 확인: 공유 맞음. **씬 체크리스트에 포함 권고** (코드가 보증하지 않으므로).

**D2. `<<set>>`은 Story에만 출력한다.** 저장소 공유이므로 Pres에 복제하면 이중 실행된다 (`+= 10`이 +20).

**D3. 조건 구조(`<<if/elseif/endif>>`)는 Pres에 그대로 복제한다.** 읽기 전용이고, 서브 레인은 메인이 해당 지점을 지난 뒤에만 평가하므로 같은 분기를 탄다. 분기 내 라인 수가 양쪽에서 같아야 락스텝이 유지된다(B 규칙과 함께 구조적으로 보장).

**D4. 변수 선언이 필요하다.** 런타임 C#에는 `<<declare>>`도 스마트 변수도 없다. 컴파일을 위해 이미터가 선언을 출력해야 한다(스탯은 숫자로 — 런타임이 float으로 읽는다). 선언 블록의 위치(전용 노드 vs 각 Story 노드 상단)는 Phase 0에서 결정. (`VNOptionsPresenter.Accumulate.cs:76`)

**D5. 옵션 해시태그는 표시 전용이다.** `-> 텍스트 #fatigue:+10`의 태그는 미리보기 라벨만 만든다 — **실제 효과는 옵션 본문의 `<<set>>`이 담당**하며 이미터가 둘 다 생성해야 한다. 파서 규칙: 키는 소문자 정규화(이미터는 소문자 키만 사용), 정수만(`+1.5`는 조용히 버려짐), `~`로 범위 표기 가능. 표시 이름은 `fatigue/rare_ingredient/common_ingredient/risk/trust/anger`만 하드코딩 — 그 외는 키 원문 노출. (`VNOptionEffectPreviewParser.cs:7`, `VNOptionEffectDisplayNameResolver.cs`)

**D6. ⚠ 옵션 라벨은 원문 그대로 렌더된다.** 접두("s1" 등)를 벗기는 코드가 없다 — options.yarn 샘플의 `s1안전한 길을 따라간다`는 버튼에 "s1"까지 표시된다. 이미터는 라벨에 식별 접두를 넣지 말 것 (또는 런타임에 스트리핑 추가 — 소유자 결정). (`VNOptionsPresentationFlow.cs:167`)

**D7. 비활성 옵션은 사라진다.** `<<if>>` 조건이 거짓인 옵션은 회색 처리가 아니라 미표시. C3의 위치 기반 리플레이는 **가용 옵션 배열 기준 인덱스**이므로, 조건부 옵션이 있는 블록은 리플레이 시점의 변수 상태가 같아야 같은 인덱스가 나온다 — C5의 결정성 규칙이 이를 보장한다.

---

## E. 커맨드 어휘

**E1. 명명 커맨드 200개 + `1fr`~`48fr` 동적 별칭 48개.** 카탈로그(`game.definition.draft.json`, 201항목)는 코드의 `AddCommandHandler` 등록과 **이름·인자 개수 0건 불일치**로 교차 검증 완료 (2026-08-03).

**E2. 메인 레인 전용 11개는 Pres/Set 노드에 출력 금지.** `pres_start/end/pause/resume/hold/advance, beat, beat_fx, box_named, box_protagonist, box_reset` — 서브 러너들엔 미등록이라 unknown command 오류. (`VnAppBootstrap.cs:360,373,386` bindMainLaneCommands)

**E3. 커맨드 기본값은 4개 층에 있다.** (a) 스펙 클래스 필드 초기값 (b) YarnCommandBridge 파싱 기본 인자 — **가장 큰 덩어리, 툴이 명시 인자를 출력하면 완전 우회 가능** (c) 토큰 파서 폴백 (d) DBSO 프리셋(키 참조: `tx_*` 모션, screen 이펙트, char_visual 등) — **키가 곧 어휘**이므로 카탈로그의 presetKey 후보로 관리. hop의 height=22처럼 Yarn 인자로 아예 노출되지 않는 하드코딩 값들(b′층)은 툴의 "커맨드 프리셋" 기능이 이주지가 된다.

**E4. 매크로 커맨드.** `show`→4스펙, `cast`→4, `emoji`→6, `slot_tyrant`→~8 등 1:N 확장이 존재. 툴은 이를 신경 쓸 필요 없다(텍스트 커맨드 하나로 출력) — 단 미래의 스펙 직출력(Phase 2)에서는 이 확장 표가 필요하다.

**E5. 라인 메타태그 어휘** (`DialogueBoxMetadataResolver` — 소문자 정규화, 첫 일치 우선):
- 비트: `#beat` `#stage` `#stage_only` `#present` / 대기 수정자: `#stay` `#beat_stay` `#no_auto`
- 박스 종류: `#surface` `#portrait` `#speaker` `#letterbox` `#onlytext` `#blackbook` (+ `#box:종류` `#box=종류` 변형)
- 박스 전환: `#boxkeep` `#boxcut` `#boxfade` `#boxfadein` `#boxhide` (+ `#box_transition=` 변형)
- Pres 라인 전용: `#main_free`
- 비트 라인은 롤백에 기록되지 않는다 (주석은 기록된다고 하나 코드가 우선 — `recordToHistory:false`)

---

## F. 소유자 확인 항목

1. **씬 배선**: 세 DialogueRunner(`dialogueRunner`/`subPresentationRunner`/`subOneShotRunner`)가 같은 `VariableStorageBehaviour`를 참조하는지 씬에서 확인 (D1).
2. **옵션 라벨 접두**: options.yarn의 `s1`/`s2` 접두가 화면에 그대로 노출됨 — 의도인지, 런타임 스트리핑을 넣을지, 이미터가 접두 없이 출력할지 (D6).
3. **YarnVariableBridge 미배선**: Yarn 변수 ↔ 에피소드 선택 상태(Stats/Flags) 브리지가 코드에 있으나 아무도 생성하지 않음 — 현재 Yarn에서 벌어진 일이 에피소드 언락에 반영되지 않는다. 인지하고 있는지.
4. **플래그 저장소 Empty 스텁**: 세이브에 변수가 안 들어가는 현 구조 유지 여부 (C5의 리플레이-결정성 요구가 여기서 나온다).
5. **CanvasScaler 기준 해상도**: 프리뷰(Phase 2)용 — 좌표가 전부 RigSpaceRoot 픽셀 기준이라 기준 해상도가 데이터로 필요.
6. **스탯 경계를 progression JSON에 실을지** (G7): 초기값·최소·최대가 지금 어느 런타임 입력에도 없다. 툴의 도달성 증명은 clamp하며 걷는데 런타임은 경계를 모른다 — 증명과 실제 플레이가 갈린다. 권고는 최상위 `Stats[]` 추가.
7. **선택지별 자유 씬 점프의 모양** (G8): `Option`에 `ViaNodeId` 한 칸을 두고 런타임이 그 Yarn 노드를 거쳐 `TargetEpisodeId`로 잇는 안. 6번과 함께 정하면 저작 쪽을 한 번에 고친다.
8. **`Option.VisibleConditions` 수용** (G5): v8부터 저작이 내보내고 있으나 런타임 `EpisodeNextOption`에 자리가 없다 — Gate D의 가장 큰 한 칸.

---

## G. 챕터 진행 JSON — 수입기 계약 (v9, 2026-08-17)

A~F는 `.yarn` 텍스트의 계약이다. 이 절은 **다른 표면**을 다룬다: 챕터 레이어가 내는
`exported/{챕터}.progression.json`을 런타임 `ChapterEpisodeProgressionSO`로 수입하는 규약.
**Gate D가 여기에 걸려 있다** — 수입기가 아직 없고, 아래 필드 중 일부는 저작 쪽이 이미 내고
있는데 런타임 타입에 자리가 없다. 이 절은 툴이 **오늘 실제로 내보내는 것**을 코드에서 그대로
옮긴 것이다(`ChapterProgressionExporter`).

### G1. 모양

**키는 PascalCase다** (camelCase 정책 없음 — 아래는 실제 출력을 옮긴 것이다).

```
{ ChapterId, DisplayName, StartEpisodeId, Nodes[], EndingRules[] }

Node   : { EpisodeId, Title, IndexText, Kind("Main"|"Attachment"), DialogueEntryId,
           VisibleConditions[], UnlockConditions[], NextOptions[], Attachments[],
           IsChapterEndingCandidate, EndingKey, DesignerNote, Position{X,Y} }

Option : { TargetEpisodeId, ChoiceLabel, VisibleConditions[], Conditions[],
           HideWhenLocked, LockedReasonText, StatChanges[{Key, Amount}] }

조건   : { Kind("Stat"|"EpisodeCleared"), Key, Op, IntValue }
```

**enum은 이름 문자열로 나간다** — 순서를 재배열해도 안 깨진다. 수입기는 이름으로 맵핑한다.
`IndexText`는 언제나 빈 문자열이다(v5에서 에피소드 `인덱스` 열 폐지).

### G2. ⚠ `IntValue`는 0이면 키 자체가 없다

`JsonIgnoreCondition.WhenWritingDefault` — `trust >= 0`은 이렇게 나간다:

```json
{ "Kind": "Stat", "Key": "trust", "Op": "GreaterOrEqual" }
```

**수입기는 없는 키를 0으로 읽어야 한다.** G4의 `flag == false`도 같은 모양이라, 기본값을
0으로 안 잡으면 **bool 조건이 통째로 어긋난다**(가장 흔한 조건인데도).

### G3. 새 `op` 둘 — `GreaterThan` · `LessThan`

2026-08-16 소유자 개방으로 조건 시트에 `<`·`>`가 열렸다. 기존 넷(`GreaterOrEqual`,
`LessOrEqual`, `Equal`, `Exists`)에 **`GreaterThan`·`LessThan`이 더해진다.** 수입기의
enum과 평가기 둘 다 열어야 한다. `NotEqual`은 파서가 아직 닫아 두어 나오지 않는다.

### G4. bool 스탯은 0/1 + `Equal`이다

스탯 시트의 `타입`이 `bool`이면 값 공간이 0·1이고 조건은 `flag == true/false`뿐이다.
내보내기는 이것을 **`Stat` + `Equal` + `IntValue` 0 또는 1**로 낸다 — 런타임에 bool이라는
별도 종류가 필요 없다(G2 때문에 `== false`는 `IntValue` 키가 없는 모양으로 나간다).

### G5. ⚠ 관문이 노드에서 길로 내려왔다 (v8)

`Node.VisibleConditions` · `Node.UnlockConditions`는 **언제나 빈 배열로 나간다.** 스키마
1:1을 위해 필드만 남겨 둔 것이고, 실제 값은 **`Option.VisibleConditions` / `Option.Conditions`**에
있다. `Option.VisibleConditions`는 런타임 `EpisodeNextOption`에 **아직 없는 필드**다 — 이것이
Gate D의 가장 큰 한 칸이다.

- `Option.VisibleConditions` 미달 → 그 선택지를 **목록에 만들지 않는다**
- `Option.Conditions` 미달 → **잠긴 채 보인다**(`LockedReasonText` 표시).
  단 `HideWhenLocked`가 참이면 숨긴다

### G6. 전이 규칙 (v9)

에피소드 재생이 끝나면:

1. 플레이어가 고른 선택지의 Option을 탄다 → `StatChanges`를 **원자적으로 1회 커밋**한 뒤
   `TargetEpisodeId`로 이동
2. 고를 수 있는 선택지가 하나도 없으면 **`ChoiceLabel`이 빈 문자열인 Option**(보이지 않는
   기본, 에피소드당 하나·조건 없음)을 탄다
3. 그것도 없으면 **챕터 런이 여기서 끝난다**

**조건 판정은 커밋 <b>전</b> 값으로 한다** — 플레이어가 선택지를 보는 시점의 값이다.
`NextOptions`의 배열 순서가 곧 **화면에 뜨는 순서**다(`간선` 시트의 행 순서).

### G7. ⚠ 스탯의 초기값·최소·최대가 이 파일에 없다

`스탯` 시트의 `초기값`·`최소`·`최대`는 **어느 런타임 입력에도 실려 있지 않다** —
progression JSON에도, `game.definition.json`에도 없다(둘 다 확인). 그런데 툴의 도달성
증명은 `Math.Clamp(값, 최소, 최대)`로 걷는다(`ChapterReachabilityProver`).
**런타임이 clamp하지 않거나 다른 경계로 clamp하면 증명과 실제 플레이가 갈린다.**

→ 소유자 결정 필요(F6). 권고: progression JSON 최상위에 `Stats[{Key, DisplayName,
Initial, Minimum, Maximum, Type}]`를 더한다. 저작 쪽 한 줄이면 되고, 경계가 살 다른
자리가 없다.

### G8. ⚠ 선택지별 자유 씬 점프가 아직 안 실린다

v9에서 작가는 **선택지 문구를 열쇠로** 자유 씬을 매단다
(`DialogueNode.ChoiceExits`, 판 파일의 `choiceExits`). 그런데 발행 경로
(`DialoguePublisher`)의 점프는 대본의 **줄**에 매여 있어, 문구를 열쇠로 한 점프를 실을
자리가 없다 — **저작은 되는데 내보내기에는 안 나간다.** 시나리오 그래프의 칩 상세가 그
사실을 적어 두고 있다.

→ 필요한 것: Option 하나가 "다음 에피소드로 곧장" 대신 "이 Yarn 노드를 거쳐서" 갈 수 있어야
한다. 저작 쪽 후보는 `Option`에 `ViaNodeId` 한 칸. 런타임은 그 노드를
재생한 뒤 `TargetEpisodeId`로 잇는다. **G7과 함께 결정되어야 저작 쪽을 한 번에 고친다.**

### G9. 부착(Attachment) 에피소드의 표시 제어가 비었다

`Kind: "Attachment"`인 에피소드는 들어오는 간선이 없다 — v8에서 관문이 간선으로 내려가면서
**부착의 표시 조건이 갈 곳을 잃었다**(이행에서 사라진다). `Node.Attachments`도 v1 비범위로
빈 배열이다. 부착을 실제로 쓰기 전에 규격이 필요하다 — 그때는 노드 쪽 `VisibleConditions`를
부착에 한해 되살리는 것이 자연스럽다(길이 없으니 노드가 유일한 주인이다).
