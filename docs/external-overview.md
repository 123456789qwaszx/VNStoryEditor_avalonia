# VNStoryEditor_avalonia — 외부 작업자용 시스템 안내와 런타임 정합성 점검

기준일: 2026-09-02

검토 기준:

- 에디터: `VNStoryEditor_avalonia/master@56ae810`
- Unity 런타임: `ked-presentation-runtime/server_DB@d53cc2f`
- 서버: `spring-prepare/dev@2c6c1db`

이 문서는 저장소를 처음 보는 개발자가 이 도구의 책임, 데이터 흐름, 런타임 계약, 현재 차이와 다음 작업을 한 번에 이해하도록 만든 진입점이다. 세부 결정의 역사는 기존 문서가 정본이며, 이 문서는 현재 구조와 점검 결과를 요약한다.

## 1. 한 문장

**VNStoryEditor_avalonia는 기획자·작가·연출자의 서로 다른 원본을 하나의 검증된 Unity 입력물로 조립하는 Avalonia 저작 도구다.**

단순한 Yarn 편집기가 아니다. 챕터 구조, 대사, 연출을 각자의 원본에서 읽고 서로 참조가 맞는지 검증한 뒤 다음 산출물을 만든다.

- `exported/{chapter}.progression.json`: 진행 그래프, 조건, 스탯, 장면 소속
- Yarn 번들: 대사와 인라인 연출
- 튜닝 데이터: 무대·프리셋·리그 설정

핵심 원칙은 **각 값의 주인은 한 곳뿐**이라는 것이다. 같은 사실을 엑셀과 그래프 양쪽에 중복 저장하지 않는다.

## 2. 세 저장소의 책임

| 저장소 | 책임 | 이 저장소에서의 취급 |
|---|---|---|
| `VNStoryEditor_avalonia/master` | 저작 원본을 읽고 편집·검증·내보내기 | 이 문서와 후속 구현의 소유자 |
| `ked-presentation-runtime/server_DB` | progression JSON과 Yarn을 실제 게임에서 재생 | 계약의 실제 소비자. 읽어서 맞추되 수정하지 않는다 |
| `spring-prepare/dev` | 세이브 백업·동기화, 챕터 버전과 통계 | 간접 소비자. 에디터가 직접 통신하지 않는다 |

```text
엑셀·연출 그래프
    ↓
VNStoryEditor_avalonia
    ├─ progression.json ─┐
    ├─ Yarn 번들 ────────┼─→ ked-presentation-runtime
    └─ 튜닝 데이터 ──────┘             │
                                      └─ 세이브·이벤트·checksum → spring-prepare
```

서버 M8-b는 에디터가 직접 구현할 대상이 아니다. 다만 두 규칙은 에디터 산출물에서 시작한다.

- 선택지는 `OptionIndex`로 기록되므로 출시 후 `NextOptions` 순서가 바뀌면 과거 이력의 뜻이 바뀔 수 있다.
- 챕터 버전은 progression JSON의 SHA-256으로 식별되므로 동일 입력은 동일 바이트로 출력되어야 한다.

## 3. 저장소 안의 구역

| 프로젝트 | 역할 | 경계 |
|---|---|---|
| `Vn.Authoring` | 저작 도메인, 엑셀 입출력, 검증, 내보내기 | 제품 핵심. 화면 없이 테스트 가능해야 한다 |
| `Vn.App` | Avalonia 화면과 OS 연결 | 도메인 규칙을 소유하지 않는다 |
| `Vn.Core` | Yarn 구문 분석과 실제 컴파일러 기반 진단 | 저작 모델·엑셀·UI를 모른다 |
| `Vn.Cli` | `Vn.Core`의 콘솔 진입점 | 분석 자동화와 골든 검증용 |
| `Ked.Presentation.Core` | 무대 상태 계산 | 런타임이 주인인 사본 |
| `Ked.Progression` | 산출물을 실제 소비자 규칙으로 다시 읽는 검증 오라클 | 런타임 사본과 동기화되어야 한다 |

에디터의 `Ked.Progression`이 런타임보다 낡으면 “에디터에서는 통과했지만 게임은 거부하는” 틈이 생긴다.

## 4. 저작 원본과 처리 경로

| 값 | 원본 |
|---|---|
| 에피소드·간선·선택지·조건·스탯 | `chapters/{id}.xlsx` |
| 대사 본문·화자·라인 구조 | `episodes/{chapter}/{episode}.xlsx` |
| Yarn 진입 이름·Via 자유 씬·인라인 연출 | 연출 그래프 |
| 커맨드 어휘·초상화 매핑 | `game.definition.json` |
| 실제 런타임 입력 | 검증을 통과한 `exported/` 결과 |

처리 순서:

1. 워크북을 읽어 `ChapterGraphModel`을 만든다.
2. `ChapterValidator`와 도달성 증명기가 저작 규칙을 검사한다.
3. `ChapterProgressionExporter`가 progression JSON을 만든다.
4. JSON을 에디터 안의 `Ked.Progression.ProgressionLoader`에 다시 넣는다.
5. 소비자가 거부할 오류가 있으면 파일을 내지 않고 엑셀 시트와 행으로 진단을 돌려준다.
6. Yarn 번들은 실제 Yarn 컴파일러 검증을 통과해야 배달된다.

에디터는 “작가가 무엇을 잘못 적었는가”를 알고, 런타임 코어는 “이 그래프를 안전하게 실행할 수 있는가”를 안다.

## 5. 런타임이 확정한 수명 계층

| 수명 | 소유 데이터 | 경계의 의미 |
|---|---|---|
| 회차 | 백로그, 플레이 시간, 장면 이력 | 새 게임·갈라지기 |
| Chapter | 확정 진행 스탯과 Yarn 변수 | 챕터 시작 시 초기화·복원 |
| Scene | 무대, 롤백, 체크포인트, 미확정 선택 | 진입 시 기준선, 종료 시 fold와 저장 |
| Episode | 진행 커서와 시청 기록 | 저작·실행 블록이며 연출 경계가 아님 |

> 장면 안에서는 모든 게 물릴 수 있고, 장면이 끝나면 확정된다.

Scene은 그룹 색상이 아니라 캐릭터·배경의 연속 범위, 롤백 재실행 범위, 선택 확정 시점, 세이브 재개 지점을 정렬하는 **게임 상태의 트랜잭션 경계**다.

## 6. progression JSON 계약과 영향

```text
Chapter
  ChapterId, DisplayName, StartEpisodeId, Stats[], Nodes[]

Node
  EpisodeId, Title, DialogueEntryId, EventKey, SceneId, NextOptions[]

Option
  TargetEpisodeId, ChoiceLabel
  VisibleConditions[], Conditions[]
  LockedReasonText, ViaNodeId, StatChanges[], Auto

StatChange
  Key, Amount, Op("Add" | "Set")
```

### 6.1 SceneId

여러 Episode가 같은 연출·롤백·커밋·저장 범위임을 표현한다. 비어 있으면 런타임은 `__scene_{EpisodeId}`를 발급해 기존 콘텐츠를 “에피소드 하나 = 장면 하나”로 실행한다.

불변식:

- 한 Scene에 외부에서 착지하는 Episode는 하나뿐이다.
- 그 Episode가 Scene root이며 롤백과 이어하기의 시작점이다.
- Scene을 나갔다 root로 다시 들어오는 재진입은 허용된다.

에디터에는 워크북 저작 자리, 마이그레이션, 그래프의 장면 묶음과 root 표시, 중간 Episode 착지 진단, JSON 출력이 필요하다.

### 6.2 Auto

문구 공백으로 자동 진행을 추론하면 실수와 의도를 구분할 수 없다. 런타임은 명시적 `Auto: true`를 요구한다.

자동 간선은 해당 Episode의 유일한 간선이며, 조건과 스탯 변화가 없고, 같은 Scene 안으로 이동해야 한다. 플레이어에게 보이는 문구도 없다. 에디터 모델·워크북·UI·검증·Exporter 모두 이 명시값을 알아야 한다.

### 6.3 EventKey

Episode 시청 완료를 이벤트·보상·통계로 보고하는 패스스루 식별자다. 에디터는 이미 `이벤트키` 열에서 출력한다. 출시 뒤 변경하면 같은 이벤트가 다른 것으로 집계될 수 있으므로 안정적인 ID로 취급해야 한다.

### 6.4 DialogueEntryId와 ViaNodeId

`DialogueEntryId`는 Episode 본문 Yarn node, `ViaNodeId`는 선택 후 도착 Episode 전에 거치는 자유 Yarn node다. 이름의 주인은 연출 그래프다. 엑셀에 중복 소유시키면 node 개명 때 JSON과 Yarn이 갈라진다. 산출 검증은 대상 Yarn node의 실재까지 확인해야 한다.

### 6.5 진행 스탯과 Yarn 변수

- 진행 스탯: progression JSON의 `Stats`, 간선 조건과 `StatChanges`만 사용
- Yarn 변수: 대사·연출의 `declare/set/if`에 사용하며 챕터 수명으로 저장

런타임은 진행 스탯을 Yarn에 투영하지 않는다. 대사에서 `$스탯`을 읽으면 도달성 증명 밖에 숨은 분기가 생기므로 금지하고, 스탯 분기는 챕터 그래프 간선으로 올려야 한다. Bool은 `Equal 0/1`과 `Set 0/1`, Number는 `Add`만 허용한다.

### 6.6 선택지 순서

간선 시트 행 순서는 화면 순서이자 서버 이력의 `OptionIndex`다. 그대로 보존해야 하며, 출시된 선택지 위에 행을 삽입하거나 재정렬하는 작업에는 경고가 필요하다.

### 6.7 결정적 출력

런타임은 progression JSON의 SHA-256을 서버 챕터 버전과 대조한다. 공백·순서가 달라도 checksum은 달라진다. 동일 원본의 반복 export는 컬렉션 순서, 개행, 들여쓰기, 인코딩까지 같은 바이트를 내야 한다.

## 7. 자체 점검 결과

### 정상 또는 방향이 맞는 부분

| 항목 | 상태 |
|---|---|
| `Stats[]`, 저작 `Int` → 런타임 `Number` 번역 | 정상 |
| 조건 연산자 이름 번역 | 정상 |
| `StatChange.Op`의 `Add`·Bool용 `Set` | 정상 |
| `EventKey` 출력 | 정상 |
| 연출 그래프를 원본으로 하는 `ViaNodeId` | 정상 |
| 간선 `SourceRow` 순서 보존 | 정상 |
| 산출 JSON을 소비자 로더로 재검증하는 구조 | 좋은 구조 |
| 대사·연출을 합친 Yarn 단일 번들 | 런타임과 일치 |

### 닫아야 할 차이

| 우선순위 | 차이 | 현재 영향 | 필요한 조치 |
|---|---|---|---|
| P0 | 에디터의 `Ked.Progression`이 런타임보다 낡음 | 에디터 통과 후 게임 로드 실패 가능 | 런타임 사본을 기준으로 통째 재반입 |
| P0 | Exporter가 `SceneId`를 내지 않음 | 모든 Episode가 독립 Scene으로 퇴화 | 모델·워크북·Exporter·검증·UI 추가 |
| P0 | Exporter가 `Auto`를 내지 않음 | 자동 간선이 런타임에서 거부 | 명시적 Auto 모델과 출력 추가 |
| P1 | 결정적 바이트 출력 미검증 | 같은 콘텐츠가 다른 checksum 가능 | 반복 export 바이트 동일성 테스트 |
| P1 | 선택지 순서 변경 경고 없음 | 기존 OptionIndex의 의미 변경 | 출시 기준선 또는 경고 설계 |
| P1 | 선택 가능 항목 0개일 때 프리뷰와 런타임이 다름 | 프리뷰는 대기, 런타임은 종료 | 런타임 판정과 일치 |
| P1 | Yarn에서 진행 스탯을 읽지 않는 회귀 테스트 부족 | 숨은 분기 재발 가능 | 산출 Yarn 검증 추가 |
| P2 | 일부 문서가 폐기 DTO와 옛 저장소 구도를 설명 | 외부 작업자가 낡은 계약을 따를 수 있음 | 정본·역사 문서 구분 |

지금 하지 않아도 되는 것: 서버 M8-b 구현, 런타임 세이브 UI, 장면 중간 저장, 전역 앨범, Scene 사이 무대 승계, 다른 두 저장소의 코드 수정.

## 8. 권장 작업 순서

세부 실행 계획의 정본은 [`docs/plans/PLAN.md`](plans/PLAN.md)이며, 각 단계는 R0~R5 문서로 나뉜다.

1. **소비자 오라클 동기화**  
   런타임 `Assets/Scripts/Ked.Progression`을 기준으로 에디터 사본을 통째 재반입하고 레거시 타입·필드를 정리한다.

2. **Scene 저작 계약**  
   SceneId를 워크북·모델·그래프·Exporter에 추가하고 런타임의 장면 진입점 불변식으로 검증한다.

3. **명시적 Auto 간선**  
   Auto를 워크북·모델·UI·Exporter에 추가하고 네 불변식을 사용자 진단으로 설명한다.

4. **장기 호환성 방어**  
   결정적 export, 순서 변경 경고, 프리뷰 종료 판정, Yarn 스탯 격리 테스트를 추가한다.

오라클 동기화가 먼저인 이유는 이후 Scene과 Auto를 잘못 구현해도 최종 소비자가 즉시 거부하도록 만들기 위해서다.

## 9. 변경 전 체크리스트

- 이 값의 원본이 엑셀인지 연출 그래프인지 확인했는가?
- 변경이 progression JSON, Yarn, 튜닝 중 어디에 영향을 주는가?
- 런타임 `ProgressionLoader`도 통과하는가?
- Scene root와 진입점 하나 규칙을 깨지 않는가?
- Auto 간선의 규칙을 모두 만족하는가?
- `NextOptions` 순서를 바꾸어 기존 `OptionIndex`의 뜻을 바꾸지 않는가?
- 같은 입력을 두 번 export했을 때 바이트가 같은가?
- 진행 스탯을 Yarn 변수로 읽는 숨은 분기를 만들지 않았는가?
- 런타임이나 서버 수정이라면 해당 저장소 담당자에게 넘겼는가?

## 10. 문서 읽는 순서

| 목적 | 문서 |
|---|---|
| 처음 구조 이해 | 이 문서 |
| 현재 작업 상태와 최신 계약 | `docs/handoff/current-state.md` |
| 코드 위치 | `ARCHITECTURE.md`, `docs/project-boundaries.md` |
| 런타임 상세 계약 | `docs/runtime-contract.md` |
| 저장소 경계 | `docs/three-repo-map.md` |
| 입출력 형식 | `docs/handoff/io-reference.md` |
| 결정 이유와 이력 | `docs/run-log.md`, `docs/handoff/architecture-decisions.md` |
| 사용자 안내 | `docs/writer-guide.md`, `docs/chapter-layer-guide.md` |

## 11. 유지 규칙

- 이 문서는 외부 진입점과 점검표다. 세부 규격은 기존 정본에 둔다.
- 런타임 DTO나 불변식이 바뀌면 런타임 코드를 먼저 확인하고 계약 요약과 점검표를 갱신한다.
- 완료 표시는 테스트 또는 실제 소비자 로더 통과 근거가 있을 때만 붙인다.
- 과거 결정은 `run-log.md`에 남기고 이 문서는 현재 사실만 유지한다.
