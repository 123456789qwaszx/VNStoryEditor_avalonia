# Phase 2a-v1 작업 지시서 (W13–W16) — 미니 프리뷰: "무슨 배경에서 누가 말하는가"

전제 문서: `docs/vntool-master-plan.md`(4차 개정 §3 Phase 2a-v1), `docs/runtime-contract.md`, `docs/runtime-knowledge-base.md`. 충돌 시 그쪽 우선. **커밋은 W 단위.**

## 범위와 원칙

- 목표는 장면 재현이 아니라 **정보**다: 라인을 선택하면 배경 이미지, 무대 위 캐릭터(초상화), 화자 강조, 대사창 종류가 보인다. 좌표·크기·이펙트·카메라 없음 — 그건 2b(정지 프레임 렌더러, 런타임 U13–U14 전제)의 일이다.
- **StageState 리듀서(U13)에 의존하지 않는다.** 접을 것은 "마지막 값" 딕셔너리 두 개뿐이고, 폴드 입력은 툴 자신의 구조화된 연출 바인딩(커맨드명+인자)이라 Yarn 파싱도 매크로 확장표도 불요.
- **미처리 커맨드 가시화 원칙**: 인식 못 하는 커맨드를 조용히 버리지 않는다. "이 라인에 반영 안 된 연출 N개"로 항상 보이게 한다. 이 카운트가 2b 확장의 백로그다. 조용히 버리는 코드는 리뷰 반려 사유.
- 이미터·발행·계약서에 손대지 않는다(프리뷰는 읽기 전용 소비자). 유니티 실재생 게이트와 독립.

## 선행/병행 — 런타임 저장소 쪽 (별도 세션, U12-v1)

초상화 덤프가 나올 때까지 기다리지 않는다. **W13에서 덤프 JSON 스키마를 먼저 확정하고 픽스처를 만들어** VnTool 쪽을 개발한 뒤, 실제 덤프가 오면 그대로 꽂는다.

- 덤프 스키마(확정): `portraits.manifest.json` = `{ "formatVersion": 1, "entries": [{ "characterId", "variantKey", "emotionKey", "file" }] }` — `file`은 매니페스트 기준 상대 경로(PNG). 런타임 U12-v1은 `PortraitGeneratedDbSo.entries[].assetPath`에서 PNG를 복사하고 이 매니페스트를 쓴다.
- 배경은 매니페스트 없음: 폴더의 `*.png` 파일명 = spriteKey (런타임 `Resources/Backgrounds/{key}` 규약 그대로).

---

## W13. 에셋 연결 (로더·해석·매핑)

1. **프로젝트 설정에 에셋 루트 2개**: 배경 폴더, 초상화 폴더(매니페스트 포함). `project.vnproject.json`에 상대 경로로 저장(프로젝트 이동 내성). 미설정이어도 저작은 계속(프리뷰만 플레이스홀더 — "편의 기능이 저작을 막지 않는다" 원칙).
2. **로더+캐시**: 파일 변경 감지는 v1 비범위 — 명시적 "새로 고침"만. Avalonia Bitmap 캐시(키 기준), 대소문자 정확 일치(런타임 Resources 규약과 동일하게 Ordinal).
3. **초상화 키 해석 — 런타임 PortraitResolver 규칙 이식**: 키 = (characterId, variantKey 기본 `a`, emotionKey 2자리 정규화 — `"2"→"02"`), 실패 시 `(characterId, "a", "01")` 폴백, 그것도 없으면 플레이스홀더. 규칙 출처를 주석에 명기(`runtime-knowledge-base.md` §6).
4. **화자명↔캐릭터 키 매핑 — `game.definition.json`에 `speakers` 추가**: `[{ "name": "박은설", "characterId": "parkeunseol" }]`. 근거: 런타임 화자 정책 DBSO는 화자명→표시명/박스만 알고 캐릭터 키가 없어 어차피 신규 저작 데이터이며, 게임별 어휘는 정의 파일이 공급한다는 기존 원칙. 미매핑 화자는 이름만 표시(오류 아님).
5. **플레이스홀더**: 누락 배경 = 키 문자열이 적힌 회색 사각형, 누락 초상화 = 이니셜 실루엣. 어느 키가 없는지 항상 문자로 보인다.

수용: 픽스처 에셋으로 배경·초상화 로드/폴백/플레이스홀더 단위 테스트, 매니페스트 formatVersion 불일치 시 명시 오류.

## W14. 라인 폴드 (MiniStageFold)

1. **입력**: 대사 노드에 공급된 PresentationResult(§2.5 — 연출이 읽은 대사 버전 기준) + Setup 블록 + 라인 바인딩. 공급이 없으면 폴드 결과는 빈 무대(오류 아님, "연출 공급 없음" 표시).
2. **상태 모양**: `{ backgroundKey (마지막 bg_spawn/bg_sprite/bg_slot00-02의 spriteKey), slots: slotKey→{characterId, variantKey, emotionKey, visible, mirrored}, aliases: @alias→slotKey, boxKind, unhandled: [ {lineId, commandName} ] }`.
3. **인식 커맨드 ~20종** (outputCommand 기준): `bg_spawn bg_sprite bg_slot00 bg_slot01 bg_slot02 / slot slot00 slot01 slot02 slot_tyrant / cast pose face actor / show fade_in fade_out / face_swap face_crossfade mirror / box_named box_protagonist box_reset`. `slot_tyrant`는 slotKey "tyrant"에 캐스팅+표시(런타임 매크로 의미 — 지식 문서 §5). `show`는 표정 설정+visible. `face_crossfade`는 character까지 갱신 가능. `mirror` 무인자는 토글.
4. **나머지 전부 unhandled로 기록** — 커맨드명과 라인을 보존. 프리셋 참조는 해석된 outputCommand로 판정(카탈로그 경유).
5. **폴드 순서**: Setup 블록 → 라인들을 대사 결과의 문서 순서대로 선택 라인까지. **v1은 갈래 가정 없음** — 조건·선택 갈래의 라인도 문서 순서대로 전부 적용한다(단순·예측 가능한 근사). 갈래 인식 폴드는 2b에서. 이 근사가 적용된 경우("지나온 구간에 갈래 있음") 프리뷰에 표시 하나를 띄운다.
6. 순수 함수로 작성(Flow 계층, 저장 안 함 — "계산은 저장하지 않는다" 원칙). 캐시는 결과 해시 기준.

수용: 폴드 골든 테스트(blank_ch01_ep00 상당 샘플의 대표 라인들 — 배경 전환·캐스팅·fade_out 후 상태·박스 전환·unhandled 카운트), 갈래 근사 표시 테스트.

## W15. 프리뷰 패널 (UI)

1. **위치**: 연출 편집기 우측(또는 하단) 패널 — 라인 선택 시 갱신. 대사 노드 편집기에서도 같은 패널 재사용(공급된 연출 기준, 없으면 화자명만).
2. **표시**: 배경 이미지(패널 배경, letterbox 맞춤) → 그 위에 visible 슬롯의 초상화를 슬롯 키 순서로 가로 나열(좌표 재현 아님 — 나열임을 시각적으로 명확히: 균등 배치, mirrored는 좌우 반전) → **화자 초상화 강조**(테두리+이름표. 화자명은 대본에서, 매핑은 W13-4로 characterId 대조 — 무대에 없으면 이름만 상단 표시) → 대사 본문·박스 종류 뱃지 → **"반영 안 된 연출 N" 뱃지**(클릭 시 커맨드명 목록) + 갈래 근사 표시.
3. 에셋 미설정 시: 플레이스홀더 구성으로 동일 레이아웃(기능이 사라지지 않고 회색이 된다).
4. 성능: 라인 이동 시 재폴드는 선택 라인까지 증분 또는 전체(문서 수백 라인 기준 즉시면 충분 — 측정 후 결정, 조기 최적화 금지).

수용: 앱 스모크 + 샘플 프로젝트에서 라인 이동하며 배경·캐릭터·화자 강조 갱신 수동 확인. 미처리 뱃지·플레이스홀더 노출 확인.

## W16. 검증·마감

1. 기존 360 테스트 전체 통과(프리뷰는 읽기 전용 — 발행 해시·저장 형식 불변 회귀 확인).
2. 픽스처 → 실제 U12-v1 덤프 교체 리허설(런타임 덤프가 준비된 경우): 매니페스트 스키마 v1 그대로 동작.
3. ARCHITECTURE.md에 프리뷰 계층 항목 추가(폴드는 Flow의 계산, 에셋은 App 경계, "미처리 가시화" 규칙 명문화).

---

## 명시적 비범위 (2b로 이월)

좌표·크기·뎁스 배치, 카메라 intent, 스크린 이펙트, 트랜지션, 이모지, 아이들, `[adv/]` 그룹별 중간 상태(그룹 경계 표시는 가능하면 뱃지로만), 갈래 인식 폴드, 실시간 재생.

## 소유자 확인 항목

1. 런타임 저장소에 U12-v1 지시(초상화 덤프 + 위 매니페스트 스키마) — 반나절 규모, M1과 병행 가능.
2. `speakers` 매핑 초안: 현재 등장 캐릭터 6명(yoonsaea/leebyeol/parkeunseol/jineunha/moonyujeong + 주인공) 이름 확정.
3. 유니티 실재생 게이트는 여전히 열려 있음 — 2a-v1과 무관하지만 2b 착수 전엔 필수.
