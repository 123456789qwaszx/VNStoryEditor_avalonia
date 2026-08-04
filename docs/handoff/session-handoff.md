# 세션 인계 — 새 대화가 가장 먼저 읽는 문서

작성 2026-08-04. 기준 커밋 `439a82c`, 테스트 463개 전원 통과, 작업 트리 clean.

---

## 30초 요약

**VnTool**은 `ked-presentation-runtime`(유니티)이 재생할 `.yarn`/CSV를 만드는 저작 도구다.
C# / .NET 10 / Avalonia. Phase 0·1·2a-v1·2a-v2와 UX 지시서 X1–X13이 **전부 완료**됐고,
다음 큰 덩어리는 **Phase 2b(정지 프레임 렌더러)**인데 **소유자의 유니티 실재생 게이트 통과가 선행 조건**이다.

지금 당장 할 수 있는 일은 [next-tasks.md](next-tasks.md)의 T1(문서 상태표 갱신)·T2(수동 검증)이고,
그 외에는 **소유자 입력을 기다리는 상태**다.

## 이 폴더의 문서 6개

| 문서 | 언제 읽나 |
|---|---|
| **session-handoff.md** (이 파일) | 세션 시작 시 첫 번째 |
| [current-state.md](current-state.md) | "지금 뭐가 구현돼 있나"를 알아야 할 때 |
| [architecture-decisions.md](architecture-decisions.md) | 코드를 고치기 전에 — 확정된 설계와 그 이유 |
| [rejected-decisions.md](rejected-decisions.md) | 아이디어를 내기 전에 — 이미 폐기된 방향 |
| [known-issues.md](known-issues.md) | 버그를 만났을 때 / 작업 시작 전 리스크 확인 |
| [next-tasks.md](next-tasks.md) | 무엇을 할지 정할 때 — 순서와 수용 기준 |

## 이 폴더 바깥의 기준 문서 (충돌 시 이쪽이 우선)

| 문서 | 역할 |
|---|---|
| `../../ARCHITECTURE.md` | **코드 구조의 최종 기준.** §6의 규칙 15개는 "깨면 다른 곳이 조용히 부서지는" 목록 |
| `../vntool-master-plan.md` | 제품 최상위 기준 (6차 개정). ⚠ §1 진행 상태표는 stale — [known-issues.md](known-issues.md) K1 |
| `../runtime-contract.md` | 런타임 계약(A5·B·C1·C3·D2·E2…). 출력이 이걸 어기면 유니티에서 조용히 깨진다 |
| `../runtime-ui-tooling-principles.md` | 툴 설계 원칙 (§2.3 추측 금지, §2.4 규약 사본 금지, §2.5 침묵을 화면으로) |
| `../YarnCommandBridge_Reference.md` | 런타임 커맨드 200종 레퍼런스 |
| `../work-orders/*.md` | 살아 있는 작업 지시서 |
| `../archive/*.md` | 완료 기록 (새 작업을 얹지 않는다) |

## 작업 규칙 (지금까지 지켜온 것)

1. **커밋은 작업 단위 하나씩** (W13, X4, §3.1 …). 커밋 메시지에 수용 기준 확인 결과를 남긴다.
2. **모든 커밋 전 전체 테스트 통과 확인.** `dotnet test VnTool.sln`
3. UI를 바꿨으면 **앱 스모크**까지: `dotnet build src\Vn.App` 후 실행해 8초 생존 확인.
4. **저장 형식을 바꿀 때는 기본값 생략 직렬화** — 기존 프로젝트 파일이 한 바이트도 바뀌지 않아야 한다.
5. 새 규칙·해석 경로를 만들지 말고 **기존 것을 재사용**한다(ARCHITECTURE 규칙 15).

## 함정 셋 (여기서 시간을 잃었다)

- **PowerShell 5.1 + 한글**: 골든 파일·스크립트는 UTF-8. `&&`/삼항 연산자 없음. 자세히는 ARCHITECTURE §6-13.
- **Avalonia 12 API 변경**: `TextBox.Watermark`→`PlaceholderText`, `DataObject`/`DoDragDrop`→`DataTransfer`/`DoDragDropAsync`.
  컴파일 오류로 알게 되므로, 새 API를 쓸 땐 작은 파일로 먼저 탐침하는 편이 빠르다.
- **테스트 픽스처가 곧 사양**: `tests/Vn.Authoring.Tests/AssetFixtures/`의 폴더 구조 자체가 에셋 규약의 실증이다(`bandi/a/07.png`).
