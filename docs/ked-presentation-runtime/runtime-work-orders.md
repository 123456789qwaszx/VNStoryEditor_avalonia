# ked-presentation-runtime 단독 작업 지시서 (U1–U17)

런타임 저장소에서 **VnTool 변경 없이 단독 수행 가능한** 리팩토링·수정·추가 작업. 근거와 상세는 `runtime-knowledge-base.md`(지식 문서, 특히 §18 결함 목록)와 `runtime-refactor-direction.md`(방향서 R0–R4). 이 지시서는 그것을 실행 단위로 쪼갠 것이다. 그룹 순서 = 착수 순서(위험도 오름차순). **커밋은 U 단위.**

> 2026-08-04 개정: U12-v1을 규약 경로 직접 출력으로 변경(W-asset-02 §6 확정), 소유자 결정 #3·#6 확정 반영.
> 세션별 붙여넣기 프롬프트는 `runtime-parallel-orders.md` 참조 — U12-v1·U6·U2는 그쪽 개정본이 정본이다.

## 공통 규칙

- **동작 호환이 기본.** 기존 .yarn 콘텐츠가 지금과 같게 재생되어야 한다. 의도적 동작 변경은 각 U에 "동작 변경" 표기가 있는 것뿐이다.
- **테스트 체계가 없는 저장소다.** 그룹 A–C의 수용 기준은 (a) 순수 C# 부분의 EditMode 테스트 신설, (b) 씬 재생 스모크(에피소드 1개 처음→끝, 세이브→로드, 롤백 1회)로 한다. 그룹 D부터는 Core 라이브러리에 정식 단위 테스트를 붙인다.
- **계약서 연동 의무.** U3·U8처럼 VnTool 이미터 계약(`runtime-contract.md`)에 닿는 변경은 완료 시 계약서의 해당 조항 개정 메모를 커밋 메시지에 남긴다.
- 스타일은 지식 문서 §19 관례를 따른다(DBSO 관례, partial 분할, 산문 계약 주석, ClaimTarget→트윈→CommitFinalState 골격).

---

## 그룹 A — 버그·방어 (동작 신뢰성. 즉시, 순서 무관 병렬 가능)

### U1. EpisodePlayer 재시작 래치 버그 수정
- `Game/EpisodePlayer.cs` — 재시작 코루틴의 세대 검사 데드 브랜치(`if (g != _gen) { if (g == _gen) … }`)와, `StopCoroutine`이 `_isRestarting` 해제 전에 코루틴을 죽이는 문제를 고친다. 권장: 플래그+코루틴 조합 대신 세대 번호 단일 기준으로 재작성하고, 해제는 `finally`.
- **수용**: 재시작 중 재시작(롤백 연타) 반복 후에도 StartGame이 무시되지 않는 EditMode/수동 재현 시나리오 통과.

### U2. 로드 시크 실패 경로 (조용한 행 제거)
- `Game/SaveLoad/VNLoadSeekDriver.cs`, `VNLinePresentationFlow` — 시크 중 노드가 끝까지 재생됐는데 타깃 `{nodeName, lineId}` 미발견이면: `Fail()` 호출(현재 아무도 안 부름) → 시크 상태 해제 → 노드 처음부터 정상 재생 폴백 + 경고 로그·트레이스 기록. `OnDialogueCompleteAsync` 시점에 `IsSeekingActive`가 남아 있으면 실패로 판정하는 방식 권장.
- **동작 변경**: 기존(입력 잠긴 무한 빨리감기) → 폴백 재생.
- **수용**: 존재하지 않는 lineId를 가진 조작된 세이브를 로드해도 플레이 가능 상태로 복귀.

### U3. 서브 레인 자기방어 (jump 누수 + pres_start 재호출)
- `VNLinePresentationFlow/SyncHub/VNSideRunnerSyncHub.cs`, 노드 시작 훅 — ① 메인 러너 `onNodeStart` 시 서브 레인이 아직 실행 중이면 자동 `StopPresentationLaneCoroutine`(경고 로그와 함께 — 저작 실수 신호다). ② `StartPresentationLaneCoroutine`이 실행 중 러너에 곧장 `StartDialogue` 하지 않도록 선행 정지(원샷 레인의 `Stop()` 선례를 따름).
- **동작 변경**: jump 후 레인이 어긋나는 대신 정리됨.
- **수용**: `pres_end` 없이 jump하는 테스트 yarn에서 다음 노드가 정상 락스텝. 완료 시 **계약서 A5·A6을 "이미터 필수"에서 "권장(런타임 방어 있음)"으로 개정**.

### U4. 선택 기록 강화
- `VNLinePresentationFlow/VNOptionFlow/VNChoiceRecord.cs`, `VNChoiceBoundary.cs` — ① `TryGetChoiceRecord` 매칭에 nodeName 포함(서수 충돌 오매칭 제거). ② 생성자에서 버리던 `selectedOptionLineId`를 저장하고, 리플레이 시 lineId 우선 매칭 → 실패 시 서수 폴백. 세이브 JSON에 필드 추가(구 세이브는 필드 부재 → 서수 폴백이라 호환).
- **수용**: lineId 매칭·서수 폴백 각각의 EditMode 테스트, 구 세이브 로드 호환 확인.

### U5. 부트스트랩 검증·순서 명시화
- `Game/VnAppBootstrap.cs` — ① 세 러너의 `VariableStorage`가 동일 인스턴스인지 Awake에서 assert(아니면 명시 오류 — 락스텝·선택 리플레이의 전제다). ② 디버그 뷰 등 선택적 필드 null 가드. ③ 부트 단계 간 암묵 의존(⑥→⑦ 순서, EpisodePlayer 지연 초기화)을 명시적 인자 전달로 전환하거나 최소한 주석 계약화. ④ 미사용 직렬화 필드(`linePresenter`, `episodeProgressionSo`) 제거, `ConfigureAlbumView` 중복 호출 정리.
- **수용**: 의도적으로 저장소를 다르게 배선한 씬에서 부팅 즉시 명시 오류. 정상 씬 스모크 통과.

### U6. 조용한 실패 소리내기
- ① `box_named`/`box_protagonist`: `Enum.TryParse` 결과 검사 — 실패 시 경고 + 현상 유지(Portrait 폴백 제거). ② `screen_blur` 계열 layerKey 파싱 실패: 무음 폐기 → 경고. ③ `DialogueBoxHost.ResolveTarget` null을 컨트롤러가 검사, 미등록 kind는 기본 박스 폴백 + 경고. ④ 미지원 스펙 드롭 로그에 스펙 타입명 포함.
- **동작 변경**: 오타가 조용히 다른 연출이 되는 대신 들리게 됨(재생은 계속).
- **수용**: 각 케이스 재현 yarn에서 경고 발생·재생 지속.
- 옵션 라벨 접두 스트리핑은 **넣지 않는다** (소유자 확정 — 아래 결정표 #3).

---

## 그룹 B — 청소 (그룹 A와 병렬 가능)

### U7. 사장 코드 정리
- 삭제: `Game/NodeGraphLegacy/`(15파일 전부 주석), `Game/Legacy/`(주석 파일들 — 단 `InlineEventMarkupHandler`의 계약 주석은 지식 문서가 이미 보존하므로 삭제 가능), `SpeedUpModeController`(미생성), `VnUxState`(미전달 — 쓸 계획 없으면).
- 격리: `Messenger/` 폴더(+내부 .prefab 2개)는 미배선 대안 프리젠터 — **소유자 결정**: 삭제 vs `Experimental/`로 이동. `PlayerState`·`YarnVariableBridge`는 U9에서 다룸.
- **수용**: 컴파일 통과, 스모크 통과, 삭제 목록이 커밋 메시지에 명시.

### U8. 소소한 정합 수정 묶음
- 24fps 상수 단일화(`YarnDurationParser` 것을 진실로, `CommandBridge.Control`이 참조). 롤백 히스토리에 캡 도입(예: 500, FIFO — 백로그 100과 일관된 정책 주석). 백로그 `lineSerial`/`timestamp` 미완 필드는 채우거나 제거. 에피소드 정지 경로에서 박스 kind 오버라이드·서피스 레이아웃·`DialogueBoxCurrentState` 리셋(다음 에피소드로 새는 상태 차단). `VnAdvanceInputPoller.Initialize`가 인스펙터 바인딩을 덮어쓰는 문제 수정. `VNTraceStream`의 호출마다 전체 미러 제거(덤프 시에만 문자열화).
- **동작 변경**: 에피소드 경계의 박스 상태 리셋.
- **수용**: 스모크 + 각 항목 확인.

### U9. 변수·플래그 배선 결정 실행 (**소유자 결정 선행**)
- 결정지: ① `YarnVariableBridge`를 정식 배선(에피소드 언락에 Yarn 변수 반영 — `ApplyRuntimeStateBeforeDialogue`/`CollectRuntimeStateAfterDialogue` 왕복) 또는 제거. ② `EmptyVNFlagStore`를 실제 플래그 저장으로 교체 여부(U14 세이브 v2와 함께 가면 자연 해소). ③ `PlayerState`의 거취.
- **수용**: 결정에 따른 배선 + 스탯이 에피소드 게이트에 반영되는 테스트(배선 선택 시).

---

## 그룹 C — 데이터화 (R1. A·B 후. 단 U12-v1과 U17은 앞당김 가능)

### U10. 커맨드 매니페스트 선언화
- 현재 200개 `AddCommandHandler` 호출 사이트가 유일한 등록 표다. 이를 선언 데이터로 승격: 권장 방식 — 브리지 메서드에 `[VnCommand("name", category, mainLaneOnly)]` 속성 부여 + 리플렉션 등록 루프(호출 사이트 제거), 그리고 에디터 메뉴에서 **매니페스트 JSON 내보내기**(이름·인자 타입·기본값·카테고리·즉시/큐잉·mainLaneOnly).
- 내보낸 매니페스트를 VnTool의 `game.definition.draft.json`(201항목)과 대조하는 검증 스크립트 포함 — 양쪽 드리프트가 CI에서 잡히게.
- **수용**: 등록 결과가 기존과 동일(248 핸들러), 매니페스트↔카탈로그 대조 0건 불일치.

### U11. 하드코딩 값 이주
- 지식 문서 §17 표 기준: 뎁스 응답 프로파일 5종 + zoom 상수 0.05 → SO(신규 `ShotResponseTuningSO`), 매크로 상수 B′층(hop/emoji/show/slot_tyrant 내부값) → SO 또는 매니페스트 확장, 옵션 스탯 표시명 6종 → SO(게임별 교체 가능), 박스 페이드 시간 → 기존 정책 DBSO 확장.
- 원칙: **기본값은 지금 값 그대로 SO 초기값으로** — 동작 불변. 코드가 게임 고유 이름·수치를 아는 지점을 0으로.
- **수용**: 스모크에서 시각적 동일(대표 에피소드), 신규 SO 미지정 시 기존 값 폴백.

### U12. 데이터 내보내기 (툴·프리뷰 연동 준비) — **v1 2026-08-04 개정**
- **U12-v1 (앞당김 — VnTool 미니 프리뷰용, 반나절 규모, W-asset-02 §6 확정 반영)**:
  ① 에디터 메뉴에서 `PortraitGeneratedDbSo.entries[]`의 각 PNG를 **규약 레이아웃으로 복사 내보내기**: `{portraitsRoot}/{characterId}/{variantKey}/{emotionKey}.png` (emotionKey 2자리). 파일·폴더명은 `PortraitKey`에서 기계적으로 조립 — 유사 이름 추측·자동 교정 금지. 규약 경로로 표현 불가한 엔트리는 건너뛰되 경고 목록으로 보고(조용히 빠뜨리지 말 것).
  ② 매니페스트 JSON은 **만들지 않는 것이 기본** — 필요 시 파일 목록 확인용 부산물로만 선택 출력(권위 아님. VnTool은 규약 스캔 1순위, 매니페스트는 구버전 수입 보조).
  ③ 배경은 내보내기 불요 — `BackgroundSpriteResolver`가 순수 문자열 규약(`Resources/Backgrounds/{key}`)이라 폴더 복사로 충분. **하위 폴더 허용, 키 = 상대경로**(예: `room/night`). 매니페스트 없음.
- U12-전체(2b 대비): DBSO 프리셋 일습(visual focus/mask motion/screen effect/depth/focus tuning/role anchor/surface layout), 리그 스키마 4종 테이블, CanvasScaler 기준 해상도. BGM **문자열 키 보존**(`BgmPlayer`에 현재 키 저장 — U15의 상태 스냅샷 전제).
- **수용**: v1 — 내보낸 PNG 폴더를 VnTool 에셋 루트에 두면 JSON 편집 없이 초상화가 해석된다. 전체 — 덤프 생성 확인, BGM 키 조회 가능.

---

## 그룹 D — 상태 모델 추출 (R2. 핵심 투자, C 후)

### U13. Ked.Presentation.Core 신설 + StageState·리듀서
- ① netstandard 클래스 라이브러리 신설, 스펙 ~98종 이동(네임스페이스 유지 — Unity asmdef 참조로 무이동 컴파일). Unity 참조 6필드는 문자열 키 필드만 Core로, 오브젝트 참조는 Unity측 partial/래퍼로 분리. ② `StageState` 정의 — **라인 경계의 확정값만**: 슬롯별 {캐스팅, 축 오프셋, 미러, 비주얼 프리셋 키, 아이들 종류}, 배경 리그별 {스프라이트 키, 변환}, 샷 intent 3필드, 스크린 이펙트 {flash/vignette/noise/blur 키·강도}, 박스 모드, BGM/보이스 키, 오버레이. 트윈 중간값 금지. ③ 리듀서 `Reduce(StageState, CommandSpecBase) → StageState` — 각 커맨드의 `CommitFinalState` 의미를 추출(발명 아닌 이동). 매크로 확장표(show→4 등)도 Core로.
- 참고: VnTool 미니 프리뷰(마스터 플랜 2a-v1)는 U13에 **의존하지 않는다** — 커맨드 텍스트 수준 폴드로 자체 해결. U13은 정지 프레임 렌더러(2b)부터의 전제.
- **수용**: Core 단위 테스트(커맨드별 리듀서 골든), Unity 빌드 통과, 기존 재생 동작 불변.

### U14. 등가성 하네스 (U13의 완료 조건이자 게이트)
- 런타임에 `StageState CaptureCurrent()` 구현(라이브 리그·시스템에서 확정 상태 스냅샷) → 대표 에피소드를 라인별로 재생하며 캡처한 상태와, 같은 라인까지 리듀서로 접은 상태를 비교하는 자동 테스트(PlayMode 또는 배치 모드).
- **수용**: 대표 에피소드 전 라인 등가. 불일치는 리듀서 수정으로 수렴(허용 오차 정책 명시 — float 좌표는 ε 비교).

---

## 그룹 E — 스냅샷 시킹 (R3. **U14 초록불 전 착수 금지**)

### U15. 세이브 v2 + 즉시 로드
- 세이브 v2 = `{schemaVersion, nodeName, lineId, 변수 스냅샷(전 $변수), StageState, 선택 기록, 기존 메타}`. v1 로드는 현행 재실행 경로 폴백(마이그레이션 불요). 로드 v2 = StageState 적용(리그 재구축·캐스팅·샷·이펙트·BGM) + 변수 복원 + **VM 무음 스프린트**(타깃까지 라인 순회하되 `PlayCollected` 배치 폐기 — 새 CommandRunScope 모드) → 타깃에서 정상 재개.
- U2의 실패 폴백은 그대로 유효(스냅샷 적용 후 스프린트 실패 시에도).
- **수용**: 긴 노드(300라인+) 로드가 체감 즉시(<0.5s), v1 세이브 호환, 로드 후 상태가 재실행 로드와 등가(U14 하네스 재사용).

### U16. 롤백 고속화
- 최근 N라인(예: 32)의 `StageState` 링버퍼를 라인 커밋 시 적재 → 롤백 = 이전 라인 State 적용 + VM 스프린트(노드 처음부터이나 배치 폐기라 저렴). U1의 재시작 경로 위에서 동작.
- **수용**: 롤백 체감 즉시, 연타 안정(U1 회귀 포함), 링버퍼 밖 롤백은 현행 경로 폴백.

---

## U17. UI 스프라이트 포트 보드 (파일 관리 에디터 툴 — 독립 트랙, 앞당김 가능)

2026-08 별도 세션에서 설계 확정. 상세 사양서는 런타임 저장소의 해당 문서가 기준이며, 여기는 확정 결정의 요약이다.

**설계 원칙: "이름이 같으면 자동으로 연결된다"는 착각을 참으로 유지한다.** 보드에 뜨는 칸은 채우면 반드시 동작해야 한다. 따라서 감사 도구가 아니라 **저작 보조 보드**(빈칸 = 할 일 목록)로 만든다.
(VnTool 쪽 W-asset-02가 같은 철학의 초상화·배경 판이다 — 규약 한 곳, 자동 추측 금지, 툴 없이도 같은 결과.)

1. **선행 — 규약 단일화**: `_Image` 접미 판정과 `ui/{theme}/{portId}` 주소 조립이 `UIBase.SpritePorts`와 `SpritePortAssignmentBuilder` 두 곳에 흩어져 있고 보드가 세 번째 사본이 될 위험 → `UISpritePortConvention` 단일 클래스로 모으고 세 소비자가 공유. (@Scripts만으로 즉시 가능)
2. **선행 — 경합 포트 3개 규약 이관**: 코드가 직접 스프라이트를 세팅하는 3곳(ChapterSelectionPanel 2, EpisodeSelectionPanel 1)의 파일을 `Resources/ui/default/{portId}`로 이동하고 코드 3줄 삭제 → 예외 0. 속성·예외 목록·테마 커버 선언은 **만들지 않는다**(예외가 없으면 표시 장치도 불요).
3. **보드**: 행 = 포트(UI 클래스별 묶음), 열 = 테마, 칸 = 썸네일/빈 슬롯. 채우기 2경로 — 드래그(툴이 `{portId}` 개명 + 복사 + TextureImporter를 Sprite로 설정) 또는 이름 맞춰 폴더에 직접 넣기(새로고침). 툴은 편의일 뿐, 툴 없이도 같은 결과여야 한다.
4. **만들지 않을 것**: 자동 일괄 채우기(유사 이름 추측 연결 — 규칙을 마법으로 바꾸므로 금지), 커버 선언 파일, `[SpritePort]` 속성.
5. **함께 잡는 검사**: 결손(포트↔파일), 고아(파일↔포트 — Resources 전량 빌드 포함 비용), 오분류(Texture2D 임포트), 바인딩 결손(Refs 항목 ↔ 씬 GameObject — `BindObjects`의 주석 처리된 경고 부활 검토), `_Image` 이중 의미 주의(비 UIBase enum의 19개 — 리그 정의 등 — 는 포트가 아님, 보드에서 제외 명시).

**수용**: 빈 슬롯을 보드에서 채우면 즉시 테마 패치에 반영, 경합 0 상태 유지, 규약 소비자 3곳이 `UISpritePortConvention` 하나만 참조.

---

## 소유자 결정 항목 (확정 상태 반영, 2026-08-04)

| # | 결정 | 관련 U | 상태 |
|---|---|---|---|
| 1 | Messenger 폴더: 삭제 vs Experimental 보존 | U7 | 미결 |
| 2 | YarnVariableBridge 배선 vs 제거 / 플래그 저장 도입 시점 | U9 | 미결 |
| 3 | 옵션 라벨 접두: 런타임 스트리핑 추가 여부 | U6 | ✅ **미도입 확정** — VnTool이 접두 없이 출력(D6). 스트리핑은 정상 라벨을 망가뜨릴 수 있는 추측 규칙이라 배제. 기존 수작업 yarn은 손으로 수정 |
| 4 | 롤백 히스토리 캡 수치 / 링버퍼 크기 | U8·U16 | 미결 |
| 5 | 세이브 v2에서 변수 "전체" 스냅샷 범위(시스템 변수 제외 규칙) | U15 | 미결 |
| 6 | 대본 화자명 ↔ 캐릭터 키 매핑의 소재 | U12-v1 | ✅ **확정·구현 완료** — `game.definition.json`의 `speakers[{name, characterId}]` (VnTool X5) |

## 추천 마일스톤

- **M1 (신뢰성)**: U1–U6 — VnTool과 병행 가능, 충돌 없음.
- **M1.5 (앞당김 트랙)**: U12-v1(규약 경로 초상화 덤프) + U17(스프라이트 포트 보드) — 독립적이라 M1과 병행 가능.
- **M2 (정리·데이터)**: U7–U11, U12-전체.
- **M3 (상태 모델)**: U13–U14 — VnTool 2b(정지 프레임 렌더러)의 기반 공사를 겸함.
- **M4 (즉시 시킹)**: U15–U16.
- F그룹(이식 경계)은 Avalonia 플레이어(2c) 착수 직전.
