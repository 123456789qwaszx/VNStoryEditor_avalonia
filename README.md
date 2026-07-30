# Story Editor Vertical Slice

작가용 스토리 에디터의 첫 번째 세로 단면을 학습용으로 구현한 Spring Boot 프로젝트입니다.

하나의 요청이 `Controller → Service → Domain → Repository → DB`를 통과하고, 다시 JSON 응답으로 돌아오는 전체 흐름을 한 프로젝트 안에서 추적할 수 있습니다.

## 구현된 흐름

1. 스토리 프로젝트 생성
2. 노드 생성
3. 노드에 선택지 추가
4. 선택지를 다른 노드 키에 연결
5. 그래프 유효성 검사
6. 프로젝트 전체 조회
7. Unity용 JSON 내보내기

인증, 권한, 실시간 협업, Redis, WebSocket은 의도적으로 제외했습니다. 첫 학습 목표는 Spring 요청 흐름과 Java 코드의 역할을 선명하게 보는 것입니다.

## 기술 구성

- Java 21
- Spring Boot 4.1.0
- Spring Web MVC
- Spring Data JPA
- Jakarta Validation
- H2 Database
- JUnit 5 / MockMvc
- Maven

Lombok은 사용하지 않았습니다. 생성자 주입, getter, record 등 Java 코드가 실제로 어떻게 생기는지 숨기지 않기 위해서입니다.

## 실행

### IntelliJ IDEA

1. 이 폴더의 `pom.xml`을 엽니다.
2. Maven 의존성 동기화가 끝날 때까지 기다립니다.
3. `StoryEditorApplication.main()`을 실행합니다.
4. `http/story-editor.http` 파일을 위에서부터 실행합니다.

### 명령줄

Maven 3.6.3 이상과 Java 21이 설치되어 있어야 합니다.

```bash
mvn spring-boot:run
```

테스트:

```bash
mvn test
```

## 가장 먼저 읽을 파일 순서

1. `http/story-editor.http`
2. `StoryController.java`
3. `StoryEditorService.java`
4. `StoryProject.java`
5. `StoryNode.java`
6. `StoryChoice.java`
7. `StoryGraphValidator.java`
8. `StoryProjectRepository.java`
9. `ApiExceptionHandler.java`
10. `StoryEditorApiTest.java`

코드 전체를 처음부터 이해하려 하지 말고, HTTP 요청 하나를 선택해 디버거로 끝까지 따라가세요.

## Unity 코드와 대응해서 보기

| Spring 코드 | Unity에서 가까운 역할 |
|---|---|
| `StoryController` | 버튼 입력이나 UI 이벤트를 받아 흐름을 시작하는 어댑터 |
| `StoryEditorService` | `DayCycleFlow`, `ServiceSessionFlow` 같은 유스케이스 진행자 |
| `StoryProject` | `CampaignState`처럼 규칙과 상태를 가진 중심 객체 |
| `StoryProjectRepository` | 저장소 인터페이스 또는 세이브 시스템 |
| 요청/응답 `record` | UI 요청 모델, 읽기 전용 Snapshot |
| `@Transactional` | 한 작업을 성공 또는 실패 단위로 묶는 경계 |
| `@RestControllerAdvice` | 여러 화면에서 공통으로 사용하는 오류 변환기 |

## 이 프로젝트에서 확인할 Java 개념

### 제네릭과 컬렉션

`StoryGraphValidator`는 다음 자료구조를 실제 용도로 사용합니다.

- `List`: 노드와 검증 결과의 순서
- `Map`: 노드 키로 노드를 빠르게 찾는 인덱스
- `Set`: 이미 방문한 노드 중복 방지
- `Deque`: 그래프 탐색 대기열

### record

`StoryProjectSnapshot`, `StoryValidationReport`, `StoryExportDocument`는 전달용 불변 데이터입니다. 상태와 행동을 가지는 JPA Entity와 구분해 보세요.

### 예외

도메인 예외를 Controller에서 직접 처리하지 않고 `ApiExceptionHandler`가 HTTP 상태와 오류 JSON으로 변환합니다.

### JPA와 객체 생명주기

- `protected` 기본 생성자: JPA가 객체를 복원하기 위해 필요합니다.
- `@OneToMany`: 프로젝트가 노드 생명주기를 소유합니다.
- `cascade`: 프로젝트 저장 시 새 노드와 선택지도 함께 저장됩니다.
- `orphanRemoval`: 컬렉션에서 제거된 자식을 DB에서도 제거할 수 있게 합니다.
- `@Version`: 동시에 수정될 때 오래된 저장을 감지하기 위한 낙관적 락 버전입니다.

### 트랜잭션

`StoryEditorService`가 트랜잭션 경계를 소유합니다. Controller가 DB 작업을 직접 하지 않도록 한 이유를 살펴보세요.

## 설계 의도

### 앞으로 존재할 노드로 연결 가능

작가는 선택지를 만든 뒤 목적지 노드를 나중에 작성할 수 있습니다. 그래서 `addChoice()`는 목적지 노드의 존재를 즉시 강제하지 않습니다.

대신 `StoryGraphValidator`가 다음을 검사합니다.

- 노드가 하나도 없음
- 시작 노드가 없음
- 존재하지 않는 노드로 향하는 선택지
- 시작 노드에서 도달할 수 없는 노드

이것은 “입력 순간의 규칙”과 “문서 전체 완성도 검사”를 분리한 예입니다.

### Entity를 API에 직접 반환하지 않음

JPA Entity를 그대로 JSON으로 반환하면 지연 로딩, 순환 참조, API와 DB 구조의 결합 문제가 생깁니다. 그래서 트랜잭션 안에서 `StoryProjectSnapshot`으로 복사한 뒤 반환합니다.

### 서비스에 요청별 상태를 저장하지 않음

Spring의 `@Service` 객체는 기본적으로 여러 요청이 공유합니다. 따라서 현재 프로젝트 ID나 선택된 노드를 필드에 저장하지 않고 메서드 매개변수와 지역 변수로만 처리합니다.

## H2 데이터베이스 확인

서버 실행 후 다음 주소에서 H2 Console을 열 수 있습니다.

- URL: `http://localhost:8080/h2-console`
- JDBC URL: `jdbc:h2:file:./data/story-editor`
- User Name: `sa`
- Password: 비워 둠

## 추천 디버거 중단점

첫 요청을 실행하면서 다음 순서로 중단점을 걸어보세요.

1. `StoryController.createProject()`
2. `StoryEditorService.createProject()`
3. `StoryProject.create()`
4. `JpaRepository.save()` 호출 직전
5. `StoryProjectSnapshot.from()`

선택지 검증에서는:

1. `StoryEditorService.validateProject()`
2. `StoryGraphValidator.validate()`
3. `findReachableNodes()`의 `while` 루프
4. `DANGLING_CHOICE`가 추가되는 지점

## 다음 문서

- [코드 읽기 가이드](docs/CODE_READING_GUIDE.md)
- [리팩터링 미션](docs/REFACTORING_MISSIONS.md)

## Spring Boot 4 참고

이 프로젝트는 Spring Boot 4.1을 사용합니다. 이전 Spring Boot 3 예제와 비교하면 테스트 자동 설정 패키지와 기본 Jackson 버전 등 일부 위치가 달라질 수 있습니다. 강의 코드와 import가 다를 때는 개념이 바뀐 것인지, 단순 패키지 이동인지 먼저 구분해 보세요.
