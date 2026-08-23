# 대사엔트리가 실제 yarn 노드 이름과 갈린다 — 작업 지시

2026-08-23 · **호스트 쪽 실측 · 저작 쪽 수정 요청**

발신: `ked-presentation-runtime` (유니티 호스트)
수신: VnTool

> 한 줄. **툴이 내는 산출물 둘이 같은 노드를 다른 이름으로 부른다.**
> 진행 JSON은 `new01`이라 하고 `.yarn`은 `Story_new01`이라 한다. 접두를 붙이거나 벗기는
> 코드는 세 저장소 어디에도 없다.

---

## 0. 증거

유니티에서 **툴이 실제로 낸 파일**(`test10/exported/qwe.progression.json`)을 물리고
**툴이 실제로 쓴 yarn**(`Assets/@Dialogue/Story_*.yarn`)과 대조한 결과다. 손으로 만든
견본이 아니다.

```
[진행] 실었다 — qwe (에피소드 3, 스탯 1)

[진행] 사전 대조 실패 — 진행 JSON이 부르는 노드가 YarnProject에 없다. 재생을 시작하지 않는다.

  qwe/new01 — 대사 노드 "new01"이 없다.  → "Story_new01"은 있다.
  qwe/new02 — 대사 노드 "new02"이 없다.  → "Story_new02"은 있다.
  qwe/new03 — 대사 노드 "new03"이 없다.  → "Story_new03"은 있다.
```

로드는 통과한다 — **스키마는 맞다.** 갈린 것은 이름 하나뿐이고, 그래서 더 위험하다.

---

## 1. 왜 지금까지 아무도 못 봤나

산출물 둘이 **다른 폴더로 나가서** 나란히 놓일 기회가 없었다.

```
                    ┌─► Assets/@Dialogue/Story_new01.yarn      LiveOutputService
   qwe.xlsx ─ 툴 ───┤     title: Story_new01
                    └─► test10/exported/qwe.progression.json   ChapterGraphView.AutoExport
                          DialogueEntryId: "new01"
```

그리고 **아무도 둘 다 볼 수 없다**:

| | 아는 것 | 못 보는 것 |
|---|---|---|
| VnTool | 자기가 낸 `.yarn` 텍스트 | 유니티의 `YarnProject` |
| `ked-progression` | `DialogueEntryId`라는 **문자열** | yarn이 무엇인지 |
| **유니티 호스트** | **둘 다** | — |

그래서 호스트에 **사전 대조**를 세웠고(`ProgressionContentPreflight`), 그것이 이 건을 잡았다.
재생 시작 전에 부르는 노드를 전부 `YarnProject`와 맞춰 보고, 안 맞으면 시작하지 않는다 —
저작 쪽 규율(*"실을 수 없으면 내보내지 않는다"*)의 호스트판이다.

**이 검사는 앞으로도 호스트가 소유한다.** 그쪽에 부탁하지 않는다.

---

## 2. ⚠ 규칙은 접두 하나가 아니다 — **두 단계**다

이걸 놓치면 고쳐도 또 갈린다.

```
yarn 노드 이름 = "Story_" + SanitizeNodeName(이름)
                            └ 영숫자·밑줄이 아닌 글자를 전부 '_'로
```

| 자리 | |
|---|---|
| `Rendering/YarnBundleEmitter.cs:135` | `story.Append("title: Story_").Append(name)` |
| `Rendering/YarnBundleEmitter.cs:247` | `StoryPrefix = "Story_"` |
| `Rendering/YarnBundleEmitter.cs:258` | `BundleNameOf(nodeName, nodeId)` → `YarnSyntax.SanitizeNodeName` |
| `Rendering/YarnSyntax.cs:98` | `SanitizeNodeName` — 영숫자·밑줄 외 전부 `'_'` |

**증거는 이미 프로젝트 안에 있다:**

| 툴의 노드 이름 | 실제 yarn 파일 |
|---|---|
| `new01` | `Story_new01.yarn` |
| `장면 1` | **`Story_장면_1.yarn`** ← 공백이 밑줄로 |

→ 단순히 `"Story_" + DialogueEntry`로 고치면 **공백·점·하이픈이 든 이름에서 다시 갈린다.**

---

## 3. ⛔ 부탁하는 것 — 이름 짓는 규칙을 한 곳에 두고 내보내기가 그것을 부른다

### 3.1 이미터에 이름 함수를 하나 연다

```csharp
// Rendering/YarnBundleEmitter.cs

/// <summary>
/// 이 이미터가 그 이름의 노드에 붙일 Story 타이틀. 진행 내보내기의
/// DialogueEntryId·ViaNodeId 가 이것과 같은 글자여야 런타임이 노드를 찾는다.
/// </summary>
public static string StoryNodeTitleOf(string? nodeName)
    => StoryPrefix + BundleNameOf(nodeName, null);
```

`Story_` 조립이 지금 **세 자리**에 흩어져 있다 — `:135`(타이틀) · `:276`(파일 이름) ·
`:789`(`JumpTargetOf`). 셋을 이 함수로 모으면 규칙이 진짜로 한 곳에만 산다.
**내보내기는 그 셋 중 어디에도 안 끼어 있었다** — 그것이 이번 건의 뿌리다.

### 3.2 내보내기가 그것을 쓴다

```csharp
// Chapters/ChapterProgressionExporter.cs:168

  DialogueEntryId = episode.DialogueEntry,
↓
  DialogueEntryId = YarnBundleEmitter.StoryNodeTitleOf(episode.DialogueEntry),
```

### 3.3 붙들고 있는 테스트를 갱신한다

`DialogueEntryId`를 글자 그대로 붙드는 곳은
**`tests/Vn.Authoring.Tests/Chapters/ChapterExportAndFixtureTests.cs`**다.
`ProgressionSampleGoldenTests`도 직렬화 결과 전체를 보므로 같이 움직인다.

**의미가 맞으므로 갱신하되**, 갱신 뒤 값이 `Story_` + 정규화된 이름인지 눈으로 확인할 것.
특히 **공백이 든 이름을 하나 케이스로 넣어 달라** — 접두만 붙이는 실수를 그 테스트가 잡는다
(`장면 1` → `Story_장면_1`).

#### ⛔ 견본에 이미 `Story_`가 손으로 적혀 있다 — 중복 접두 가드를 넣지 말 것

이 저장소의 견본 챕터는 `대사엔트리` 칸에 접두를 **사람이 직접 적어 둔** 상태다.
§4 가운데 줄의 그 방식이 견본에 이미 들어와 있다.

`EpisodeSyncService.NodeNameFor`(`:263`)가 `대사엔트리`를 **노드 이름 그대로** 쓰므로
견본의 yarn 타이틀은 지금 `Story_Story_ch05_01`이다. **견본도 이미 갈려 있다 — `new01`과
반대 방향으로.** §0의 증거가 한쪽 방향만 보여 준 것은 그 챕터가 새로 만든 것이어서다.

#### ⚠ 범위 — 세 자리가 아니라 세 저장소다

원천은 테스트 코드가 아니라 **엑셀 견본**이고, 거기서 파생된 값이 저장소 경계를 넘어가 있다.

| 자리 | 무엇 | 규모 |
|---|---|---|
| `docs/chapter-graph-sample.xlsx` | **원천.** `대사엔트리` 칸에 `Story_ch05_01` … | 견본 워크북 |
| `tests/**` | 위에서 파생된 기대값 | **10파일 25곳** |
| `ked-progression` `Tests/Fixtures/chapter-sample-export.json` | 이 견본을 내보낸 결과 (`DialogueEntryId: "Story_ch05_01"`) | 견본을 고치면 **다시 내보내 갱신** |

```
Vn.App.Tests/          ChapterGraphSyncViewTests · ExcelNodeLockTests
Vn.Authoring.Tests/    ChapterExportAndFixture · ChapterFolderWatcher · ChapterSchemaV5
                       ChapterWorkbookCellType · ChapterWorkbookReader · ChapterWorkbookWriter
                       EdgeKindAndEnding · EpisodeSyncService
```

**이 숫자를 먼저 아는 것이 중요하다.** 세 곳만 고치고 테스트를 돌리면 나머지 22곳이 한꺼번에
터지고, 그 순간 *"가드를 하나 넣으면 다 통과하는데"*라는 유혹이 정확히 아래 ⛔ 지점에서 온다.
25곳은 **기계적 치환**이다 — 판단이 필요한 자리가 아니다.

**그래서 ①②를 그대로 적용하면 그 테스트가 `Story_Story_ch05_01`을 낸다. 그것이 옳다.**
양쪽이 같은 `대사엔트리`에서 같은 규칙을 통과하므로 사전 대조는 지나간다.

⛔ **이 값을 보고 "이미 `Story_`로 시작하면 붙이지 않는다"는 가드를 넣지 말 것.**
그 가드는 (ㄱ) 진짜로 `Story_`로 시작하는 이름을 영영 못 쓰게 만들고, (ㄴ) 규칙의 둘째
사본을 만들어 §3.1의 목적을 정확히 되돌린다. 고칠 것은 코드가 아니라 **견본 데이터**다 —
`docs/chapter-graph-sample.xlsx`의 접두를 지워 `ch05_01`로 되돌리면 견본이 `new01`과 같은
모양이 되고, 파생된 25곳과 코어 픽스처가 따라온다.

### 3.4 ⚠ `ViaNodeId`도 같은 자리다 — §5 먼저 읽을 것

`ChapterProgressionExporter`의 `ViaNodeId = edge.PresentationNodeName ?? ""`도 같은 문제를
갖는다. 다만 **연출 노드가 독립 yarn 노드로 나가는지가 불분명**하다 — 확인 전에는 손대지 말 것.

---

## 4. 왜 다른 방식이 아닌가

| 안 | 왜 안 되나 |
|---|---|
| **런타임이 접두를 붙여 재생한다** | `SanitizeNodeName`은 **저작 이미터의 규칙**이다. 런타임이 흉내 내면 규칙 사본이 셋째 저장소에 생기고, 이미터가 규칙을 바꾸는 날 조용히 갈린다. 같은 결말을 이미 한 번 봤다 — §6 |
| **기획자가 `대사엔트리`에 `Story_new01`을 직접 적는다** | 코드 변경은 0이지만 사람이 정규화 규칙까지 외워야 한다(`장면 1` → `Story_장면_1`). 그리고 `EpisodeSyncService`가 그 이름으로 대사노드를 찾으므로(`:263`·`:286`) 노드 이름 자체가 `Story_` 로 시작하게 되어 이미터가 `Story_Story_…`를 낸다 |
| **이미터가 접두를 떼고 낸다** | 접두의 존재 이유(파일 이름 구분·`JumpTargetOf`)가 사라진다. 붙이는 쪽이 옳고, 안 따라간 쪽이 틀렸다 |

---

## 5. 같이 봐 둘 것 — 지금 막지는 않는다

### 5.1 `ViaNodeId`는 아직 한 번도 실물로 통과한 적이 없다

툴의 실제 출력에서 `ViaNodeId`가 **언제나 빈 문자열**이다 — `연출` 열을 아무도 안 썼다.

그런데 이미터에는 `Set_`·`Pres_` 접두가 있고(`:249`·`:251`), 유니티의 `@Dialogue`에는
**`Story_` 파일만** 있다. 연출 노드(`new01 연출`)가 독립 yarn 노드로 나가는지 Story 안으로
접혀 들어가는지가 밖에서는 안 보인다.

**`연출` 열을 쓰기 시작하는 순간 같은 이름 문제가 다른 접두로 한 번 더 나온다.**
그때 §3.4를 함께 정할 것.

### 5.2 깃발 내보내기 거부를 이제 지워도 된다

`ChapterProgressionExporter.cs:57`·`:89`의 `BoolSetNotCarried`가 아직 깃발 쓰는 챕터의
내보내기를 통째로 막고 있다. 그 함수 주석이 *"실을 수 있게 되면 이 함수와 그 호출 한 줄만
지운다"*고 적어 뒀는데 — **그날이 왔다.**

`ked-progression` **0.2.0**(2026-08-23 태그)에 `StatChangeDto.Op`가 섰다:

```json
"StatChanges": [ { "Key": "met_willow", "Amount": 1, "Op": "Set" } ]
```

- 비었거나 `"Add"` = 더하기 (**기존 JSON은 한 글자도 안 바뀐다**)
- `"Set"` = 정하기
- 불변식 셋을 코어가 강제: `Set`은 bool에만 · 값은 0/1만 · 한 간선에서 같은 키를 두 번 정하면 오류

`StatChangeJson`에 `Op` 칸을 더하고 `delta.IsSet`을 그대로 옮기면 된다. 유니티에서 이미
검증됐다 — 관문이 잠기고, 스탯을 올리면 열린다.

### 5.3 간선 `종류`(`EdgeKind`)를 JSON에 실으면 경고 하나가 사라진다

코어 로더가 지금 이 경고를 낸다:

```
문구가 빈 간선 1개를 자동 진행으로 읽었다: Nodes[new02].NextOptions[0].
저작 데이터에 종류 열이 없어 문구의 유무로 판별한다 —
선택지 문구를 실수로 지운 것이라면 여기 나타난다(D5).
```

**저작엔 이미 `ChapterEdge.Kind`(`EdgeKind{Choice,Auto}`)가 있다.** 시트 변경 없이
`NextOptionJson`에 칸 하나만 더하면, 코어가 추론을 그만두고 **문구 없는 `Choice`를
오류로 잡는다** — `ChapterGraphModel.cs:92`의 주석이 원하던 그것이다.

---

## 6. 왜 이 건을 가볍게 보지 않았으면 하는가

같은 부류를 **같은 날 하나 더 찾았다.**

`Ked.Presentation.Core`는 런타임이 소유하고 툴이 **복사**해 갔다. 39개 파일을 대조하니
38개는 같고 **1개가 갈려 있다**:

```
Tuning/PortraitDimensionsDto.cs — 변형 키 정규화

  툴    변형은 마지막 글자만   ('school' → 'l',  'casual' → 'l')
  런타임  변형은 문자열 전체    ('school' ≠ 'casual')
```

런타임이 `38fef522`(2026-08-21)에서 고친 것이 툴 쪽에 안 갔다. `school`과 `casual`이
툴에서는 **같은 키가 된다** — 툴 미리보기가 엉뚱한 초상 치수를 집고 게임은 올바른 것을
집는데, **오류는 하나도 안 난다.** 소비자는 `Flow/CoreStageFold.cs` ·
`Flow/MotionInspection.cs` · `Flow/StageMotionPlan.cs` · `Views/MiniStagePreview.axaml.cs`다.

**이름 갈림도 이것과 같은 병이다.** 둘 다 "오류 없이 다른 답"이고, 둘 다 사본이 둘이라서
생겼다. 이번 건이 §3.1처럼 **규칙을 한 곳으로 모으는** 모양으로 고쳐지길 바라는 이유다.

---

## 7. 확인법 — 고친 뒤 30초

호스트에 사전 대조가 이미 서 있으므로 왕복이 필요 없다.

1. 툴에서 챕터를 저장한다 (`exported/*.progression.json`이 갱신된다)
2. 유니티 `VNAppBootstrap` → `진행 층 › Progression Chapter Path`에 그 파일의 **절대 경로**
3. 키 `3`

```
[진행] 사전 대조 통과 — 부르는 노드가 전부 YarnProject에 있다.
```

이 줄이 나오면 끝이다. 안 나오면 무엇이 왜 안 맞는지 전부 적힌다.

---

## 8. 요약

| | 무엇 | 자리 | 상태 |
|---|---|---|---|
| **①** | `StoryTitleOf`·`StoryNodeTitleOf`를 열고 타이틀 짓는 두 자리가 그걸 부른다 | `YarnBundleEmitter.cs:135`·`:789` | ✅ **완료 2026-08-23** |
| **②** | `DialogueEntryId`가 ①을 통과한다 | `ChapterProgressionExporter.cs:168` | ✅ **완료** |
| **③** | **견본에서 `Story_` 접두를 지운다** — 원천 하나 | `docs/chapter-graph-sample.xlsx` | ⚠ **안 했다 — §9** |
| **③-a** | 파생 기대값 갱신 (기계적 치환) | `tests/**` **11파일 34곳** | ⏸ ③에 딸림 |
| **③-b** | 견본을 다시 내보내 코어 픽스처 갱신 | `ked-progression` `chapter-ch01-sample.json` | ⚠ **지금 갈려 있다 — §9** |
| **③-c** | 공백 든 이름 케이스 추가 | `ChapterExportAndFixtureTests` · `YarnBundleEmitterTests` | ✅ **완료** |
| **④** | `BoolSetNotCarried` 삭제 + `StatChangeJson.Op` | `ChapterProgressionExporter.cs` | ✅ **완료** |
| **⑤** | `NextOptionJson.Kind` | 같은 파일 | ✅ 이쪽 완료 · ⛔ **저쪽 칸 대기** |
| **⑥** | `PortraitDimensionsDto` 동기화 | `src/Ked.Presentation.Core/` | ℹ️ 별건, 알림 |
| **⑦** | `ViaNodeId` 이름 규칙 | — | ⏸ 연출 노드 방출 방식 확인 후 |
| **⑧** | 저작 관문 — `대사엔트리`가 대사노드와 안 맞으면 안 내보낸다 | `ChapterValidator` | ⏸ **보류 — §9** |

테스트 **1490 통과, 실패 0** (Ked.Presentation.Core 343 · Vn.Core 60 · Vn.Authoring 760 · Vn.App 327).

---

## 9. 반영하며 갈린 판단 셋 — 2026-08-23

### 9.1 ③ 견본 접두 — **지우지 않고 파생값을 맞췄다**

견본의 `대사엔트리`는 여전히 `Story_ch05_01`이고, 내보내기는 이제 `Story_Story_ch05_01`을
낸다. **그 값이 맞다** — 이미터가 그 노드에 붙이는 yarn 타이틀도 같은 글자라 사전 대조가
지나간다. `ChapterExportAndFixtureTests`에 그 이유와 **⛔ 중복 접두 가드 금지**를 주석으로
박아 뒀다.

⚠ **더 나은 끝 모양은 ③이다** — 견본이 `ch05_01`이면 실제 기획자가 치는 모양(`new01`)과
같아진다. 안 한 이유는 규모다: 견본 xlsx(바이너리) + `tests/**` 11파일 34곳 + 코어 픽스처
하나가 한 덩어리로 움직여야 하는데, **막고 있던 것은 ①②였고 그건 닫혔다.** ③은 미루어도
아무것도 안 막는다.

### 9.2 ③-b 코어 픽스처가 지금 갈려 있다

`docs/ch01.progression.sample.json`을 다시 구웠다(골든). 그 파일의 **사본**이
`ked-progression/Tests/Fixtures/chapter-ch01-sample.json`인데 **안 따라갔다**:

```
이쪽 골든   "DialogueEntryId": "Story_시작"    + "Op" + "Kind"
저쪽 픽스처 "DialogueEntryId": "시작"          (칸 둘 없음)
```

저쪽 테스트는 `EpisodeId`만 붙들고 `DialogueEntryId`는 안 보므로 **깨지지는 않는다.** 다만
"남에게 건넨 표본이 낡는 것은 계약서가 낡는 것과 같은 무게다"(골든 테스트 주석)에 그대로
걸리는 자리다 — 파일 하나 복사면 끝이고, **저장소 경계를 넘으므로 소유자 판단으로 남긴다.**

### 9.3 ⑧ 저작 관문 — 지금 걸면 안 연 챕터가 전부 거부된다

`대사엔트리`가 프로젝트의 대사노드와 안 맞으면 내보내기를 막자는 안(§3 논의)은 **지금
구조에서 오탐을 낸다**:

```
SyncEpisodes()   고른 챕터 하나만 노드를 만든다   ChapterGraphView.axaml.cs:565
AutoExport()     전 챕터를 내보낸다               같은 파일 :1571
```

한 번도 안 연 챕터는 노드가 없으므로 관문이 하드면 **오늘 잘 나가던 챕터가 전부 거부된다.**
그리고 `ValidationFor`의 캐시 지문(`Fingerprint(entry.Path, episodesFolder)`)이 프로젝트
노드를 안 보므로 동기화가 노드를 만들어도 캐시가 안 깨진다.

바르게 하려면 "그 챕터의 판이 있고 그 판에 노드가 있을 때만 검사"로 좁히고 캐시 지문에
노드 목록을 넣어야 한다 — 별건이다. **①이 닫힌 지금은 급하지 않다**: 양쪽이 같은 함수
하나를 지나므로 *이름을 다르게 짓는* 어긋남은 구조적으로 불가능해졌고, 남는 것은
*노드가 아예 없는* 경우인데 그건 호스트 사전 대조가 잡는다.

---

## 10. 마무리 — 접두 자체가 없어졌다 (2026-08-24, 소유자)

> 소유자: *"`Story_` 이것도 제거해 주십시오. 런타임 측에서도 이제 필터를 없애겠습니다."*

§9.1이 "더 나은 끝 모양은 ③"이라고 남겨 둔 자리가 **③보다 위에서** 닫혔다. 견본에서
접두를 지우는 대신 **이미터가 접두를 안 붙인다**:

```
StoryPrefix = ""        →   타이틀 = SanitizeNodeName(대사엔트리)
```

- **③ · ③-a · ③-b가 함께 없어졌다.** 견본의 `대사엔트리`는 `Story_ch05_01` 그대로 두고,
  내보내기도 `Story_ch05_01`을 낸다 — §9.1이 감수했던 `Story_Story_ch05_01` 중복이
  사라졌다. 견본을 고칠 일이 없으므로 파생값 34곳도 손댈 것이 없었다.
- **⛔ 중복 접두 가드 금지(§9.1)는 여전히 유효하다.** 그런 가드는 애초에 필요가 없었다는
  것이 이번에 확인됐다 — 붙이는 자리를 없애면 중복도 없다.
- **①은 그대로 살아 있다.** 접두가 빠져도 `StoryNodeTitleOf`는 남는다 — 정규화가 남았기
  때문이다(`장면 1` → `장면_1`). 문이 하나라는 것이 이 지시의 본체였고, 그건 안 바뀌었다.

파일 이름도 `{대사엔트리}.yarn`이 됐다. 그래서 `대사엔트리`를 `declarations`로 지으면
선언 파일과 부딪히는데, **이미터가 쓰기 전에 거부한다**.

상세: [`chapter-scope-variables-orders.md`](chapter-scope-variables-orders.md) §9.
