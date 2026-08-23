# 구역 지도 — 다섯 프로젝트의 역할과 경계

기준: 2026-08-23 실측 · 소스 61,797줄 · 테스트 1,510

> [`three-repo-map.md`](three-repo-map.md)이 **저장소 셋**의 경계를 다룬다면, 이 문서는
> **저장소 하나 안의 다섯 구역**을 다룬다. 2b 작업(에피소드 동기화를 화면 밖으로)이
> 어느 선을 어떻게 옮기는 일인지가 §4에 있다.

---

## 1. 다섯 구역

```
Vn.Core ─────────► Vn.Cli          Yarn 텍스트 → 진단
   ▲                                (2026-08-23부터 Vn.App도 소비자)
   │
   └──────────┐
              │
Ked.Presentation.Core ──► Vn.Authoring ──► Vn.App
   무대 상태 계산          저작 도메인        Avalonia 셸
   (런타임이 주인)         (공식 원본)
```

| 구역 | 파일 | 줄 | 한 문장 |
|---|---|---|---|
| **`Vn.Authoring`** | 111 | 29,500 | **저작 도메인.** 프로젝트가 무엇이고 무엇으로 나가는가 |
| **`Vn.App`** | 34 | 24,200 | **Avalonia 셸.** 사람이 만지는 자리 |
| **`Ked.Presentation.Core`** | 39 | 4,876 | 무대 상태 계산. **런타임 저장소가 주인이고 여기는 사본** |
| **`Vn.Core`** | 30 | 3,351 | Yarn 텍스트를 읽고 **진짜 컴파일러로** 컴파일한다 |
| **`Vn.Cli`** | 1 | 260 | `Vn.Core`의 콘솔 얼굴 |

---

## 2. 구역별 역할

### 2.1 `Vn.Core` — 텍스트가 들어오고 진단이 나간다

| 폴더 | 줄 | |
|---|---|---|
| `Yarn` | 1,916 | 컴파일러 접합 · 진단 매핑 · 블록/라인 인덱스 |
| `Schema` | 555 | `game.schema.json` — 커맨드·변수 어휘 |
| `Validation` | 348 | 어휘 사용 검사 · 작성 규약 |
| `Story` | 203 | **파싱 결과** 모델(`StoryNode` = 파일·줄 범위) |
| `Diagnostics`·`Reporting`·`Analysis` | 311 | 도구 공용 진단 모델 |

**모르는 것**: 저작 모델(`StoryProject`·`DialogueNode`), 엑셀, 화면.
**유일한 외부 의존**: `YarnSpinner.Compiler`.

⚠ `Vn.Core.Story.StoryNode`(파싱 뷰)와 `Vn.Authoring.Model.StoryNode`(편집 모델)는
**다른 개념인데 이름이 같다.** 테스트가 둘 다 참조하면서 전체 이름을 적어야 한다.

### 2.2 `Vn.Cli` — 260줄, 파일 하나

`Vn.Core`를 콘솔로 연다. 골든 픽스처 회귀의 실행기다.

### 2.3 `Vn.Authoring` — 저작 도메인 (11폴더)

폴더 열하나는 **네 층**으로 읽는 것이 맞다:

| 층 | 폴더 | 줄 | 무엇 |
|---|---|---|---|
| **원본** | `Model` · `Script` · `Serialization` · `Editing` | 9,144 | 프로젝트가 무엇인가 + **그것을 고치는 유일한 길**(`ProjectEditor` 2,851) |
| **계산** | `Flow` · `Graph` | 4,872 | 원본에서 파생되는 답 — 조건 흐름·분기·무대 폴드·재생 경로 |
| **산출** | `Rendering` · `Results` | 4,838 | 밖으로 나가는 것 — yarn 이미터·발행·합성·매니페스트 |
| **어휘·자산** | `Definition` · `Assets` | 2,236 | 커맨드 팔레트·초상·튜닝 |
| **기획자 계층** | `Chapters` | **8,410** | 위 넷을 엑셀 위에서 다시 한 벌 — 가장 크고 가장 새것 |

**모르는 것**: 화면, 파일 대화상자, Yarn 컴파일러.
**아는 것**: 엑셀(ClosedXML — 8파일). 이것은 의도된 것이다. 제품 전제가 *"엑셀이 원본"*이다.

### 2.4 `Vn.App` — Avalonia 셸

| 폴더 | 파일 | 줄 | |
|---|---|---|---|
| `Views` | 16 | **19,234** | 코드비하인드. 상위 넷이 12,628줄(66%) |
| `Services` | 15 | 3,370 | 세션·라이브 출력·오디오·재생 |
| 뿌리 | 3 | 1,431 | `MainWindow` 1,264 · 부트스트랩 |

**아는 것**: Avalonia, 파일 대화상자, OS 연결, 오디오 장치.

### 2.5 `Ked.Presentation.Core` — 사본

`Reduce`(1,897) · `Tuning`(847) · `State`(791) · `Ease`(583) · `Transforms`·`Tokens`·`Primitives`(759).
**주인은 `ked-presentation-runtime`이다.** 이쪽은 손으로 복사해 온 한 벌이고, 그 복사의 단위는
파일이 아니라 **한 번의 내보내기**다(코드 + 튜닝 덤프가 같이 움직인다 — 2026-08-23 실측).

---

## 3. ⚠ 경계가 새는 자리 — 실측

### 3.1 `Vn.App/Services`의 절반이 화면을 모른다

| 파일 | 줄 | Avalonia | 자기 주석이 말하는 것 |
|---|---|---|---|
| `StagePlayback` | 564 | **0** | *"시간의 계산만 있고 타이머·UI가 없다(테스트 가능)"* |
| `StageSceneComposer` | 415 | **0** | *"…를 계산하는 **순수 함수**"* |
| `StageTransitions` | 52 | **0** | *"라인 전이 시간의 **규약**"* |
| `YarnOutputVerification` | 123 | **0** | (2026-08-23 신설 — 일부러 그렇게 지었다) |

**셋이 스스로 "순수하다"고 적어 두고 셸에 산다.** 합쳐서 1,031줄이다.

반대로 셸의 것이 분명한 것들: `AppSettingsService`(최근 프로젝트) · `StartupLog` ·
`SpreadsheetAssociation`(OS 연결) · `AssetRootPicker`(대화상자) · `ProjectRefreshPlanner`
(*"각 **화면**에 어떤 갱신을 요구하는지"*) · `AudioPreview` · `UiGuard` · `PreviewImageCache`.

→ **"Avalonia를 안 만진다"가 곧 "옮겨야 한다"는 아니다.** 판정 기준은 다음이다:
> **다음 게임 툴을 만들 때 이것을 다시 쓰나?**

### 3.2 `AuthoringSession`의 Avalonia 접점은 **한 줄**이다

```csharp
// AuthoringSession.cs:1    using Avalonia.Media.Imaging;
// AuthoringSession.cs:129
public PreviewImageCache<Bitmap> ImageCache { get; } = new(path => new Bitmap(path));
```

1,041줄 중 그 한 줄이 전부다. 그리고 `PreviewImageCache<T>`는 **이미 제네릭**이다.
세션은 자기 주석대로 *"도구의 관심사만 얹는" 객체*에 거의 도달해 있다.

### 3.3 `EnsureChapterBoard`는 세션의 것이 아니다

```csharp
internal string EnsureChapterBoard(string chapterId)
{
    StoryFile? board = Project.Files.FirstOrDefault(f => f.Name == chapterId);
    board ??= Editor.AddStoryFile(chapterId);
    Editor.EnsureChapterSettingsNode(board.Id);
    return board.Id;
}
```

**세션 상태를 하나도 안 본다.** 순수한 `ProjectEditor` 작업인데 세션에 주차돼 있다.
2b가 막히는 이유의 절반이 이것이다.

### 3.4 `Views`가 코드베이스의 31%다

19,234줄. `interface` 0 · `abstract class` 0 · `ViewModel` 0.
`ChapterGraphView` 하나에 `internal` 33개 — 테스트가 뷰를 **뚫고** 들어간다는 뜻이다.

---

## 4. 지금 작업(2b)의 목표

### 4.1 종전 프레이밍은 좁았다

*"`SyncEpisodes` 156줄을 옮긴다"* — 이건 수단이지 목표가 아니다. 실측이 더 정확한
목표를 준다:

> # 🔒 **`Vn.App`에는 화면만 남긴다.**
> **다음 게임 툴이 다시 쓸 것은 전부 `Vn.Authoring`에 있어야 한다.**

### 4.2 그 기준으로 지금을 재면

`Vn.Authoring`을 그대로 들고 가면 새 툴은 **저작·챕터·이미터·발행**을 얻는다.
그런데 **못 들고 가는 것**이 이만큼 있다:

| 못 들고 가는 것 | 줄 | 왜 아까운가 |
|---|---|---|
| 무대 재생 진행 모델 (`StagePlayback`) | 564 | 타자·여운·선택지 대기 — 게임 툴 공용 |
| 무대 배치 계산 (`StageSceneComposer`) | 415 | 순수 함수라고 자기가 적어 뒀다 |
| 전이 시간 규약 (`StageTransitions`) | 52 | 24fps 규약 — 코어와 짝 |
| 에피소드 동기화 순서·정책 (`SyncEpisodes`) | 156 | 엑셀↔노드 반영은 이 제품군의 심장 |
| 판 보장 (`EnsureChapterBoard`) | 12 | 순수 `ProjectEditor` 작업 |

합 **1,199줄** — 규모가 아니라 **종류**가 문제다. 전부 "다음 툴에서도 똑같이 필요한 것"이다.

### 4.3 2b가 실제로 바꾸는 것

```
지금   SyncEpisodes(뷰 156줄)
         ├─ AuthoringSession 6:  Editor · Definition · ProjectPath
         │                       SetStatus · EnsureChapterBoard · NotifyExternalScriptChange
         └─ 뷰 사적 메서드 7:    AdoptFlatWorkbooks · PushVocabularyToEpisodes
                                 ProjectSpeakerNames · StartWatchingEpisodes
                                 SupplyEdgePresentations · Validate · Draw
```

여섯 중 넷은 이미 도메인이거나 문자열이다. **진짜 셸의 것은 `SetStatus`와
`NotifyExternalScriptChange` 둘뿐이고, 둘 다 _출력_ 이다.**

→ 그래서 인터페이스를 만들지 않는다. **결과를 반환하고 셸이 말하게 한다.**
(이 코드베이스의 `interface` 총 개수는 1이다 — 그 결을 지킨다.)

```
목표   EpisodeSyncRunner.Run(editor, definition, projectPath, chapter, fileId)
         → EpisodeSyncRun { Reports, BoardWarnings, Migrations, Applied, Rejected }
       뷰: 그 결과를 상태줄과 판에 그린다
       ProjectEditor.EnsureChapterBoard(chapterId)   ← 세션에서 내려온다
```

### 4.4 완료 정의

| | |
|---|---|
| `EpisodeSyncRunner`가 `Vn.Authoring/Chapters`에 있고 **Avalonia·세션을 모른다** | |
| 그 규칙의 테스트가 `Vn.Authoring.Tests`에서 돈다 (7초 대, UI 없이) | |
| `EnsureChapterBoard`가 `ProjectEditor`로 내려간다 | |
| `ChapterGraphView`의 `internal`이 **33 → 20 이하** | |
| 뷰는 `SetStatus`·`Draw`만 한다 — 순서를 정하지 않는다 | |

### 4.5 이번에 하지 않는 것

`StagePlayback`·`StageSceneComposer`·`StageTransitions` 1,031줄의 이사는 **별건**이다.
2b와 같은 병(화면이 아닌 것이 셸에 산다)이지만, 저 셋은 이미 순수해서 **옮기기만 하면**
되고 위험이 낮다 — 반대로 `SyncEpisodes`는 경계를 새로 그어야 한다. 어려운 쪽을 먼저 한다.
