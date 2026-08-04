# §6.6 미지수 셋 — 런타임 저장소 조사 결과

2026-08-05 조사. 대상 `ked-presentation-runtime` (`Assets/@Scripts` 572파일 / 78,215줄).
`work-orders/phase2-design-brief.md` §6.6이 "런타임만 답할 수 있다"고 남긴 세 질문에 대한 답.
**U13 지시서 작성 전에 읽을 것.** 근거는 전부 실제 파일에서 나왔고, 확인하지 못한 것은 §6에 적었다.

---

## 0. 요약

| # | 질문 | 답 |
|---|---|---|
| 1 | netstandard2.1 만족하는가 | **예.** `apiCompatibilityLevel: 6` = .NET Standard |
| 2 | MonoBehaviour에 얼마나 엉켜 있는가 | **거의 안 엉켜 있다. 질문이 겨눈 곳이 아니다** — 진짜 엉킴은 `RectTransform` |
| 3 | `CaptureCurrent()` 구현 가능한가 | **예.** 같은 일을 하는 코드가 이미 두 군데 있다 |
| + | (묻지 않음) 어셈블리 분리 | ⚠ **asmdef가 0개다.** 코어를 뽑으려면 이것부터 |

---

## 1. Q1 — 타깃

`ProjectSettings/ProjectSettings.asset:948`

```yaml
  apiCompatibilityLevel: 6
```

Unity `ApiCompatibilityLevel` 열거값 6 = `NET_Standard`. Unity 2021.2 이후 이 프로필은 **netstandard2.1**을 대상으로 한다. 프로젝트는 Unity `6000.3.16f1`(README)이므로 해당된다.

> 열거값↔표시명 대응이 역사적으로 한 번 바뀐 적이 있으므로,
> 에디터에서 **Player Settings → Api Compatibility Level이 ".NET Standard 2.1"로 보이는지** 눈으로 한 번 확인하는 것을 권한다. 숫자보다 라벨이 근거로 낫다.

**결론: §6.2의 `netstandard2.1 · 외부 의존성 0 · UnityEngine 참조 0` 제약은 타깃 측면에서 성립한다.**

---

## 2. Q2 — 엉킴의 위치가 다르다

질문은 "연출 로직이 `MonoBehaviour`·컴포넌트 상태에 얼마나 엉켜 있는가"였다.
**세어 보면 거의 안 엉켜 있다.**

| 영역 | 파일 수 | 그중 MonoBehaviour |
|---|---|---|
| `Commands/` | 116 | **1** |
| `PresentationCore/` | 54 | **2** |

커맨드는 전부 평범한 C# 클래스다. `CommandBase`는 `MonoBehaviour`가 아니라 추상 클래스이고,
`PresentationShotResponseSystem`·`CharacterPlacementTargetLedger` 같은 시스템 클래스도 마찬가지다.

### 진짜 엉킴은 `RectTransform`을 상태 담지체로 쓰는 것이다

`Commands/` + `PresentationCore/` + `CharacterRig/` + `ShotResponse/` + `BackgroundRig/` 기준 타입 등장 빈도:

```
Vector2        585
RectTransform  402      ← 이것
Vector3        179
Color          117
Image           60
Sprite          56
CanvasGroup     50
Transform       28
MonoBehaviour   14      ← 대부분 코루틴 호스트 인자
GameObject       6
Component        4
```

`Vector2`·`Vector3`·`Color`는 값 타입이라 §6.2의 `Vec2`·`Rgba`로 **기계적 치환**이 된다.
문제는 `RectTransform` 402회인데, 이게 단순 참조가 아니라 **상태가 사는 자리**다.

```csharp
// SetDepthCommandCharR — 51개 커맨드가 공유하는 모양
ResolveRefs(scope)      → _rigRefs.GetRect(...)          Unity에서 RectTransform 획득
ClaimTarget(scope)      → _startDepthY = _depthYRect.anchoredPosition   현재값을 Unity에서 읽고
                          _destFinalDepthY = ...(순수 계산)              목표값을 계산
   ↓ 트윈
CommitFinalState()      → _depthYRect.anchoredPosition = _destFinalDepthY   Unity에 쓴다
```

**현재 상태를 물어볼 곳이 트랜스폼밖에 없다.** 별도의 상태 모델이 없다.
그래서 U13은 "MonoBehaviour에서 로직 뜯어내기"가 아니라
**"트랜스폼을 읽고 쓰던 자리를 상태 값으로 바꾸기"**다. 일의 성격이 다르다.

### 이미 절반은 되어 있다 — 두 가지 발견

**(가) 커맨드 51개가 이미 "목표값 계산"과 "트윈"을 분리해 두었다.**

`ClaimTarget` 패턴을 가진 커맨드가 51개다. 정지 프레임(2b)이 필요로 하는 것은
**`ClaimTarget`이 산출하는 목표값뿐**이고 트윈은 2c의 일이다.

DOTween 의존은 217파일 중 67개(31%)인데, 그 대부분이 트윈 구간에 몰려 있다.
**2b 범위에서는 이 67개를 건드릴 필요가 없다.**

⚠ 단 이 분리는 **규약이 아니라 관습이다.** `CommandBase`에 `ClaimTarget`/`HasClaimedTarget`
선언이 없고 51곳이 각자 구현했다. 코어로 올릴 때 51곳이 미묘하게 다를 수 있으므로
**표준화가 U13의 실제 작업량**이다.

**(나) 원형(proto) 상태 모델이 이미 돌고 있다.**

`ShotResponse/CharacterPlacementTargetLedger.cs`:

```csharp
// 측정 직전, 움직이는 상위 노드들을
// - 예약된 최종 target 값에 도착했다고 가정하여 값을 세팅하고,
// - focus point의 world position을 잠깐 측정하고
// - 즉시 원복
private readonly Dictionary<RectTransform, Entry> _targets = new();

public void PublishAnchoredPosition(RectTransform node, Vector2 targetAnchoredPosition)
public void PublishLocalScale(RectTransform node, Vector2 targetLocalScaleXY)
public void PublishLocalEuler(RectTransform node, Vector3 targetLocalEuler)
```

**"트윈이 다 끝났다면 어디에 있을 것인가"가 이미 별도로 보관되고 있다.**
이게 `StageState`의 원형이다. 다른 점은 둘뿐이다.

- 키가 `RectTransform` → 논리 식별자(`slotKey`·`stage`·`layer`)로 바뀌어야 한다
- 측정을 "Unity에 잠깐 써넣고 되돌리기"로 하고 있다 → 순수 변환 수학으로 바뀌어야 한다

**U13은 없는 것을 만드는 게 아니라 이것을 승격시키는 일이다.**

---

## 3. Q3 — `CaptureCurrent()`

**가능하다. 같은 일을 하는 코드가 이미 두 군데 있다.**

**(가) 라이브 트랜스폼 → 값 구조체**

`PresentationResponseCoordinateMapper.CaptureBaseMeasure(target)`가
살아 있는 리그에서 읽어 Unity 참조가 하나도 없는 값 구조체를 만든다.

```csharp
public readonly struct PresentationResponseMeasure
{
    public readonly Vector2 basePositionInRigSpace;
    public readonly Vector2 baseAnchoredPosition;
    public readonly Vector2 baseLocalScale;
}
```

**(나) 정착 상태 재구성**

`CharacterPlacementTargetLedger.WorldPointToSettledParentLocalPoint`가
"예약된 최종값이 적용된 세계"를 만들어 측정하고 되돌린다 — U14 하네스가 원하는 바로 그 질문이다.

**결론: 읽어내는 패턴이 이미 검증돼 있다.** `CaptureCurrent()`는 이것들을
논리 키 기준으로 모으는 일이지 새로 발명하는 일이 아니다.

---

## 4. 묻지 않았지만 U13을 막는 것 — asmdef가 0개다

```
Assets/**/*.asmdef  →  0개
```

**모든 코드가 기본 `Assembly-CSharp` 하나에 들어 있다.**
`Ked.Presentation.Core`를 별도 어셈블리로 뽑아 VnTool이 참조하려면
어셈블리 경계를 먼저 그어야 하는데, 이건 §6.2·§6.5에 없는 작업이다.

**주의할 점**: 경계가 없던 572파일 프로젝트에 asmdef를 도입하면
**숨어 있던 순환 참조가 한꺼번에 드러난다.** Unity에서 흔한 함정이고,
"코어만 뽑으면 되는 줄 알았는데 프로젝트 절반을 재배치하게 되는" 자리다.

**권고: U13을 두 개로 쪼갠다.**

| | 내용 | 성격 |
|---|---|---|
| U13-a | asmdef 도입 + 순환 참조 해소. **코드 로직 변경 0** | 위험이 여기 몰려 있다 |
| U13-b | `Ked.Presentation.Core` 추출 + 리듀서 표준화 | a가 끝나야 시작 가능 |

U13-a의 규모는 실제로 그어 봐야 안다. 순환이 적으면 하루, 많으면 주 단위가 될 수 있고,
**그 편차가 §6.6이 걱정한 "일정이 달라진다"의 실체**다 — MonoBehaviour 엉킴이 아니라 어셈블리 경계다.

---

## 5. 이 답이 설계 초안에 미치는 영향

| 초안 | 조정 |
|---|---|
| §6.6-1 (타깃) | ✅ 해소. 제약을 U13 지시서에 그대로 실으면 된다 |
| §6.6-2 (엉킴) | 🔄 **질문 교체.** MonoBehaviour 아님 → RectTransform-as-state. 추출 대상은 `ClaimTarget` 51곳 |
| §6.6-3 (캡처) | ✅ 해소. 기존 패턴 두 개를 근거로 지시 가능 |
| §6.5 이행 순서 | ➕ **U13-a(asmdef)가 0단계 앞에 붙는다** |
| §1.1 소유자 진술 | 부분 확인 — 아래 §6 |
| D-2b-3 (접는 순서) | 보강: `char_rig_placement`·`depth`·`shot`이 전부 `ClaimTarget` 패턴 안에 있어 추천이 그대로 유효 |

---

## 6. 확인하지 못한 것 (추정을 넣지 않는다)

1. **셰이더 비중.** §1.1의 "셰이더를 빼면 75% 이상 순수 C#"에서
   **"상태 계산에 MonoBehaviour 의존이 없다"는 확인했지만**, 셰이더 파일 자체와
   `StageMaskGraphic`(849줄)·`ScreenEffectRigBuilder`의 렌더 경로는 읽지 않았다.
   D-2b-2의 "미표시(뱃지)" 등급이 걸린 자리다.
2. **`CommandBase`의 코루틴 호스트.**
   `RegisterStepLifetime(scope, MonoBehaviour host, IEnumerator routine)` —
   기반 클래스 시그니처에 `MonoBehaviour`가 있다. 순수 리듀서에는 무관하지만
   **`CommandBase` 자체는 코어로 옮길 수 없다.** 코어에 가는 것은 커맨드 클래스가 아니라
   "스펙 → 상태 변환" 부분이다. 이 경계를 U13 지시서가 명시해야 한다.
3. **51개 `ClaimTarget`의 실제 편차.** 개수만 셌고 본문을 다 읽지는 않았다.
   표준화 비용은 U13-b 착수 시 표본 5~10개를 읽고 다시 추정할 것.
4. **순환 참조 규모.** asmdef를 실제로 그어 보기 전에는 U13-a의 크기를 알 수 없다.
