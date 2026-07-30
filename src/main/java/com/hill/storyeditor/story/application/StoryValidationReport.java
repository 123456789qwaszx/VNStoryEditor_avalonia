package com.hill.storyeditor.story.application;

import com.hill.storyeditor.story.domain.ValidationIssue;
import com.hill.storyeditor.story.domain.ValidationSeverity;

import java.util.List;

public record StoryValidationReport(
    boolean valid,
    List<IssueView> issues
) {
    public static StoryValidationReport from(List<ValidationIssue> issues) {
        boolean valid = issues.stream()
            .noneMatch(issue -> issue.severity() == ValidationSeverity.ERROR);

        return new StoryValidationReport(
            valid,
            issues.stream().map(IssueView::from).toList()
        );
    }

    public record IssueView(
        String severity,
        String code,
        String message,
        String nodeKey,
        String choiceText
    ) {
        private static IssueView from(ValidationIssue issue) {
            return new IssueView(
                issue.severity().name(),
                issue.code(),
                issue.message(),
                issue.nodeKey(),
                issue.choiceText()
            );
        }
    }
}
