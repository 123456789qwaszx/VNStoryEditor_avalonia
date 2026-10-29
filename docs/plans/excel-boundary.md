# 엑셀 입출력 분리 — 원본은 프로젝트, 엑셀은 들어오는 것과 나가는 것

> 2026-10-29 소유자: 서버는 두지 않는다. 다만 **엑셀 입출력은 개념적으로 분리돼야 한다.**
> 근거는 설계 조사 보고서 `VNStoryEditor_Architecture_Audit_2026-10-29.md`(소유자 보관)의 개선 지점 25건.
> 보고서의 결론과 같다 — 문제는 서버의 유무가 아니라, 원본을 프로젝트로 옮긴 뒤에도 **엑셀 중심의 규칙이
> 여러 곳에 남아 있다**는 것이다. 전면 폐기가 아니라, 이미 옮겨 둔 원본 모델을 기준으로 나머지를 정렬한다.

## 1. 지금의 사실 (2026-09-16 R-A~R-F 뒤)

- 원본: `StoryProject.Chapters`(챕터 구조)·`StoryProject.Scripts`(대사). 저장은 프로젝트 JSON.
- 엑셀 입력: 명시적 가져오기(`Chapters/Import/*`).
- 엑셀 출력: `ChapterWorkbookEmitter`·`EpisodeWorkbookEmitter` — `chapters/`·`episodes/`는 **산출물**.
- 챕터 감시자(`ChapterFolderWatcher`)는 저장 동기화를 하지 않고 **잠금 변화만** 듣는다.

그런데 잠금·개명·검증·캐시·화자 변경에는 옛 규칙이 살아 있다. 이 문서가 그것을 하나씩 뗀다.

## 2. 규칙 (이 작업이 코드에 관철하는 것)

| # | 규칙 | 뜻 |
|---|---|---|
| R1 | **산출물의 상태가 원본 편집을 막지 않는다** | 엑셀이 파일을 열고 있으면 못 하는 것은 *그 파일을 지금 쓰는 일*뿐이다. 편집은 되고 출력만 미뤄진다 |
| R2 | **검증은 프로젝트만 본다** | 엑셀을 읽는 검증은 가져오기에만 있다. 내보내기 관문은 프로젝트 스냅샷으로 판정한다 |
| R3 | **캐시 열쇠는 원본의 내용** | 디스크 파일의 해시가 아니라 검증이 실제로 읽은 모델 |
| R4 | **개명·변경은 모델 명령 하나** | 사용자 동작 하나 = 되돌리기 한 칸. 파일 이름 바꾸기는 후속 출력이다 |
| R5 | **엑셀을 고치지 않는다 — 다시 낸다** | 산출물을 셀 단위로 수정하는 길(화자 개명 Writer)을 없애고, 프로젝트를 바꾼 뒤 전체를 재출력한다 |
| R6 | **출력은 한 조정자** | 챕터·대본 워크북이 같은 Pending/Retry/EmitAll을 지난다. 저장이 전부 다시 낸다 |
| R7 | **에피소드 식별은 (ChapterId, EpisodeId)** | 프로젝트 전체를 EpisodeId 하나로 뒤지지 않는다 |

## 3. 순서 — 1단계 "잘못된 연결 끊기"

한 커밋 = 한 항목. CI(Windows) 녹색을 보고 다음으로. 로컬에 dotnet이 없어 CI가 유일한 검증이다.

| 커밋 | 보고서 | 무엇 | 상태 |
|---|---|---|---|
| C1 | 12 | `ChapterBoard`를 그 챕터의 판으로 한정 (R7) | 54d4be6 · fe4a21e (run 44) |
| C2 | 04 | 챕터 개명: `ProjectEditor.RenameChapter`가 ChapterId·판 이름·조건 공급 노드를 한 Mutate로. `ChapterRenamer`는 모델 개명 뒤 파일 이동 — 못 옮겨도 모델을 되돌리지 않고 사유만 (R4) | 003b9d2 (run 45) |
| C3 | 02·03 | `ChapterValidator`에서 대본 엑셀 읽기 삭제, 지문을 모델 내용으로 (R2·R3) | 477da59 · e012e73 (run 47) |
| C4 | 05·11 | 화자 개명: 프로젝트만 바꾸고 워크북은 재출력. `EpisodeWorkbookWriter.RenameSpeaker` 삭제 (R5) | cfcfb15 (run 48) |
| C5 | 06 | 출력 조정자: `ChapterOutput`→`WorkbookOutput`이 대본 워크북도 낸다. 저장 = 전부 재출력. `EpisodeWorkbookWriter`·첫 대사 심기 삭제 (R6) | 이 커밋 |
| C6 | 01 | 잠금 관문 제거 — 편집은 늘 열려 있고 배너는 "출력이 미뤄진다" (R1) | |

C6를 마지막에 두는 이유: 잠긴 채 편집을 허용하려면 **못 낸 출력을 기억하고 다시 내는 길(C5)**이 먼저 있어야 한다.

## 4. 항목별 설계

### C1 — ChapterBoard (12)

`ChapterBoard.For(project)`가 프로젝트의 모든 대사 노드를 `MarkedEpisodeId` 하나로 사전에 넣었다(`TryAdd` — 먼저 만난
것이 이긴다). `EpisodeLibrary`는 EpisodeId가 챕터 안에서만 유일하다고 명시한다. `For(project, chapterId)`로 바꾸고
`EpisodeNaming.BoardOf`가 주는 **그 챕터의 판** 노드만 본다. 이름 뒷길(`대사엔트리`)도 그 판 안에서만.

### C2 — 챕터 개명 (04)

지금: `ChapterRenamer.Rename`이 워크북 파일·대본 폴더·`StoryFile.Name`·조건 공급 노드 이름을 바꾸지만
`Project.Chapters[*].ChapterId`는 **안 바꾼다**. `ProjectEditor.RenameChapter`는 있는데 아무도 부르지 않는다.
결과: 저장하고 다시 열면 챕터는 옛 이름, 판은 새 이름.

바꿈: `ProjectEditor.RenameChapter(old, new)`가 ChapterId + 판 이름 + 조건 공급 노드 이름을 **한 `Mutate`**로 바꾼다
(되돌리기 한 칸). `ChapterRenamer`는 ① 막을 것(이름 충돌)을 보고 ② 모델을 개명하고 ③ 워크북·대본 폴더를 옮긴다.
③이 실패하면 **모델을 되돌리지 않는다** — 파일은 산출물이라 다음 출력이 새 이름으로 다시 내고, 옛 파일은 사유와 함께 알린다.
(전에는 되돌렸다. 원본이 파일이던 시절의 규칙이다.)

### C3 — 검증 (02·03)

`ChapterValidator.Validate(chapter, episodesFolder, project)`가 `episodes/{챕터}/*.xlsx`를 읽어 리더·평평화의 진단을
합친다. 스탯 증감은 이미 챕터 모델(간선)에서 읽고, "줄이 없는 노드"는 이미 프로젝트(`VerifyScenesFilled`)로 본다.
엑셀에서 얻는 것은 **산출물에 대한 리더의 불평**뿐이다 — 원본이 같은데 산출물 상태로 판정이 달라지는 자리.
`episodesFolder`를 지운다. `ChapterProgressionExporter.Export`도 같이.

지문(`ChapterExportService.Fingerprint`)은 챕터 워크북·대본 워크북의 SHA-256이었다. 검증이 읽는 것이 모델이므로
열쇠도 모델이어야 한다: 그 챕터의 `ChapterDocument` JSON + 정의의 변수 키 + 판 노드(이름·줄 유무). 되돌리기·조건 변경·
출력 실패에 흔들리지 않는다.

### C4 — 화자 개명 (05·11)

`SpeakerRenamer`가 워크북을 먼저 셀 단위로 고치고(`EpisodeWorkbookWriter.RenameSpeaker`) 프로젝트를 바꿨다.
그 Writer는 머리글을 1행에서 찾는데 이미터는 2행에 쓴다 — 툴이 낸 파일의 화자 개명이 실패하는 경로.
R5대로: 프로젝트의 줄을 바꾸고(`editor.RenameSpeaker`) 그 줄을 가진 에피소드의 워크북을 **다시 낸다**. 잠긴 파일은
미룸이지 거부가 아니다. `EpisodeWorkbookWriter.RenameSpeaker`는 지운다. 등록부(`game.definition.json`)와 대본의
되돌리기 경계를 합치는 것(보고서 11의 나머지)은 2단계로.

### C5 — 출력 조정자 (06)

챕터는 `ChapterOutput`(Pending/Retry/EmitAll)이 있고, 대본은 `ScriptView.EmitWorkbook`이 바로 쓰고 실패 문구만
돌려준다. 저장은 대본을 다시 내지 않는다. `ChapterOutput`이 대본 워크북도 맡는다 — 챕터를 낼 때 그 챕터의 에피소드
대본도 내고, Pending에 에피소드도 든다. `ScriptView`는 조정자를 부른다.

### C6 — 잠금 관문 (01)

`ChapterGraphView.ToolEditable => !_excelHoldsChapter`가 에피소드·간선·조건 편집을 막는다. 원본이 프로젝트가 된 뒤에도
산출물의 잠금이 원본의 편집 권한을 정하는 자리다. 관문을 지우고, 배너는 *"엑셀이 열고 있어 이 파일의 갱신은 닫힐 때
따라갑니다"*로. 감시자는 Retry의 신호로만 남는다.

## 5. 2단계 이후 (이 문서 밖, 보고서 §8)

가져오기의 원본 불변·전체 원자성(07·08·09), 프로젝트 저장의 전체 원자성·충돌 정책(10·13), 정의와 대본의 되돌리기
경계(11), 삭제·이동의 Undo(19), 임시 파일·오류 분류(14), 입력/출력 폴더 분리와 교환용 LineId(15·16), 그리고
`ked-story-director`로 넘길 **버전된 스토리 패키지**(23·§9).
