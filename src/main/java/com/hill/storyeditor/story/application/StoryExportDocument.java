package com.hill.storyeditor.story.application;

import com.hill.storyeditor.story.domain.StoryChoice;
import com.hill.storyeditor.story.domain.StoryNode;
import com.hill.storyeditor.story.domain.StoryProject;

import java.time.Instant;
import java.util.List;

public record StoryExportDocument(
    int schemaVersion,
    long projectId,
    long projectVersion,
    String title,
    String entryNodeKey,
    Instant exportedAt,
    List<ExportNode> nodes
) {
    public static StoryExportDocument from(StoryProject project) {
        return new StoryExportDocument(
            1,
            project.getId(),
            project.getVersion(),
            project.getTitle(),
            project.getEntryNodeKey(),
            Instant.now(),
            project.getNodes().stream().map(ExportNode::from).toList()
        );
    }

    public record ExportNode(
        String key,
        String title,
        String dialogue,
        List<ExportChoice> choices
    ) {
        private static ExportNode from(StoryNode node) {
            return new ExportNode(
                node.getNodeKey(),
                node.getTitle(),
                node.getDialogue(),
                node.getChoices().stream().map(ExportChoice::from).toList()
            );
        }
    }

    public record ExportChoice(
        String text,
        String targetNodeKey,
        String condition
    ) {
        private static ExportChoice from(StoryChoice choice) {
            return new ExportChoice(
                choice.getText(),
                choice.getTargetNodeKey(),
                choice.getConditionExpression()
            );
        }
    }
}
