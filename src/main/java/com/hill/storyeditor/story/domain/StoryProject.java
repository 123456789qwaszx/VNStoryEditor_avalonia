package com.hill.storyeditor.story.domain;

import jakarta.persistence.CascadeType;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.OneToMany;
import jakarta.persistence.OrderBy;
import jakarta.persistence.Table;
import jakarta.persistence.Version;

import java.time.Instant;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

@Entity
@Table(name = "story_projects")
public class StoryProject {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, length = 100)
    private String title;

    @Column(name = "entry_node_key", length = 80)
    private String entryNodeKey;

    @Version
    private long version;

    @Column(name = "created_at", nullable = false)
    private Instant createdAt;

    @Column(name = "updated_at", nullable = false)
    private Instant updatedAt;

    @OneToMany(
        mappedBy = "project",
        cascade = CascadeType.ALL,
        orphanRemoval = true
    )
    @OrderBy("id ASC")
    private List<StoryNode> nodes = new ArrayList<>();

    protected StoryProject() {
        // JPA 전용 생성자다.
    }

    private StoryProject(String title) {
        this.title = requireTitle(title);
        this.createdAt = Instant.now();
        this.updatedAt = createdAt;
    }

    public static StoryProject create(String title) {
        return new StoryProject(title);
    }

    public StoryNode addNode(
        String nodeKey,
        String nodeTitle,
        String dialogue
    ) {
        String normalizedKey = StoryNode.normalizeNodeKey(nodeKey);
        if (containsNode(normalizedKey)) {
            throw StoryEditorException.duplicateNodeKey(normalizedKey);
        }

        StoryNode node = new StoryNode(
            this,
            normalizedKey,
            nodeTitle,
            dialogue
        );
        nodes.add(node);

        if (entryNodeKey == null) {
            entryNodeKey = normalizedKey;
        }

        touch();
        return node;
    }

    public void addChoice(
        String sourceNodeKey,
        String text,
        String targetNodeKey,
        String conditionExpression
    ) {
        StoryNode sourceNode = findNode(sourceNodeKey);
        sourceNode.addChoice(text, targetNodeKey, conditionExpression);
        touch();
    }

    public void changeEntryNode(String nodeKey) {
        String normalizedKey = StoryNode.normalizeNodeKey(nodeKey);
        if (!containsNode(normalizedKey)) {
            throw StoryEditorException.invalidEntryNode(normalizedKey);
        }
        entryNodeKey = normalizedKey;
        touch();
    }

    public StoryNode findNode(String nodeKey) {
        String normalizedKey = StoryNode.normalizeNodeKey(nodeKey);
        return nodes.stream()
            .filter(node -> node.getNodeKey().equals(normalizedKey))
            .findFirst()
            .orElseThrow(() -> StoryEditorException.nodeNotFound(normalizedKey));
    }

    public boolean containsNode(String nodeKey) {
        return nodes.stream()
            .anyMatch(node -> node.getNodeKey().equals(nodeKey));
    }

    private void touch() {
        updatedAt = Instant.now();
    }

    private static String requireTitle(String title) {
        if (title == null || title.isBlank()) {
            throw new IllegalArgumentException("프로젝트 제목은 비어 있을 수 없습니다.");
        }
        String normalized = title.trim();
        if (normalized.length() > 100) {
            throw new IllegalArgumentException(
                "프로젝트 제목은 100자 이하여야 합니다."
            );
        }
        return normalized;
    }

    public Long getId() {
        return id;
    }

    public String getTitle() {
        return title;
    }

    public String getEntryNodeKey() {
        return entryNodeKey;
    }

    public long getVersion() {
        return version;
    }

    public Instant getCreatedAt() {
        return createdAt;
    }

    public Instant getUpdatedAt() {
        return updatedAt;
    }

    public List<StoryNode> getNodes() {
        return Collections.unmodifiableList(nodes);
    }
}
