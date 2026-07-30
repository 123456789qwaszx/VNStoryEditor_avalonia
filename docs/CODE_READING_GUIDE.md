# 코드 읽기 가이드

## 1. HTTP 요청에서 시작하기

`http/story-editor.http`의 첫 번째 요청은 다음 메서드로 들어옵니다.

```java
StoryController.createProject()
```

여기서 확인할 것:

- `@RestController`는 왜 필요한가?
- `@RequestMapping`과 `@PostMapping`은 각각 무엇을 결정하는가?
- JSON이 어떻게 `CreateProjectRequest` record로 변환되는가?
- `@Valid`는 언제 실행되는가?

## 2. Service로 이동하기

Controller는 규칙을 직접 처리하지 않고 `StoryEditorService`를 호출합니다.

확인할 것:

- 생성자에 `StoryEditorService`를 직접 `new` 하지 않아도 값이 들어오는 이유
- 필드가 `final`인 이유
- Service 전체에 붙은 `@Transactional`의 범위
- 조회 메서드에 다시 `readOnly = true`를 지정한 이유

## 3. Domain 규칙 확인하기

`StoryProject.addNode()`는 다음 규칙을 책임집니다.

- 키 정규화
- 중복 키 거부
- 노드 생성
- 첫 노드를 시작 노드로 지정
- 수정 시각 갱신

Controller나 Service로 이 규칙을 옮겼을 때 생기는 중복을 상상해 보세요.

## 4. Repository 확인하기

`StoryProjectRepository`에는 구현 코드가 없습니다.

질문:

- 누가 구현체를 만드는가?
- `JpaRepository<StoryProject, Long>`의 두 제네릭 타입은 무엇인가?
- `findById()`가 왜 `Optional<StoryProject>`를 반환하는가?

## 5. 응답 Snapshot 확인하기

`StoryProjectSnapshot.from()`은 JPA Entity를 API용 불변 데이터로 복사합니다.

질문:

- Entity를 그대로 반환하면 어떤 문제가 생기는가?
- record는 일반 class와 무엇이 다른가?
- `stream().map(...).toList()`는 어떤 순서로 실행되는가?

## 6. 그래프 검증 확인하기

`StoryGraphValidator`는 중급 2편의 컬렉션을 실제 문제에 적용한 파일입니다.

- `Map<String, StoryNode>`: 키 기반 조회
- `Set<String>`: 방문 여부
- `Deque<String>`: BFS 대기열
- `List<ValidationIssue>`: 발견된 문제의 순서

직접 종이에 다음 그래프를 그리고 큐의 변화를 추적해 보세요.

```text
Start → Hallway → End
   └→ Missing
Unused
```

## 7. 테스트에서 의도 읽기

`StoryProjectTest`는 객체 규칙을 보여줍니다.

`StoryEditorApiTest`는 실제 HTTP 요청부터 DB 저장, 검증, JSON 응답까지 보여줍니다.

구현 코드를 먼저 읽기 어려울 때 테스트의 메서드 이름과 입력/기대 결과부터 읽으세요.
