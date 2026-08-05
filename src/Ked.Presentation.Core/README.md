# Ked.Presentation.Core — 복사 스냅샷

`ked-presentation-runtime` 저장소 `Assets/Ked.Presentation.Core/`에서 그대로 복사해 반입한
연출 코어다. **동기화 없음 — 복사 스냅샷이다** (`docs/handoff/architecture-decisions.md` H-4).

- 원본 커밋: `3fe52e9be129b06cf0a20437be8704a18753ea58` (2026-08-05 반입)
- 범위: `Primitives/`·`Reduce/`·`State/`·`Tokens/`·`Transforms/`·`Tuning/`의 소스 24파일과
  `Documentation~/` 규약 문서 2편. 순수 테스트(UnityParity 제외)는
  `tests/Ked.Presentation.Core.Tests/`에 있다.
- 수정하게 되면 커밋 메시지에 "코어 로컬 수정"임을 명시할 것 — 재수입/패키지화를 논할 때
  어긋난 자리를 찾기 위해서다.
