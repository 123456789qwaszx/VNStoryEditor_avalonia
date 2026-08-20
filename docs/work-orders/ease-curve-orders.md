# 커스텀 이징 곡선을 여는 길 — 작업 지시 · 런타임 개통 요청 2

2026-08-20 · 소유자 결정 · **[`ease-open-orders.md`](ease-open-orders.md)의 후속**

수신: `ked-presentation-runtime`. 소유자 결정: *"이징이라고 하지만 사실 그냥 각자 그래프
곡선이다. 마야 그래프 에디터처럼 키를 주면서 이징 곡선을 커스텀하고 싶다. 이징 선택기를
기본으로 두되, 선택한 이징에서 출발해 그래프 에디터에서 키를 줘 곡선을 조절한다."*

MG4(모션 그래프 상세 규격)의 `@curve` 탈출구가 "증거가 쌓이면"에서 **소유자 직접 결정**으로
당겨졌다. 툴이 곡선 편집기를 짓기 전에 **계약 셋**이 그쪽에서 서야 한다 — 지난번과 같은
이유다: 프리뷰가 그리는 모양과 유니티가 재생하는 모양이 같은 함수에서 나와야 한다.

---

## 1. 설계 요지

- **텍스트 표현**: 다섯째 인자에 `@이름` — `<<move_by c1 +2u 0u 12fr @hop_snappy>>`.
  `@` 접두 = 커스텀 곡선 참조, 접두 없음 = 기존 EaseKind 이름. 왕복·생략 규칙은 기존
  다섯째 인자와 같다.
- **곡선 데이터**: 키프레임 목록 — **Unity AnimationCurve와 같은 Hermite 모델**
  (키마다 `time · value · inTangent · outTangent`). 이 모델을 고른 이유: 유니티 쪽
  대조 상대(AnimationCurve.Evaluate)가 이미 있고, 마야식 키+탄젠트 편집과 1:1이다.
- **평가기는 코어에**: `CurveFunctions.Evaluate(keys, t)` — 순수 Hermite 보간,
  UnityEngine 타입 금지, `EaseFunctions`와 같은 자리(`Ease/`). **양쪽이 이 함수
  하나를 쓴다**: 툴 프리뷰·스크럽도, 그쪽 트윈도. DOTween은 커스텀 이즈 델리게이트를
  받으므로(`SetEase(EaseFunction)`) 호스트는 `t => CurveFunctions.Evaluate(keys, t/d)`를
  넘기면 된다 — 트윈 경로 구조는 그대로다.
- **데이터 파일**: 번들 옆 `curves.json` — `{ "curves": { "이름": { "keys": [ {t,v,inTangent,outTangent}, … ] } } }`
  (스키마 상세는 §3). 저작 쪽 소유는 프로젝트(작가 자산)이고, 내보내기가 이 파일을 함께 낸다.

## 2. 부탁 ① — 코어 `CurveFunctions`

`Ease/CurveFunctions.cs` — 형태 규약은 `EaseFunctions`와 같다(순수·UnityEngine 금지).

- `Evaluate(CurveKey[] keys, float t)` — Unity AnimationCurve와 같은 Hermite 보간:
  구간 양끝 키의 value·tangent로 3차 보간, `t`가 첫 키 이전/마지막 키 이후면 끝값 클램프.
- `CurveKey` — `time · value · inTangent · outTangent` (float 4개짜리 순수 구조체).
- **등가의 심판은 그쪽 EditMode 테스트**: `CurveFunctions` ↔ `AnimationCurve.Evaluate`
  (같은 키로 만든 커브) 대표 곡선 몇 개 × 257샘플 < 1e-4. 이쪽은 대표 커브 픽스처로
  사본 낡음만 지킨다 — ease-golden과 같은 이중 구조다.
- 유의: AnimationCurve의 `weightedMode`·`inWeight/outWeight`는 **비범위**다(기본
  가중치만). 마야식 자유 가중 탄젠트가 필요해지면 그때 확장한다 — 지금 넣으면
  에디터·직렬화·평가가 전부 무거워진다.

## 3. 부탁 ② — `@` 접두 분기와 `curves.json` 로더

- 다섯째 토큰이 `@`로 시작하면: 커브 저장소에서 이름 조회 → 있으면
  `SetEase(t => CurveFunctions.Evaluate(keys, t / duration))` → 없으면 **로그 + OutCubic**
  (모르는 이징 이름과 같은 처리 — 1차 방어는 이쪽 저작 검증이다).
- `curves.json` 로더 — 번들(대본) 옆에서 읽는다. 스키마:

  ```json
  {
    "schema": "ease-curves/1",
    "curves": {
      "hop_snappy": {
        "keys": [
          { "t": 0.0, "v": 0.0, "inTangent": 0.0, "outTangent": 2.6 },
          { "t": 0.4, "v": 0.9, "inTangent": 0.8, "outTangent": 0.3 },
          { "t": 1.0, "v": 1.0, "inTangent": 0.1, "outTangent": 0.0 }
        ]
      }
    }
  }
  ```

  이름 규칙: `[a-z0-9_]+` (커맨드 토큰에 실리므로 공백·특수문자 금지). 키는 t 오름차순,
  첫 키 t=0 · 마지막 키 t=1을 로더가 검증(어긋나면 로그 + 그 커브 무시).
- 파일이 없으면 커브 0개로 조용히 동작한다(커브를 안 쓰는 프로젝트가 정상 경로다).

## 4. 이쪽(VnTool)이 지을 것 — 참고

곡선 그래프 에디터(선택 이징을 키로 베이크해 시작 → 키 추가/드래그/탄젠트 조절 →
프로젝트에 저장 → 커맨드 인자 `@이름`), 내보내기의 `curves.json` 동반 출력, 저작 검증
(`@이름`이 프로젝트에 없으면 오류 — "채우면 반드시 동작한다"). 프리뷰·재생·스크럽은
반입된 `CurveFunctions`를 그대로 쓴다.

## 5. 수용 기준

1. `CurveFunctions` ↔ `AnimationCurve.Evaluate` 등가 EditMode 테스트 (< 1e-4).
2. `@이름` 재생: 대표 커브 하나가 실제로 그 모양으로 움직인다 (EditMode 또는 육안 1회).
3. 모르는 `@이름` → 로그 + OutCubic, 크래시 없음.
4. `curves.json` 없는 기존 프로젝트: 동작 불변.
5. 스키마 §3 확정 회신 (수정이 있으면 수정본으로 — 이쪽 에디터가 이 스키마로 저장한다).

## 6. 하지 않는 것

- 가중 탄젠트(weightedMode) · 커브의 커브(중첩 참조) · EaseKind 재정의.
- 커브를 game.definition으로 — 커브는 작가 자산이라 프로젝트에 산다(2.4b: 정의 파일은
  기획자 전용).
