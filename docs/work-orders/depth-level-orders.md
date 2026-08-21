# 뎁스 레벨을 폴드로 여는 길 — 작업 지시 · 코어 개통 요청

2026-08-21 · 소유자 의도 · **VnTool 뎁스 편집(작업대 레벨 슬라이더)의 선행물**

수신: `ked-presentation-runtime`. VnTool 작업대에서 `size`의 depth를 **-10~10 레벨
슬라이더**로 직접 끌 수 있게 했다(소유자 지시 2026-08-21). 그런데 **프리뷰가 그 결과를
그리지 못한다** — 코어 리듀서가 프리셋 키만 알고 숫자 레벨은 거부하기 때문이다.
재생·발행은 정상이므로 이것은 재현의 구멍이고, 무대에 "반영 안 됨"으로 선다.

> 이 문서는 그쪽 실코드를 읽고 썼다. 아래 §1의 파일·줄이 근거다.
> 새로 만들 것은 거의 없다 — **필요한 데이터도 계산기도 이미 그쪽에 다 있다.**

---

## 1. 확인한 현재 모습

| 사실 | 근거 |
|---|---|
| 브리지는 depth 인자가 **숫자로 파싱되면 레벨 경로**, 아니면 프리셋 경로다 | `YarnBridge/CommandBridge.CharRigFocus.cs:162` — `TryParseDepthLevel` → `spec.useLevel = true` |
| 레벨은 **임의 실수**다. NaN·무한대만 거른다 (범위 제한 없음) | `PresentationCore/Parser/CharacterDepthPresetParser.cs:5` |
| 레벨 → 값은 두 커브다: `yCurve`·`scaleCurve`. 설계 구간 0~10, **밖은 외삽** | `CharacterRig/Depth/CharacterDepthTuningSO.cs` — `CharacterDepthLevelTuningSet.Resolve` |
| 외삽은 **끝 두 키의 할선(secant) 기울기**로 직선 연장이다 (탄젠트가 아니다) | 같은 파일 `EvaluateUnclamped` + `CalculateSlope` |
| 구간 안은 그냥 `AnimationCurve.Evaluate` | 같은 함수의 마지막 줄 |
| **레벨 경로의 `preserveFocus`는 사장 데이터다** — `ResolveRawDepth`가 읽은 직후 커맨드 인자로 덮어쓴다 | `CharacterRig/Depth/CharacterDepthResolver.cs:38-43` |
| 커브는 **이미 덤프에 실려 나온다**: `presets/depth.json`의 `level.yCurve`·`level.scaleCurve` | `ExportedTuning/presets/depth.json:93` (`m_Curve[]` — time·value·inSlope·outSlope) |
| 코어는 그 필드를 **읽지 않기로** 돼 있다 | `Ked.Presentation.Core/Tuning/PresetTuningDtos.cs:24` — "level(AnimationCurve)은 담지 않는다 — 커브 폴드는 미지원이다" |
| 그래서 숫자 레벨 커맨드는 폴드에서 거부된다 | `Reduce/FocusStageReductions.cs:179` — `"depth 프리셋 '5'를 모른다 (레벨 수치는 커브 폴드 미지원)"` |
| **실제 원고가 이미 숫자 레벨을 쓴다** — 새 기능만의 문제가 아니다 | `PresetTuningDtos.cs:25` 주석 — "실제 원문이 숫자 레벨을 쓰므로(size c1 5 등) 그 커맨드는 Unhandled로 남는다" |
| 평가기는 이미 코어에 있다 — `CurveFunctions.Evaluate`(AnimationCurve 등가, 골든 고정) | `Ked.Presentation.Core/Ease/CurveFunctions.cs:42` |

마지막 두 줄이 이 요청의 전부다: **데이터도 계산기도 있는데 배선만 없다.**

---

## 2. 부탁 ① — DTO가 level 커브를 읽는다

`DepthTuningBodyDto`에 `level`을 더한다. 덤프 형식 그대로:

```
level: { yCurve: { m_Curve: [ { time, value, inSlope, outSlope }, … ] },
         scaleCurve: { … 같은 모양 … } }
```

- `tangentMode`·`weightedMode`·`inWeight`·`outWeight`는 **읽지 않는다** —
  코어 `CurveKey`는 (Time·Value·InTangent·OutTangent)이고 가중 탄젠트는 비범위다
  (`CurveFunctions` 주석의 그 규약 그대로).
- `m_PreInfinity`·`m_PostInfinity`도 **읽지 않는다** — 런타임이 WrapMode를 쓰지 않고
  `EvaluateUnclamped`로 직접 외삽하기 때문이다. 읽어서 흉내 내면 재생과 갈라진다.
- 키가 없거나(`m_Curve` 빈 배열) `level` 자체가 없으면 **레벨 폴드는 그대로 미지원**
  으로 두고 지금과 같은 사유로 거부한다(조용히 0으로 떨어뜨리지 않는다).

## 3. 부탁 ② — 숫자 레벨 분기

`SetDepthStageReduction.TryReduce`(`Reduce/FocusStageReductions.cs`)에서
`depthPresetKey`가 **숫자로 파싱되면** 프리셋 조회 대신 커브로 푼다.
판정은 브리지와 같은 함수 의미론이어야 한다(`TryParseDepthLevel` — NaN·무한대만 거부).

구하는 값은 **둘뿐**이다. 나머지 경로(포커스 보존 보정·체인 계산)는 지금 것을 그대로 탄다:

```
depthY     = (0, EvaluateUnclamped(yCurve, level))
depthScale = max(0.0001, EvaluateUnclamped(scaleCurve, level))
```

`EvaluateUnclamped`의 코어 대응(그쪽 `CharacterDepthTuningSO`의 그 함수와 같은 규칙):

| 조건 | 값 |
|---|---|
| 키 0개 | `0` |
| 키 1개 | `keys[0].value` |
| `t < first.time` | `first.value + slope(keys[0], keys[1]) * (t - first.time)` |
| `t > last.time` | `last.value + slope(keys[n-2], keys[n-1]) * (t - last.time)` |
| 그 사이 | `CurveFunctions.Evaluate(keys, t)` |

`slope(a, b) = |b.time - a.time| <= 0.0001 ? 0 : (b.value - a.value) / (b.time - a.time)`

> ⚠ **할선이지 탄젠트가 아니다.** 지금 덤프의 yCurve는 Linear라 `outSlope(-56)`과
> 할선(`(-440-120)/10 = -56`)이 우연히 같다 — 곡선을 손보는 날 갈린다.

## 4. 부탁 ③ — 하지 말 것 하나 (같은 함정 반복 금지)

`CharacterDepthLevelTuningSet.Resolve`는 레벨로 `preserveFocus`도 고른다
(`≤2.5 far · ≤6.5 mid · ≤8.5 close · 그 밖 front`). **그 값을 폴드에 쓰면 안 된다.**
`ResolveRawDepth`가 읽은 직후 **커맨드 인자로 무조건 덮어쓰기** 때문이다
(`CharacterDepthResolver.cs:42`) — 프리셋 경로에서 이미 겪은 함정이고, 코어에
그 경고가 주석으로 남아 있다(`FocusStageReductions.cs:150-153`). 폴드의 보존 대상은
**지금처럼 `preserveFocusToken` 인자**다.

## 5. 수용 기준

1. `size c1 5`·`size c1 -3.5`·`size c1 12.5`(설계 구간 밖)가 **폴드에서 거부되지 않는다.**
2. 같은 셋을 실제 재생한 값과 폴드 값이 일치한다 — EditMode 대조 `< 1e-3`
   (`depthY.y`·`depthScale` 둘 다). 기존 `FocusStageReductionTests` 옆에 붙이면 된다.
3. `size c1 5 face`와 `size c1 5 bust`의 보정이 서로 다르다 = 인자 보존 대상이 이긴다(§4).
4. **프리셋 경로 회귀 없음** — 기존 코퍼스·테스트 그대로 통과.
5. 커브가 비었거나 `level`이 없는 덤프에서는 지금과 같이 거부(사유 문자열 유지).

## 6. 이쪽이 할 일 (그쪽 개통 뒤)

코어 사본 동기화 한 번(`src/Ked.Presentation.Core/README.md` 절차) + 작업대 슬라이더
아래의 "프리뷰 폴드는 아직 프리셋만 압니다" 안내 한 줄 제거. 그게 전부다 —
VnTool의 시간 흐름 계획(`StageMotionPlan`)은 폴드 차이만 보므로, 레벨이 접히는 순간
**뎁스가 시간에 따라 흐르는 것까지 저절로 따라온다.**

## 7. 하지 않는 것

- 레벨 범위 제한 — 런타임이 임의 실수를 받으므로 폴드도 받는다. -10~10은 **VnTool
  슬라이더의 편의 범위**일 뿐 계약이 아니다.
- 프리셋 ↔ 레벨 상호 변환 — 두 경로는 값의 출처가 다르다(프리셋 `far`의 depthY 480 vs
  레벨 0의 120). 섞어 매핑하면 둘 다 거짓말이 된다.
- 덤프 스키마 확장 — `level`은 **이미 나오고 있다.** 새로 내보낼 것이 없다.
