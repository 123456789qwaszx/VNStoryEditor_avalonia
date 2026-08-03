# VnTool 마스터 플랜

2026-08-03 확정. Claude Code 작업 세션의 최상위 기준 문서. 세부 런타임 규약은 `runtime-contract.md`(이하 계약서)가, 코드 구조 원칙은 저장소의 ARCHITECTURE.md가 기준이며, 충돌 시 그쪽이 우선한다.

## 0. 비전과 전제

- **목적**: 10개 이상의 게임을 수년간 만들 공통 저작 기반. 특정 게임 비종속 — 게임별 어휘(변수·커맨드·프리셋)는 전부 `game.definition.json`이 공급한다.
- **진실은 순수 텍스트다.** 모든 저작 결과는 최종적으로 .yarn 텍스트(및 CSV 등)로 산출되고, ked-presentation-runtime이 그것을 재생한다. 런타임은 95% 완성·검증 상태이고 데이터 형태는 바뀌지 않는다.
- **런타임은 의도적으로 순수 UI로 설계되었다.** 카메라조차 리그 이동으로 시뮬레이트(PresentationShotResponseSystem, ~200줄의 순수 대수)하며, 롤백·빨리감기·시킹의 상태기계는 유저 기능이기 이전에 **연출 작업용**이다. 장기적으로 툴 안에서 실시간 재생을 목표로 한다 (Phase 2).
- **작업 흐름** (소유자 정의): [1] 외부 대본 가져오기(LineId 부여) 또는 툴 내 직접 작성 → [2] 조건·선택지·스탯 변경·이벤트 반영 후 편집본 발행 → [4] 연출(보조 AI 포함) 일괄 세팅으로 합본 생성 → [5] 유니티에서 확인. 합본 출력은 다형식(§2.5).

## 1. 구조 판정 — 재작성하지 않는다

전면 재작성 옵션을 검토했고, **기각한다**. 근거:

1. **현행 모델이 런타임의 가장 무거운 요구와 정확히 맞물린다.** 세이브 호환의 핵심이 "LineId와 노드 타이틀의 영구 안정성"(계약서 C1·C2)인데, ScriptDocument의 LineId 정체성 모델·불변 DialogueResult·은퇴 정책이 바로 그 보장 장치다. 이건 우연이 아니라 같은 문제(정체성 vs 본문)를 양쪽에서 푼 결과다.
2. **확장 지점이 이미 설계에 있다.** 연출 공급 노드(§2.1)는 ARCHITECTURE §5.2의 "capability node 추가" 레시피 그대로이고, 선택지·변수·이벤트의 자리는 `DialogueLineExtension`에 예약되어 있다.
3. **Vn.Core(Yarn 컴파일·분석 엔진, 1년치)가 출력 검증기로 재활용된다.** 이미터 산출물을 실제 컴파일해 회귀를 잡는다.
4. 다시 쓰면 잃는 것: 270여 개의 테스트, 검증된 재동기화, 불변 발행 파이프라인. 다시 써서 얻는 것: 없음 — 고칠 곳은 국소적이다(카탈로그 enum → 데이터, 무순서 인자 → 순서 배열, 파일 내보내기 부재).

## 2. 도메인 확장 설계

### 2.1 연출 공급 노드 (CommandSupplyNode) — 신규 노드 종류

소유자 요구: 커맨드 전체를 평평한 드롭다운으로 주지 않고, CommandBridge의 역할 구분을 살려 **캐릭터 연출 노드·카메라 노드·스크린 이펙트 노드 등을 PresentationNode에 연결해야** 해당 커맨드군이 드롭다운에 나타난다.

- **모델**: `CommandSupplyNode : StoryNode` — 필드: 공급 카테고리 집합(카탈로그의 category id들, 예: 카메라 노드 = `{shot, transition}`), **커맨드 프리셋 목록**(§2.2). 노드 하나가 여러 카테고리를 공급할 수 있고, 어떤 카테고리 묶음을 "캐릭터 연출 노드"라 부를지는 데이터다(코드에 박지 않는다).
- **연결**: `NodeLinkKind.CommandSupply` 추가. SetNode→DialogueNode의 조건 공급과 동형.
- **해석**: `AvailablePresentationCommandResolver` — `AvailableConditionResolver`와 같은 모양. PresentationNode에 연결된 공급 노드들의 카테고리 합집합 + 프리셋들이 편집기 드롭다운 후보. **공급 노드가 하나도 없으면 게임 정의의 전체 카탈로그로 폴백**(저작을 막지 않는다는 기존 원칙).
- **구현 순서**: ARCHITECTURE §5.2 capability node 레시피 그대로 — StoryNode 파생 → NodeLinkKind → Flow 해석기 → 직렬화 → GraphProjectionBuilder(공급 포트·간선) → 편집기.

### 2.2 커맨드 프리셋 (ConfiguredCommand) — 하드코딩 기본값의 이주지

CommandBridge에는 Yarn 인자로 노출되지 않는 하드코딩 값이 많다(hop의 height=22 등, 계약서 E3의 b′층). 소유자 방향: 이런 기본값을 **툴에서 편집해 "정확한 연출종류/커맨드명령"으로 정의**하고, PresentationNode는 값이 세팅된 커맨드를 그대로 쓴다 → `<<place_br @2 bust -1>>` 같은 완성 텍스트로 파싱된다.

- **모델**: `CommandPreset { PresetId, DisplayName, CommandDefinitionId(카탈로그 참조), ArgumentValues(파라미터 순서 기준), Note }` — **CommandSupplyNode가 소유**한다 (조건이 SetNode에서 태어나듯, 프리셋은 공급 노드에서 태어난다).
- **사용**: PresentationNode의 라인 바인딩은 (a) 카탈로그 커맨드 + 직접 인자, 또는 (b) 프리셋 참조 + 필요 시 일부 인자 오버라이드.
- **발행 시 값으로 동결**: PresentationResult에는 프리셋 참조가 아니라 **해석된 최종 인자 값**을 얼린다. 프리셋을 나중에 고쳐도 발행된 결과는 불변(기존 원칙 그대로).
- **출력**: 카탈로그의 parameters 순서대로 포지셔널 조립. 트레일링 기본값 생략 가능하되 "뒤쪽부터만" 규칙 준수. 기본값 의존을 없애려면 전 인자 명시 출력 옵션도 둔다(계약서 E3 — 명시 출력이면 브리지 기본값 층을 완전 우회).

### 2.3 대사 논리 확장 (DialogueLineExtension)

- **Phase 0**: `SetOperations [{variable, operator(=|+=|-=), value}]` → `<<set $x = $x + n>>`. 후보는 게임 정의의 variables.
- **Phase 1**: `ChoiceBlock` — 라인 뒤에 붙는 선택지 블록. 옵션마다 {라벨, SetOperations, 표시용 스탯 미리보기(자동 생성: `#key:+n`, 소문자·정수 — 계약서 D5), 출구(옵션 본문 or jump)}. **순서 안정성은 세이브 계약**(계약서 C3) — 옵션에도 안정 Id를 부여하고 순서 변경 시 경고, 출시 태그 이후 삽입은 맨 뒤만 권고.
- 이벤트 발생은 커맨드(`seq` 등) 또는 set으로 표현 가능한지 게임별 정의에 따름 — 전용 필드는 필요해질 때.

### 2.4 PresentationNode 확장

- **Setup 블록**: LineId 없는 노드 수준 커맨드 목록 → Set_ 노드 본문. (슬롯·캐스팅·배경 스폰·리셋)
- **라인 바인딩**: 기존 LineId별 커맨드 목록 + 프리셋 참조(§2.2) + 라인 메타 토글(`#main_free` — 계약서 B1).

### 2.5 출력 — 소유자의 4형식 매핑과 파일 내보내기

| 소유자 정의 | 현행 프리셋 | 파일 형식 | 상태 |
|---|---|---|---|
| 1) Runtime Full (유니티 재생용 완본) | Runtime Full | **.yarn 트리오** (Story/Set/Pres) | 프리셋 있음 · 파일 출력 신규(P0-D) |
| 2) LineId+대본 (번역·녹음) | Recording Script / Localization Script | .txt 또는 .csv | 프리셋 있음 · 파일 출력 신규 |
| 3) 대본+조건·선택지·변수 (기획 검수) | Scenario Only 확장 | **.csv** (엑셀용) | 프리셋 확장 + 파일 출력 신규 |
| 4) LineId+연출 테이블 | Direction Sheet | .csv | 프리셋 있음 · 파일 출력 신규 |

원칙: 다섯 프리셋은 전부 같은 `ResultDocumentComposer` 산출물에서 나온다(기존 구조 유지). 파일 라이터만 형식별로 추가.

## 3. 로드맵

### Phase 0 — 파이프라인 관통 (이번 주 프로토타입)

목표: **대본 가져오기 → 조건·set 편집 → 발행 → 합성 → .yarn 트리오 파일 → 런타임 재생 성공.** 데모 시나리오: 대사 문구 수정 → v2 발행 → 재익스포트 → Story/Pres 사본 자동 일치 + `#line:` 태그 불변(세이브 유지).

작업 순서 (각각 독립 커밋 단위, 상세 수용 기준은 §4):

- **W1. 카탈로그 데이터 주도화** — PresentationCategory enum 제거 → 문자열 카테고리 + `presentationCommandCategories`, DefaultArguments → 순서 있는 `parameters`. 검증 완료된 `game.definition.draft.json`(201 커맨드)이 목표 스키마이자 기본 데이터.
- **W2. SetOperations** — DialogueLineExtension 확장, ARCHITECTURE §5.2의 8단계 순서 준수(결과 스키마 버전 +1).
- **W3. PresentationNode Setup 블록** — §2.4 (결과 스키마 버전 +1).
- **W4. YarnBundleEmitter** — 계약서 A·B·C·D 규칙 전부 반영: `#line:` 태그(C1), jump 전 `pres_end`(A5), 노드별 `pres_start`(A5), set은 Story만(D2), if 구조 Pres 복제(D3), 선언 출력(D4), 메인 전용 커맨드 Pres/Set 금지 검증(E2), Set 노드 커맨드 전용(A2), `[adv/]` 발견 시 발행 검증 경고(B). 결정적 출력(UTF-8 no BOM, LF, 임시 파일 교체). YarnPreviewFormatter와 조립 코드 공유.
- **W5. 검증 체계** — 골든 3파일 비교(참고본: samples/Runtime의 blank_ch01_ep00 트리오), 수정→재발행→재출력 왕복 테스트, **Vn.Core YarnCompilerAdapter로 산출물 실컴파일**(문법·라인 ID 유일성 회귀).
- **W6. UI 최소 연결** — 내보내기 버튼(합성 선택 → 폴더에 3파일), 라인 카드 set 편집, PresentationNodeEditor Setup 탭.

### Phase 1 — 저작 완성

- 연출 공급 노드 + 커맨드 프리셋 (§2.1–2.2).
- 선택지 (§2.3 ChoiceBlock, 계약서 C3·D5·D6·D7 준수).
- CSV 내보내기 3종 (§2.5의 2·3·4).
- 3분할 레이아웃 정리(좌: 편집 자료, 중: 그래프+필터, 우: 노드 편집) — 기존 목표 UI.
- `[adv/]` 인라인 동기화 지원(라인 예산 반영 — 계약서 B).

### Phase 2 — 툴 내 실시간 플레이어

런타임 분석 결과, 포팅 경계는 깨끗하다: 순수 수학/상태 ~20%(카메라 시뮬 전체가 ~200줄 대수, 리그 스키마는 선언적 테이블), 얇은 Unity 래퍼 ~55%(Avalonia Canvas+Transform+트윈 티커 300–500줄로 대체), 깊은 Unity 의존 ~15%(뎁스 블러·마스크 메시·셰이더 — 근사 대체). 접근:

1. `RuntimeComposition` 위에 재생기 골격(ARCHITECTURE §5.2 레시피 그대로 — 조건 평가만 주입).
2. 2D 씬그래프 + 트윈 티커 + 리그 스키마 테이블 전사(CharacterRigDefinition 등은 정적 테이블이라 JSON 전사 가능).
3. 카메라 시뮬(PresentationResponseMath 등) 이식 — 순수 함수.
4. **선행 요구(런타임 쪽 데이터화)**: 뎁스 응답 프로파일과 zoom 상수 0.05가 C# 하드코딩 → SO/JSON으로 승격, PortraitGeneratedDbSo의 assetPath 내보내기, CanvasScaler 기준 해상도 명시. (계약서 F5)
5. 장기적으로는 Yarn 텍스트가 아니라 **CommandSpec JSON을 직접 타깃** 가능 — 런타임의 스펙은 이미 직렬화 가능한 POCO이고 `CommandExecutor.PlaySpecs`는 Yarn과 무관한 진입점이다. SequenceSpecSO(nodes→steps→specs+gate)가 자연스러운 프로그램 형식.

### Phase 3 — 연출 보조 AI

카탈로그(파라미터·함정 노트) + YarnCommandBridge_Reference의 작성 지침(23장) + 축적된 프리셋/기존 Pres 파일들이 컨텍스트. 대사 결과에 라인별 초기 연출을 일괄 생성 → 사람이 다듬는 흐름. 데이터 준비는 Phase 0–1에서 자연히 끝난다.

### 명시적 비범위(전 Phase 공통 유예)

중첩 조건·else, .yarn 역파싱(Vn.Import는 별도 트랙), 실시간 다인 협업.

## 4. Claude Code 작업 지시 요령

- 착수 지시: "docs/의 master-plan과 runtime-contract를 읽고 W1부터. 커밋은 W 단위."
- 각 W의 수용 기준: **W1** 기존 3 카테고리 프로젝트가 새 스키마로 열리고 기본 카탈로그가 draft.json으로 대체됨, 전체 테스트 통과. **W2** set이 발행 결과에 실리고 해시 변경·스키마 버전 상승 확인 테스트. **W3** Setup 커맨드가 결과에 실림. **W4** blank_ch01_ep00 상당 샘플에서 트리오 3파일 생성, 계약 규칙 위반 0. **W5** 실컴파일 통과 + 왕복 테스트 + 골든. **W6** 수동 확인.
- **계약서의 ⚠ 항목(A5·A6·C1·C3·D6)은 코드 리뷰 시 반드시 재확인** — 어기면 컴파일은 되고 런타임에서만 깨진다.
- 테스트 우선 순서: 이미터는 골든부터 쓰고 구현(기대 출력이 곧 스펙).

## 5. Repo 준비 체크리스트 (Claude Code 착수 전, 소유자 작업)

1. `docs/` 에 커밋: `vntool-master-plan.md`(이 문서), `runtime-contract.md`, `YarnCommandBridge_Reference.md`.
2. `samples/Runtime/` 에 커밋: `Story_blank_ch01_ep00.yarn`, `Pres_blank_ch01_ep00.yarn`(Set 포함), `options.yarn` — 골든 참고본.
3. `game.definition.draft.json` 커밋 (코드 검증 완료본).
4. (선택) 런타임 `@Scripts/Commands/CommandBridge/` 사본 또는 등록 테이블 덤프 — 카탈로그 재검증용.
5. 계약서 F절의 확인 항목 5건에 답 준비 (특히 씬의 변수 저장소 배선, 옵션 라벨 접두).
