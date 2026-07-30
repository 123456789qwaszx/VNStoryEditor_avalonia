package com.hill.storyeditor.story.domain;

public record ValidationIssue(
    ValidationSeverity severity,
    String code,
    String message,
    String nodeKey,
    String choiceText
) {
    public static ValidationIssue error(
        String code,
        String message,
        String nodeKey,
        String choiceText
    ) {
        return new ValidationIssue(
            ValidationSeverity.ERROR,
            code,
            message,
            nodeKey,
            choiceText
        );
    }

    public static ValidationIssue warning(
        String code,
        String message,
        String nodeKey
    ) {
        return new ValidationIssue(
            ValidationSeverity.WARNING,
            code,
            message,
            nodeKey,
            null
        );
    }
}
