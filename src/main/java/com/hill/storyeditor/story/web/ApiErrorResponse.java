package com.hill.storyeditor.story.web;

import java.time.Instant;
import java.util.Map;

public record ApiErrorResponse(
    Instant timestamp,
    int status,
    String code,
    String message,
    String path,
    Map<String, String> fieldErrors
) {
    public static ApiErrorResponse of(
        int status,
        String code,
        String message,
        String path
    ) {
        return new ApiErrorResponse(
            Instant.now(),
            status,
            code,
            message,
            path,
            Map.of()
        );
    }

    public static ApiErrorResponse validation(
        int status,
        String path,
        Map<String, String> fieldErrors
    ) {
        return new ApiErrorResponse(
            Instant.now(),
            status,
            "REQUEST_VALIDATION_FAILED",
            "요청 값이 올바르지 않습니다.",
            path,
            Map.copyOf(fieldErrors)
        );
    }
}
