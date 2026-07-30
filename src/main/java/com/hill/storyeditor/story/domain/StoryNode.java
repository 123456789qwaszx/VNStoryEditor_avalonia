package com.hill.storyeditor.story.domain;

import jakarta.persistence.CascadeType;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.FetchType;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.OneToMany;
import jakarta.persistence.OrderBy;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.regex.Pattern;

@Entity
@Table(
    name = "story_nodes",
    uniqueConstraints = @UniqueConstraint(
        name = "uk_story_node_project_key",
        columnNames = {"project_id", "node_key"}
    )
)
public class StoryNode {

    private static final Pattern NODE_KEY_PATTERN =
        Pattern.compile("[A-Za-z][A-Za-z0-9_]{0,79}");

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "node_key", nullable = false, length = 80)
    private String nodeKey;

    @Column(nullable = false, length = 120)
    private String title;

    @Column(nullable = false, length = 10_000)
    private String dialogue;

    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "project_id", nullable = false)
    private StoryProject project;

    @OneToMany(
        mappedBy = "sourceNode",
        cascade = CascadeType.ALL,
        orphanRemoval = true
    )
    @OrderBy("id ASC")
    private List<StoryChoice> choices = new ArrayList<>();

    protected StoryNode() {
        // JPA 전용 생성자다.
    }

    StoryNode(
        StoryProject project,
        String nodeKey,
        String title,
        String dialogue
    ) {
        this.project = project;
        this.nodeKey = normalizeNodeKey(nodeKey);
        this.title = requireText(title, "노드 제목", 120);
        this.dialogue = requireText(dialogue, "대사", 10_000);
    }

    void addChoice(
        String text,
        String targetNodeKey,
        String conditionExpression
    ) {
        choices.add(new StoryChoice(
            this,
            text,
            targetNodeKey,
            conditionExpression
        ));
    }

    static String normalizeNodeKey(String nodeKey) {
        if (nodeKey == null) {
            throw new IllegalArgumentException("노드 키는 비어 있을 수 없습니다.");
        }

        String normalized = nodeKey.trim();
        if (!NODE_KEY_PATTERN.matcher(normalized).matches()) {
            throw new IllegalArgumentException(
                "노드 키는 영문자로 시작하고 영문자, 숫자, 밑줄만 사용할 수 있습니다."
            );
        }
        return normalized;
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

    public Long getId() {
        return id;
    }

    public String getNodeKey() {
        return nodeKey;
    }

    public String getTitle() {
        return title;
    }

    public String getDialogue() {
        return dialogue;
    }

    public List<StoryChoice> getChoices() {
        return Collections.unmodifiableList(choices);
    }
}
