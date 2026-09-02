# Ked.Progression 반입 기록

이 폴더의 순수 C# 소스는 Unity 런타임 진행 코어의 검증용 사본이다. 여기서 독자적으로
규칙을 수정하지 않는다.

## 현재 기준

- 반입일: 2026-09-02
- 저장소: `123456789qwaszx/ked-presentation-runtime`
- 브랜치: `server_DB`
- 커밋: `d53cc2f58423ba6e83fabfabbe88f1f547272380`
- 원천: `Assets/Scripts/Ked.Progression/**/*.cs`
- 대상: `src/Ked.Progression/**/*.cs`

## 반입 경계

다음은 Unity 또는 호스트 전용이므로 사본에 넣지 않는다.

- `*.meta`
- `Ked.Progression.asmdef`
- `Documentation~/`
- `Tests/EditMode/` — Unity NUnit 테스트이며, 에디터에서는 대응하는 xUnit 계약 테스트를 둔다.

`Ked.Progression.csproj`는 .NET/Avalonia 쪽 빌드 경계이므로 이 저장소가 소유한다.

## 이번 반입의 의미 있는 변화

- DTO가 `Ked.Progression.Dto`의 단일 파일에서 `Ked.Progression`의 타입별 파일로 바뀌었다.
- `EndingRule`, `ScenarioAdvance`, `ScenarioTransition`이 제거됐다.
- `EpisodeNode`에 `SceneId`가 추가되고, 빈 값은 `__scene_{EpisodeId}`로 읽힌다.
- `EpisodeOption`에 명시적 `Auto`가 추가됐다.
- 장면 외부 착지점 하나와 Auto 간선 불변식이 로더의 거부 규칙이 됐다.
- `ProgressionState`가 즉시 Commit 대신 Scene 내 선택 Fold를 지원한다.
- `ResolvedOption.SourceIndex`가 원본 `NextOptions` 순서를 보존한다.

## 재반입 검산

원천과 대상의 `*.cs` 상대 경로 및 바이트를 비교한다. 위 제외 목록 외 차이가 있으면
반입이 끝난 것이 아니다. 그 뒤 솔루션 전체 빌드와 테스트를 통과시킨다.
