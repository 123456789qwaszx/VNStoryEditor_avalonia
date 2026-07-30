package com.hill.storyeditor.story.domain;

import org.springframework.stereotype.Component;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

@Component
public final class StoryGraphValidator {

    public List<ValidationIssue> validate(StoryProject project) {
        List<ValidationIssue> issues = new ArrayList<>();

        if (project.getNodes().isEmpty()) {
            issues.add(ValidationIssue.error(
                "NO_NODES",
                "프로젝트에 노드가 하나도 없습니다.",
                null,
                null
            ));
            return List.copyOf(issues);
        }

        Map<String, StoryNode> nodesByKey = indexNodes(project.getNodes());
        findDanglingChoices(project.getNodes(), nodesByKey, issues);

        String entryNodeKey = project.getEntryNodeKey();
        if (entryNodeKey == null || !nodesByKey.containsKey(entryNodeKey)) {
            issues.add(ValidationIssue.error(
                "ENTRY_NODE_MISSING",
                "유효한 시작 노드가 지정되어 있지 않습니다.",
                entryNodeKey,
                null
            ));
            return List.copyOf(issues);
        }

        Set<String> reachableNodeKeys = findReachableNodes(
            entryNodeKey,
            nodesByKey
        );

        for (StoryNode node : project.getNodes()) {
            if (!reachableNodeKeys.contains(node.getNodeKey())) {
                issues.add(ValidationIssue.warning(
                    "UNREACHABLE_NODE",
                    "시작 노드에서 도달할 수 없는 노드입니다.",
                    node.getNodeKey()
                ));
            }
        }

        return List.copyOf(issues);
    }

    public boolean hasErrors(List<ValidationIssue> issues) {
        return issues.stream()
            .anyMatch(issue -> issue.severity() == ValidationSeverity.ERROR);
    }

    private Map<String, StoryNode> indexNodes(List<StoryNode> nodes) {
        Map<String, StoryNode> nodesByKey = new HashMap<>();
        for (StoryNode node : nodes) {
            nodesByKey.put(node.getNodeKey(), node);
        }
        return nodesByKey;
    }

    private void findDanglingChoices(
        List<StoryNode> nodes,
        Map<String, StoryNode> nodesByKey,
        List<ValidationIssue> issues
    ) {
        for (StoryNode node : nodes) {
            for (StoryChoice choice : node.getChoices()) {
                if (!nodesByKey.containsKey(choice.getTargetNodeKey())) {
                    issues.add(ValidationIssue.error(
                        "DANGLING_CHOICE",
                        "선택지가 존재하지 않는 노드를 가리킵니다: "
                            + choice.getTargetNodeKey(),
                        node.getNodeKey(),
                        choice.getText()
                    ));
                }
            }
        }
    }

    private Set<String> findReachableNodes(
        String entryNodeKey,
        Map<String, StoryNode> nodesByKey
    ) {
        Set<String> visited = new HashSet<>();
        Deque<String> waiting = new ArrayDeque<>();
        waiting.add(entryNodeKey);

        while (!waiting.isEmpty()) {
            String currentNodeKey = waiting.removeFirst();
            if (!visited.add(currentNodeKey)) {
                continue;
            }

            StoryNode currentNode = nodesByKey.get(currentNodeKey);
            for (StoryChoice choice : currentNode.getChoices()) {
                String targetNodeKey = choice.getTargetNodeKey();
                if (nodesByKey.containsKey(targetNodeKey)) {
                    waiting.addLast(targetNodeKey);
                }
            }
        }

        return visited;
    }
}
