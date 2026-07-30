package com.hill.storyeditor.story.domain;

import org.junit.jupiter.api.Test;

import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

class StoryGraphValidatorTest {

    private final StoryGraphValidator validator = new StoryGraphValidator();

    @Test
    void reportsDanglingChoiceAsError() {
        StoryProject project = StoryProject.create("테스트");
        project.addNode("Start", "시작", "시작한다.");
        project.addChoice("Start", "문을 연다", "Missing", null);

        List<ValidationIssue> issues = validator.validate(project);

        assertThat(issues)
            .anySatisfy(issue -> {
                assertThat(issue.severity()).isEqualTo(ValidationSeverity.ERROR);
                assertThat(issue.code()).isEqualTo("DANGLING_CHOICE");
                assertThat(issue.nodeKey()).isEqualTo("Start");
            });
    }

    @Test
    void reportsUnreachableNodeAsWarning() {
        StoryProject project = StoryProject.create("테스트");
        project.addNode("Start", "시작", "시작한다.");
        project.addNode("Unused", "미사용", "도달할 수 없다.");

        List<ValidationIssue> issues = validator.validate(project);

        assertThat(issues)
            .anySatisfy(issue -> {
                assertThat(issue.severity()).isEqualTo(ValidationSeverity.WARNING);
                assertThat(issue.code()).isEqualTo("UNREACHABLE_NODE");
                assertThat(issue.nodeKey()).isEqualTo("Unused");
            });
    }

    @Test
    void connectedStoryWithoutErrorsIsValid() {
        StoryProject project = StoryProject.create("테스트");
        project.addNode("Start", "시작", "시작한다.");
        project.addNode("End", "끝", "끝난다.");
        project.addChoice("Start", "끝낸다", "End", null);

        List<ValidationIssue> issues = validator.validate(project);

        assertThat(validator.hasErrors(issues)).isFalse();
        assertThat(issues).isEmpty();
    }
}
