# R-C 인수인계 — 대본 워크북 규격 v15

작성: 2026-09-16 · 기준 브랜치: `dev` · 이어받는 자리: **테스트 정리**

> **한 줄.** 소스는 v15로 다 넘어갔고 컴파일된다. **테스트가 아직 v14 픽스처를 만든다** —
> 그것만 정리하면 녹색이다.

규격 원본은 [`docs/work-orders/tool-owns-workbooks-orders.md`](../work-orders/tool-owns-workbooks-orders.md)다.
이 문서는 그 지시서의 **R-C 진행 상황**만 다룬다.

---

## 1. 지금 어디인가

| 커밋 | 내용 | 상태 |
|---|---|---|
| `6630b26` | 연출그래프의 발행탭 제거 | ✅ 녹색 |
| `dce0308` | 대본 워크북 이미터 (R-B 1/2) | ✅ 1,833/1,833 |
| `d5dabfc` | 챕터 워크북 이미터 (R-B 2/2) | ✅ 1,840/1,840 |
| `93f967b` | 임포트를 챕터 그래프 화면 밖으로 (R-A) | ⚠ 녹색 확인 안 함 |
| (WIP) | **대본 규격 v15** (R-C) | ❌ 빌드 OK · 테스트 다수 실패 |

⚠ **`93f967b`(R-A)은 실제로 녹색을 본 적이 없다.** 그 직전 실행에서 테스트 하나가 실패했고
(내 테스트의 전제 오류였고 고쳤다) 그 뒤 빌드를 돌리기 전에 R-C가 얹혔다. 아래 테스트
정리가 끝나 전체가 녹색이 되면 그때 함께 확인되는 셈이다.

---

## 2. v15가 무엇인가

**대본 시트가 6열에서 4열로 줄었다.**

```
v14   유형 · 조건라벨 │ 인덱스 · LineId · 화자 · 내용
v15                    인덱스 · LineId · 화자 · 내용
```

`유형`(대사/IF/ELSEIF/ENDIF)과 `조건라벨`이 폐지됐고, 그 둘이 그리던 **조건 블록이 함께
사라졌다.** 이제 대본의 모든 행은 대사다.

### 왜 — 근거 셋

1. **런타임이 금지했다.** `ked-presentation-runtime/docs/scene-boundary-plan.md` §4 G0 —
   *"대사에서 스탯 표현·분기 금지. 진행 그래프가 볼 수 없는 숨은 [2] 분기가 대사 속에
   있는 것 자체가 도달성 증명의 사각이었다."* `<<if>>` 자체는 [3](연출 변수) 전용으로 남았다.
2. **대본이 쓸 수 있는 조건은 [2]뿐이었다.** `조건라벨`은 챕터 `조건` 시트에서만 오고
   그 시트는 전부 진행 스탯이다(`EpisodeFlattener` — *"식의 원천은 챕터 `조건` 시트다"*).
3. **그래서 이미 죽어 있었다.** R4가 그 금지를 **내보내기 관문에만** 반영해
   (`YarnBundleEmitter.ValidateProgressionStatReferences`), 대본에서 만들 수 있는 조건 블록은
   **전부** 내보내기에서 막혔다. 실측: `"IF"`를 쓰는 테스트가 10개 파일에 있는데 이미터까지
   가는 것은 **차단을 단언하는 하나뿐**이고, 성공 사례는 **0건**이었다.

**막는 자리(이미터)와 권하는 자리(엑셀 드롭다운)가 다른 말을 하던 것**을 합친 것이 이 작업이다.

---

## 3. 소스는 끝났다 — 무엇이 바뀌었나

`EpisodeRowKind`는 `src`에서 **0건**이다.

| 파일 | 변화 |
|---|---|
| `EpisodeWorkbookModel` | `EpisodeRowKind` 삭제 · `EpisodeRow` 7→5필드 · `IsLine`은 `Index is not null` |
| `EpisodeWorkbookReader` | **482→251줄.** 4열 · `ReadKind`·`VerifyBlocks`·블록 빗장 소멸 |
| `EpisodeWorkbookMigrator` | **전면 재작성.** v14/v13/v10/구판9열 → v15 한 길 |
| `EpisodeLibrary` | `유형` 드롭다운·조건 목록·블록 빗장 제거 · `EnsureWorkbook`/`PushVocabulary` 시그니처에서 `conditionLabels` 제거 |
| `EpisodeWorkbookWriter` | 4열 · `ClearBlockRowIndexes` 삭제 |
| `EpisodeWorkbookEmitter` | 4열 (R-B의 6열 유예 종료) |
| `EpisodeFlattener` | 조건 번역 소멸 — 평평화가 "줄을 순서대로" |
| `EpisodeSyncService` · `EpisodeSyncRunner` | `TidyBlockRows` 삭제 · 조건 공급 빈 목록 |
| `ChapterGraphView` | 미리보기에서 깊이·들여쓰기 소멸 · `PushVocabulary` 호출 |
| `ChapterDiagnostic` | `EpisodeConditionBlockRetired` 추가 |

### 이행기가 핵심이다

`EpisodeWorkbookMigrator`를 통째로 다시 썼다. **줄어드는 이행이라 앞의 모든 판이 한 길로
합쳐진다** — 남길 칸이 넷뿐이므로 머리글로 그 넷을 찾아 새 시트에 옮기면 어느 판에서 오든
같은 코드가 처리한다. 짝마다 맞바꾸기를 두던 경우 수가 통째로 사라졌다.

지켜야 할 것 셋:

- **조건 블록 행은 버리되 몇 행 버렸는지 말한다** (`MigrationResult.Failure`가 아니라 알림으로).
- ⛔ **인덱스는 절대 다시 매기지 않는다.** 신원이고 `ExcelLineMap`이 붙들고 있다. 블록 행을
  걷으면 번호가 뜨문뜨문해지는데, 오름차순이 v10에서 권고로 내려간 것이 이런 경우를 위해서다.
- **CHOICE·OPTION 행은 남긴다.** 사람이 쓴 문구가 사라지면 안 된다.

---

## 4. 남은 일 — 테스트 정리

마지막 실측(v15 소스 직전 기준) **83개 실패**. 성격이 셋으로 갈린다.

### ① 은퇴 — 폐지한 기능 자체를 지키던 것

지운다. 이 테스트들이 지키던 계약이 더는 없다.

| 파일 | 실패 | 비고 |
|---|---:|---|
| `Chapters/EpisodeWorkbookReaderTests` | 12 | IF/ELSEIF/ENDIF 짝·조건라벨·중첩 블록·유형 |
| `Chapters/EpisodeFlattenerTests` | 5 | 조건 번역 |
| `Chapters/EpisodeBlockRowGuardTests` | 전부 | 파일째 삭제 |
| `Chapters/EpisodeColumnOrderV14Tests` | ~2 | 블록 행 번호 정리 관련만 |

⚠ **파일을 통째로 지우기 전에 확인할 것** — 같은 파일에 v15에서도 유효한 테스트(머리글로
시트 찾기, 인덱스 중복, 빈 행 거르기)가 섞여 있다. 그것은 4열 픽스처로 고쳐 남긴다.

### ② 픽스처 수정 — 블록 행을 배경으로 쓸 뿐인 것

대부분 **파일당 헬퍼 하나**가 워크북을 만든다. 그 헬퍼를 4열로 고치면 그 파일 전체가 따라온다.

| 파일 | 실패 |
|---|---:|
| `Vn.App.Tests/ExcelNodeLockTests` | 16 |
| `Chapters/ChapterDeleterTests` | 8 |
| `Chapters/EpisodeLineEditorTests` | 7 |
| `Chapters/ChapterRenamerTests` | 7 |
| `Vn.App.Tests/ExcelToPresentationGraphTests` | 5 |
| `Vn.App.Tests/ChapterGraphSyncViewTests` | 4 |
| `Vn.App.Tests/StarterProjectTests` | 3 |
| `Chapters/WorkbookParseCacheTests` | 1 |

바꾸는 모양:

```
전:  ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"]
     [null,   null,       "10",     "ln_0001", "윌로", "복도는 조용했다."]
     ["IF",   "신뢰높음",  null,     null,      null,   null]

후:  ["인덱스", "LineId", "화자", "내용"]
     ["10",    "ln_0001", "윌로", "복도는 조용했다."]
```

### ③ 뜻이 바뀌는 것 — 손으로 다시 쓴다

**`Chapters/EpisodeSyncServiceTests.엑셀_출처_노드의_진행_스탯_Yarn_참조를_발행_전에_막는다`**

이 테스트가 지키던 것은 *"IF 블록이 [2] 스탯을 참조하면 **내보내기가** 막는다"* 였다.
v15에서는 **읽는 시점에** 막히므로 내보내기까지 가지도 않는다.

→ 삭제가 아니라 **관문이 앞당겨졌다는 사실을 지키도록** 다시 쓴다. 구판 워크북(IF 행 포함)을
읽으면 `EpisodeConditionBlockRetired` 오류가 서는지, 그리고 이행기를 태우면 그 행이 걷히고
대사만 남는지. 그게 이번 변경의 요점이다.

⚠ 그 테스트 안의 `#if false` 블록(*"R4 이전: 진행 스탯을 Yarn 전역 변수로 선언하여 컴파일하던
계약"*)은 옛 계약의 화석이다. 함께 지운다.

### ④ 새로 있어야 할 테스트

- **이행 왕복**: v14 6열 워크북(IF 행 포함) → `EpisodeWorkbookMigrator.Migrate` → 4열이 되고
  블록 행이 사라지고 **대사 행의 인덱스·LineId가 그대로**인지. ← 데이터 안전의 핵심
- **이행 알림**: 버린 블록 행 수가 보고에 실리는지
- 이미 있는 `ChapterImportServiceTests.이미터가_낸_파일은_이행이_필요_없다`가 **이미터↔리더
  규격 드리프트의 파수꾼**이다. 건드리지 말 것

---

## 5. 검증

```powershell
dotnet build .\VnTool.sln -c Release      # 오류 0 · 경고 0 (TreatWarningsAsErrors)
dotnet test  .\VnTool.sln -c Release
```

⚠ **솔루션 단위 `dotnet test`는 빌드 실패한 프로젝트를 조용히 건너뛴다.** 테스트 어셈블리가
**4개** 다 돌았는지 확인할 것 — `artifacts\diag.ps1`이 그 검사를 포함한다.

기준선: R-C 착수 전 **1,840/1,840**. ①의 은퇴분만큼 총수가 줄고 ④만큼 는다.

---

## 6. 이 작업에서 물린 함정 (되풀이하지 말 것)

- **ClosedXML의 경로 `SaveAs`는 확장자를 검사한다** — `.tmp`로 끝나는 이름을 거부한다.
  `WorkbookAtomicWrite`가 스트림→바이트로 우회한다. 지우면 두 이미터가 전부 터진다.
- **`docs/chapter-graph-sample.xlsx`는 현행 규격이 아니다.** 열면 이행된다. "현행 규격의
  실물"이 필요하면 이미터 산출물을 쓸 것.
- **큰 슬라이스로 잘라내면 사이에 살던 것이 딸려 나간다.** `ReadKind`~`Cell` 구간을 한 번에
  지웠다가 `DataRows`·`VerifyBlocks`를 함께 잃었다(git에서 복구).
- **미사용 지역변수가 빌드를 깬다** (`TreatWarningsAsErrors=true`). 시그니처에서 인자를 빼면
  호출부의 준비 코드도 함께 지울 것.
- **테스트가 실패를 실패로 못 잡는 경우가 있다.** `임시_파일을_남기지_않는다`가 그랬다 —
  `Emit`이 실패해도 임시 파일은 치워지므로 통과했다. **성공 단언을 먼저 세울 것.**

---

## 7. 다음 단계 (R-C 이후)

지시서 §7 기준으로 R-D·R-E·R-F가 남는다.

⛔ **R-D(역방향 철거)는 되돌릴 길을 닫는다** — 그 전까지는 "워크북에서 다시 임포트"가
탈출구다. R-A~R-C가 테스트로 굳은 뒤에 착수할 것(지시서 §9).
