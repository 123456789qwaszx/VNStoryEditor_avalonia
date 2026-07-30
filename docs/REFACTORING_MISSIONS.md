# 리팩터링 미션

코드를 읽은 뒤 아래 순서대로 변경하면 Java와 Spring을 실제 문제를 통해 익힐 수 있습니다.

## 미션 1. 노드 대사 수정 API

추가할 API:

```text
PUT /api/projects/{projectId}/nodes/{nodeKey}
```

요구사항:

- 제목과 대사를 수정한다.
- 존재하지 않는 노드면 404를 반환한다.
- 수정 시 프로젝트의 `updatedAt`과 `version`이 바뀐다.
- Domain 테스트와 API 통합 테스트를 추가한다.

학습 대상:

- 객체 캡슐화
- HTTP PUT
- 변경 감지
- 트랜잭션

## 미션 2. 중복 선택지 금지

같은 출발 노드 안에서 `text + targetNodeKey`가 같은 선택지를 두 번 만들 수 없게 합니다.

고민할 것:

- `List`를 유지하면서 검사할 것인가?
- `Set`으로 바꿀 것인가?
- `StoryChoice.equals/hashCode()`를 구현해야 하는가?
- 선택지 순서가 중요한가?

학습 대상:

- List와 Set
- equals/hashCode
- 도메인 불변식

## 미션 3. 노드 이름 변경

노드 키 `Hallway`를 `MainHall`로 변경합니다.

반드시 함께 처리할 것:

- 시작 노드 키
- 다른 선택지의 `targetNodeKey`
- DB 유니크 제약조건
- 잘못된 부분 수정 시 전체 롤백

학습 대상:

- 객체 그래프 변경
- 트랜잭션의 원자성
- Map을 이용한 인덱싱

## 미션 4. 버전 기반 저장 충돌

클라이언트가 마지막으로 읽은 `version`을 수정 요청에 포함하도록 합니다.

```json
{
  "expectedVersion": 3,
  "title": "수정된 제목",
  "dialogue": "수정된 대사"
}
```

서버의 현재 버전과 다르면 409 Conflict를 반환합니다.

학습 대상:

- 낙관적 동시성 제어
- `@Version`
- 자동 저장 충돌
- 여러 요청이 같은 데이터를 수정하는 문제

## 미션 5. 검증 규칙 인터페이스 분리

현재 `StoryGraphValidator`에 들어 있는 규칙을 다음 인터페이스로 분리합니다.

```java
public interface StoryValidationRule {
    List<ValidationIssue> validate(StoryProject project);
}
```

예:

- `EntryNodeRule`
- `DanglingChoiceRule`
- `ReachabilityRule`

Spring이 `List<StoryValidationRule>`를 생성자에 주입하도록 구성합니다.

학습 대상:

- 인터페이스
- 다형성
- 컬렉션 주입
- Open/Closed Principle

## 미션 6. JPA Entity와 Domain 분리

현재 `StoryProject`에는 JPA 애노테이션과 도메인 규칙이 함께 있습니다.

다음 구조로 리팩터링합니다.

```text
domain/StoryProject
application/StoryProjectStore
persistence/StoryProjectJpaEntity
persistence/JpaStoryProjectStore
```

바꾸기 전후의 장단점을 기록하세요. 무조건 분리된 구조가 더 좋은 것은 아닙니다.

학습 대상:

- 포트와 어댑터
- 매핑 비용
- 프레임워크 의존성
- 과도한 추상화 판단

## 미션 7. 비동기 내보내기

내보내기 요청이 즉시 작업 ID를 반환하도록 바꿉니다.

```text
POST /api/projects/{id}/export-jobs
GET  /api/export-jobs/{jobId}
```

처음에는 메모리 기반으로 구현하고 이후 DB 저장으로 변경합니다.

학습 대상:

- Executor
- CompletableFuture
- 스레드 풀
- 공유 가변 상태
- 작업 상태 모델링
