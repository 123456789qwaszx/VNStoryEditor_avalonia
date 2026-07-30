package com.hill.storyeditor.story.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.FetchType;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;

@Entity
@Table(name = "story_choices")
public class StoryChoice {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, length = 200)
    private String text;

    @Column(name = "target_node_key", nullable = false, length = 80)
    private String targetNodeKey;

    @Column(name = "condition_expression", length = 500)
    private String conditionExpression;

    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "source_node_id", nullable = false)
    private StoryNode sourceNode;

    protected StoryChoice() {
        // JPA가 객체를 복원할 때 사용하는 생성자다.
    }

    StoryChoice(
        StoryNode sourceNode,
        String text,
        String targetNodeKey,
        String conditionExpression
    ) {
        this.sourceNode = sourceNode;
        this.text = requireText(text, "선택지 문구", 200);
        this.targetNodeKey = StoryNode.normalizeNodeKey(targetNodeKey);
        this.conditionExpression = normalizeOptional(conditionExpression, 500);
    }

    private static String requireText(
        String value,
        String fieldName,
        int maxLength
    ) {
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException(fieldName + "은 비어 있을 수 없습니다.");
        }

        String normalized = value.trim();
        if (normalized.length() > maxLength) {
            throw new IllegalArgumentException(
                fieldName + "은 " + maxLength + "자 이하여야 합니다."
            );
        }
        return normalized;
    }

    private static String normalizeOptional(String value, int maxLength) {
        if (value == null || value.isBlank()) {
            return null;
        }

        String normalized = value.trim();
        if (normalized.length() > maxLength) {
            throw new IllegalArgumentException(
                "조건식은 " + maxLength + "자 이하여야 합니다."
            );
        }
        return normalized;
    }

    public Long getId() {
        return id;
    }

    public String getText() {
        return text;
    }

    public String getTargetNodeKey() {
        return targetNodeKey;
    }

    public String getConditionExpression() {
        return conditionExpression;
    }
}
