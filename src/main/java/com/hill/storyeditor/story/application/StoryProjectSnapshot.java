package com.hill.storyeditor.story.application;

import com.hill.storyeditor.story.domain.StoryChoice;
import com.hill.storyeditor.story.domain.StoryNode;
import com.hill.storyeditor.story.domain.StoryProject;

import java.time.Instant;
import java.util.List;

public record StoryProjectSnapshot(
    long id,
    long version,
    String title,
    String entryNodeKey,
    Instant createdAt,
    Instant updatedAt,
    List<NodeSnapshot> nodes
) {
    public static StoryProjectSnapshot from(StoryProject project) {
        return new StoryProjectSnapshot(
            project.getId(),
            project.getVersion(),
            project.getTitle(),
            project.getEntryNodeKey(),
            project.getCreatedAt(),
            project.getUpdatedAt(),
            project.getNodes().stream()
                .map(NodeSnapshot::from)
                .toList()
        );
    }

    public record NodeSnapshot(
        long id,
        String nodeKey,
        String title,
        String dialogue,
        List<ChoiceSnapshot> choices
    ) {
        private static NodeSnapshot from(StoryNode node) {
            return new NodeSnapshot(
                node.getId(),
                node.getNodeKey(),
                node.getTitle(),
                node.getDialogue(),
                node.getChoices().stream()
                    .map(ChoiceSnapshot::from)
                    .toList()
            );
        }
    }

    public record ChoiceSnapshot(
        long id,
        String text,
        String targetNodeKey,
        String conditionExpression
    ) {
        private static ChoiceSnapshot from(StoryChoice choice) {
            return new ChoiceSnapshot(
                choice.getId(),
                choice.getText(),
                choice.getTargetNodeKey(),
                choice.getConditionExpression()
            );
        }
    }
}
