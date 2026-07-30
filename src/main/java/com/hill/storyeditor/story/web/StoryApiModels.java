package com.hill.storyeditor.story.web;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;

public final class StoryApiModels {

    private StoryApiModels() {
    }

    public record CreateProjectRequest(
        @NotBlank(message = "프로젝트 제목은 필수입니다.")
        @Size(max = 100, message = "프로젝트 제목은 100자 이하여야 합니다.")
        String title
    ) {
    }

    public record CreateNodeRequest(
        @NotBlank(message = "노드 키는 필수입니다.")
        @Pattern(
            regexp = "[A-Za-z][A-Za-z0-9_]{0,79}",
            message = "노드 키는 영문자로 시작하고 영문자, 숫자, 밑줄만 사용할 수 있습니다."
        )
        String nodeKey,

        @NotBlank(message = "노드 제목은 필수입니다.")
        @Size(max = 120, message = "노드 제목은 120자 이하여야 합니다.")
        String title,

        @NotBlank(message = "대사는 필수입니다.")
        @Size(max = 10_000, message = "대사는 10,000자 이하여야 합니다.")
        String dialogue
    ) {
    }

    public record CreateChoiceRequest(
        @NotBlank(message = "선택지 문구는 필수입니다.")
        @Size(max = 200, message = "선택지 문구는 200자 이하여야 합니다.")
        String text,

        @NotBlank(message = "목적지 노드 키는 필수입니다.")
        @Pattern(
            regexp = "[A-Za-z][A-Za-z0-9_]{0,79}",
            message = "목적지 노드 키 형식이 올바르지 않습니다."
        )
        String targetNodeKey,

        @Size(max = 500, message = "조건식은 500자 이하여야 합니다.")
        String conditionExpression
    ) {
    }

    public record ChangeEntryNodeRequest(
        @NotBlank(message = "시작 노드 키는 필수입니다.")
        @Pattern(
            regexp = "[A-Za-z][A-Za-z0-9_]{0,79}",
            message = "시작 노드 키 형식이 올바르지 않습니다."
        )
        String nodeKey
    ) {
    }
}
