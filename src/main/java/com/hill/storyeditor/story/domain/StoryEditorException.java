package com.hill.storyeditor.story.domain;

public final class StoryEditorException extends RuntimeException {

    private final StoryErrorCode code;

    private StoryEditorException(StoryErrorCode code, String message) {
        super(message);
        this.code = code;
    }

    public static StoryEditorException projectNotFound(long projectId) {
        return new StoryEditorException(
            StoryErrorCode.PROJECT_NOT_FOUND,
            "스토리 프로젝트를 찾을 수 없습니다. projectId=" + projectId
        );
    }

    public static StoryEditorException nodeNotFound(String nodeKey) {
        return new StoryEditorException(
            StoryErrorCode.NODE_NOT_FOUND,
            "스토리 노드를 찾을 수 없습니다. nodeKey=" + nodeKey
        );
    }

    public static StoryEditorException duplicateNodeKey(String nodeKey) {
        return new StoryEditorException(
            StoryErrorCode.DUPLICATE_NODE_KEY,
            "한 프로젝트 안에서 노드 키는 중복될 수 없습니다. nodeKey=" + nodeKey
        );
    }

    public static StoryEditorException invalidEntryNode(String nodeKey) {
        return new StoryEditorException(
            StoryErrorCode.INVALID_ENTRY_NODE,
            "시작 노드는 프로젝트 안에 존재해야 합니다. nodeKey=" + nodeKey
        );
    }

    public static StoryEditorException storyNotExportable(long projectId) {
        return new StoryEditorException(
            StoryErrorCode.STORY_NOT_EXPORTABLE,
            "오류가 있는 스토리는 내보낼 수 없습니다. 먼저 validation API를 확인하세요. projectId=" + projectId
        );
    }

    public StoryErrorCode getCode() {
        return code;
    }
}
