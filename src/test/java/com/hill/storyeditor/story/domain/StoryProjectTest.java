package com.hill.storyeditor.story.domain;

import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class StoryProjectTest {

    @Test
    void firstNodeAutomaticallyBecomesEntryNode() {
        StoryProject project = StoryProject.create("마녀의 게스트하우스");

        project.addNode("Start", "시작", "문이 열린다.");

        assertThat(project.getEntryNodeKey()).isEqualTo("Start");
    }

    @Test
    void duplicateNodeKeyIsRejectedInsideProjectBoundary() {
        StoryProject project = StoryProject.create("마녀의 게스트하우스");
        project.addNode("Start", "시작", "문이 열린다.");

        assertThatThrownBy(() ->
            project.addNode("Start", "다른 시작", "중복 노드")
        )
            .isInstanceOf(StoryEditorException.class)
            .extracting("code")
            .isEqualTo(StoryErrorCode.DUPLICATE_NODE_KEY);
    }

    @Test
    void choiceCanPointToFutureNodeAndValidatorWillCheckItLater() {
        StoryProject project = StoryProject.create("마녀의 게스트하우스");
        project.addNode("Start", "시작", "문이 열린다.");

        project.addChoice(
            "Start",
            "복도로 나간다",
            "Hallway",
            "courage >= 2"
        );

        assertThat(project.findNode("Start").getChoices())
            .singleElement()
            .extracting(StoryChoice::getTargetNodeKey)
            .isEqualTo("Hallway");
    }
}
