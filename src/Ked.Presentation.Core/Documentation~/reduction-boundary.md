# 리덕션 경계 — "스펙 → 목표 상태" 규약 (U13-b-4)

51곳의 `ClaimTarget` 관습을 규약으로 승격한 것. b-5의 커맨드 이관이 전부
이 문서의 모양을 따른다. 본보기: `Reduce/CharPlacementReductions.cs`
(MoveBy · ScaleTo) ↔ 호스트 `MoveByCommandCharR` · `ScaleToCommandCharR`.

## 코어로 가는 것 / 호스트에 남는 것

| | 어디 | 왜 |
|---|---|---|
| 스펙 → 목표 상태 변환 (`XxxReduction.Reduce`) | **코어** | 정지 프레임(2b)이 필요한 전부. 순수 산수다 |
| 출력 타입 (`StageNodeClaim`) | **코어** | 게시·트윈 종점·리듀서 폴드가 같은 값을 봐야 한다 |
| `CommandBase` · 코루틴 · `WaitForCompletion` | 호스트 | MonoBehaviour 호스트를 문다 |
| `ResolveRefs` (slotKey → RectTransform) | 호스트 | 유니티 참조 해석이다 |
| `DOKill` · 트윈 생성/가속 (`OnStepLifetimeFinished`) | 호스트 | 시간의 세계다 — 2c에서 다시 만난다 |
| Commit의 트랜스폼 쓰기 · Ledger Clear | 호스트 | 유니티에 쓰는 일이다 |
| `HasClaimedTarget` 수명 관리 | 호스트 | 실행 수명의 일부다 |

## 리덕션의 형태 규약

```csharp
public static class XxxReduction            // 커맨드당 하나, Reduce/ 폴더
{
    public readonly struct Args { ... }     // 파싱된 스펙 값만. UnityEngine 타입 금지
    public static StageNodeClaim Reduce(    // 순수 함수
        string nodeKey,                     // 논리 노드 키 (호스트: rect.name / 리듀서: 트리 키)
        in Args args,
        /* 필요한 현재 상태를 명시적 인자로 */ ...);
}
```

- **순수**: 시간·랜덤·IO·전역 상태 없음. 트랜스폼을 읽지 않는다 — 현재 상태가
  필요하면 값으로 받는다(예: `currentAnchoredPosition`). 같은 입력은 언제나 같은 출력.
- **출력은 `StageNodeClaim`**(복수 노드면 배열 — SetDepth처럼 위치+스케일 두 장).
  스케일 클레임의 z 보존 규약은 `ApplyTo`가 안다.
- **입력 편차 축** (b-4 표본 보고): ① 순수(스펙만) ② tuning 값 필요(파싱된 프리셋
  값을 Args에 담아 받는다) ③ 현재 정착 상태 의존(SetDepth·PlaceFocus·shot —
  정착 조회 결과를 인자로 받거나, b-5에서 solver 자체를 코어 함수로 옮긴다.
  `TryMeasureFocusWithTemporaryDepthTransform`의 "적용→측정→복원"도 그때 같이 걷는다).

## 호스트 어댑터의 형태 (본보기에서)

```csharp
private void ClaimTarget()
{
    _rect.DOKill(true);                                    // 호스트
    _startPos = _rect.anchoredPosition;                    // 호스트: 현재 상태 읽기
    StageNodeClaim claim = MoveByReduction.Reduce(         // ── 코어 ──
        _rect.name,
        new MoveByReduction.Args(_spec.useAbsolutePosition, ToVec2(_spec.delta)),
        ToVec2(_startPos));
    _destPos = ToVector2(claim.Value);                     // 호스트: 트윈 종점
    _rigRefs.PlacementTargets.PublishAnchoredPosition(_rect, _destPos); // 호스트: 게시
    HasClaimedTarget = true;
}
```

동작 불변이 규약이다: 어댑터는 계산의 **자리만** 바꾸고 값·순서·부작용을 바꾸지 않는다.

## 클레임이 흐르는 세 갈래

1. 호스트 게시: `CharacterPlacementTargetLedger`(어댑터) → 코어 `PlacementTargetLedger.Publish(claim)`
2. 트윈 종점: 호스트가 `claim.Value`를 DOTween 종점으로 사용
3. 리듀서 폴드(2b): `claim.ApplyTo(tree)` — `RectNodeTree.SetState`로 접힌다

셋이 같은 값을 보므로, "재생 결과 = 정착 예약 = 정지 프레임"이 한 곳에서 갈라진다.
