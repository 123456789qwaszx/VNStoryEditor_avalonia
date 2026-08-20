# Ked.Presentation.Core — 복사 사본 (정본은 런타임 저장소)

`ked-presentation-runtime` 저장소 `Assets/Scripts/Ked.Presentation.Core/`의 복사 사본이다.
**정본은 언제나 저쪽이고, 흐름은 한 방향이다: 런타임 → 이 사본.**
패키지화는 소유자 결정으로 보류 중이며(2026-08-20), 그 대신 아래 동기화 절차가
반복 가능한 대체물이다 (`docs/work-orders/presentation-refresh-orders.md` W64).

- 최근 동기화: **2026-08-20** (저쪽 `0a7f6d0f` 이후 시점의 작업 트리 기준, W64)
- 이전 반입: `3fe52e9b` (2026-08-05, 최초 복사 스냅샷 — 당시엔 "동기화 없음"이었다)
- 범위: `Primitives/`·`Reduce/`·`State/`·`Tokens/`·`Transforms/`·`Tuning/` 소스와
  `Documentation~/` 규약 문서. 순수 테스트(UnityParity 제외)는
  `tests/Ked.Presentation.Core.Tests/`에 있다.

## 동기화 절차 (다음에도 이대로)

1. **감사** — 줄바꿈 정규화 diff(`tr -d '\r'`)로 세 갈래를 가른다:
   ⓐ 런타임에만 있는 파일(가져온다) ⓑ 내용이 다른 파일(런타임 본으로 교체)
   ⓒ 이쪽에만 있는 것 — `README.md`(이 파일)·`Ked.Presentation.Core.csproj`는 툴 살림이라
   유지, **코드 멤버가 이쪽에만 있으면 그건 과거 로컬 수정이다** — 목록으로 남기고
   저쪽 반영을 소유자에게 물은 뒤 처리한다 (2026-08-20 감사에서는 0건이었다).
2. **이식** — 저쪽 트리에서 `Tests/`·`*.meta`·`*.asmdef` 제외 전부 복사.
   테스트는 `Tests/EditMode/*.cs`(UnityParity 제외)를 `tests/Ked.Presentation.Core.Tests/EditMode/`로.
3. **보수** — 소비자(Vn.Authoring·Vn.App)의 컴파일·의미 적응은 **소비자 쪽에서** 고친다.
   이 사본의 파일을 고쳐 맞추지 않는다 — 사본을 고치는 순간 다음 동기화가 그 수정을
   조용히 지운다. 저쪽에 문제가 있으면 목록으로 남겨 소유자가 전달한다.
4. **고정** — 전체 빌드 + 전체 테스트. 튜닝 실덤프 픽스처
   (`tests/Vn.Authoring.Tests/TuningFixtures/ExportedTuning/`)도 저쪽 `ExportedTuning/`
   폴더째 갱신한다(코어가 새 튜닝 축을 요구하면 낡은 픽스처가 진단을 낸다 —
   2026-08-20의 role-anchor가 그랬다). run-log에 항목을 남긴다.

⚠ zip 스냅샷 등 **빌드 검증 없이 건너온 코드**를 그대로 믿지 말 것 — 같은 컴파일
오류가 되살아난 전력이 있다. 복사 후 반드시 이쪽에서 빌드·테스트까지가 동기화다.

## 유니티 호환 안전핀

csproj가 원본 asmdef의 `noEngineReferences: true`와 유니티 C# 수준(LangVersion 9,
netstandard2.1)을 컴파일러 제약으로 유지한다 — 코드가 유니티로 못 돌아가는 상태가
되는 것을 막는다.
