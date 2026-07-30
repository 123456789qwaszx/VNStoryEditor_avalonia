package com.hill.storyeditor.story.web;

import com.hill.storyeditor.story.domain.StoryEditorException;
import com.hill.storyeditor.story.domain.StoryErrorCode;
import jakarta.servlet.http.HttpServletRequest;
import org.springframework.dao.OptimisticLockingFailureException;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;

import java.util.LinkedHashMap;
import java.util.Map;

@RestControllerAdvice
public class ApiExceptionHandler {

    @ExceptionHandler(StoryEditorException.class)
    public ResponseEntity<ApiErrorResponse> handleStoryEditorException(
        StoryEditorException exception,
        HttpServletRequest request
    ) {
        HttpStatus status = statusFor(exception.getCode());
        ApiErrorResponse response = ApiErrorResponse.of(
            status.value(),
            exception.getCode().name(),
            exception.getMessage(),
            request.getRequestURI()
        );
        return ResponseEntity.status(status).body(response);
    }

    @ExceptionHandler(MethodArgumentNotValidException.class)
    public ResponseEntity<ApiErrorResponse> handleValidationException(
        MethodArgumentNotValidException exception,
        HttpServletRequest request
    ) {
        Map<String, String> fieldErrors = new LinkedHashMap<>();
        exception.getBindingResult().getFieldErrors().forEach(error ->
            fieldErrors.putIfAbsent(error.getField(), error.getDefaultMessage())
        );

        ApiErrorResponse response = ApiErrorResponse.validation(
            HttpStatus.BAD_REQUEST.value(),
            request.getRequestURI(),
            fieldErrors
        );
        return ResponseEntity.badRequest().body(response);
    }

    @ExceptionHandler(IllegalArgumentException.class)
    public ResponseEntity<ApiErrorResponse> handleIllegalArgumentException(
        IllegalArgumentException exception,
        HttpServletRequest request
    ) {
        ApiErrorResponse response = ApiErrorResponse.of(
            HttpStatus.BAD_REQUEST.value(),
            "INVALID_DOMAIN_VALUE",
            exception.getMessage(),
            request.getRequestURI()
        );
        return ResponseEntity.badRequest().body(response);
    }

    @ExceptionHandler(OptimisticLockingFailureException.class)
    public ResponseEntity<ApiErrorResponse> handleOptimisticLockingFailure(
        OptimisticLockingFailureException exception,
        HttpServletRequest request
    ) {
        ApiErrorResponse response = ApiErrorResponse.of(
            HttpStatus.CONFLICT.value(),
            "EDIT_CONFLICT",
            "다른 요청이 먼저 프로젝트를 수정했습니다. 최신 내용을 다시 불러온 뒤 재시도하세요.",
            request.getRequestURI()
        );
        return ResponseEntity.status(HttpStatus.CONFLICT).body(response);
    }

    private HttpStatus statusFor(StoryErrorCode code) {
        return switch (code) {
            case PROJECT_NOT_FOUND, NODE_NOT_FOUND -> HttpStatus.NOT_FOUND;
            case DUPLICATE_NODE_KEY -> HttpStatus.CONFLICT;
            case INVALID_ENTRY_NODE -> HttpStatus.BAD_REQUEST;
            case STORY_NOT_EXPORTABLE -> HttpStatus.UNPROCESSABLE_ENTITY;
        };
    }
}
