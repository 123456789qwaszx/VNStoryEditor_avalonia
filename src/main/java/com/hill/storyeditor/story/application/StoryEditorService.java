package com.hill.storyeditor.story.application;

import com.hill.storyeditor.story.domain.StoryEditorException;
import com.hill.storyeditor.story.domain.StoryGraphValidator;
import com.hill.storyeditor.story.domain.StoryProject;
import com.hill.storyeditor.story.domain.ValidationIssue;
import com.hill.storyeditor.story.persistence.StoryProjectRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;

@Service
@Transactional
public class StoryEditorService {

    private final StoryProjectRepository projectRepository;
    private final StoryGraphValidator graphValidator;

    public StoryEditorService(
        StoryProjectRepository projectRepository,
        StoryGraphValidator graphValidator
    ) {
        this.projectRepository = projectRepository;
        this.graphValidator = graphValidator;
    }

    public StoryProjectSnapshot createProject(String title) {
        StoryProject project = StoryProject.create(title);
        StoryProject savedProject = projectRepository.saveAndFlush(project);
        return StoryProjectSnapshot.from(savedProject);
    }

    public StoryProjectSnapshot addNode(
        long projectId,
        String nodeKey,
        String title,
        String dialogue
    ) {
        StoryProject project = getProject(projectId);
        project.addNode(nodeKey, title, dialogue);
        projectRepository.flush();
        return StoryProjectSnapshot.from(project);
    }

    public StoryProjectSnapshot addChoice(
        long projectId,
        String sourceNodeKey,
        String text,
        String targetNodeKey,
        String conditionExpression
    ) {
        StoryProject project = getProject(projectId);
        project.addChoice(
            sourceNodeKey,
            text,
            targetNodeKey,
            conditionExpression
        );
        projectRepository.flush();
        return StoryProjectSnapshot.from(project);
    }

    public StoryProjectSnapshot changeEntryNode(
        long projectId,
        String nodeKey
    ) {
        StoryProject project = getProject(projectId);
        project.changeEntryNode(nodeKey);
        projectRepository.flush();
        return StoryProjectSnapshot.from(project);
    }

    @Transactional(readOnly = true)
    public StoryProjectSnapshot getProjectSnapshot(long projectId) {
        return StoryProjectSnapshot.from(getProject(projectId));
    }

    @Transactional(readOnly = true)
    public StoryValidationReport validateProject(long projectId) {
        StoryProject project = getProject(projectId);
        List<ValidationIssue> issues = graphValidator.validate(project);
        return StoryValidationReport.from(issues);
    }

    @Transactional(readOnly = true)
    public StoryExportDocument exportProject(long projectId) {
        StoryProject project = getProject(projectId);
        List<ValidationIssue> issues = graphValidator.validate(project);

        if (graphValidator.hasErrors(issues)) {
            throw StoryEditorException.storyNotExportable(projectId);
        }

        return StoryExportDocument.from(project);
    }

    private StoryProject getProject(long projectId) {
        return projectRepository.findById(projectId)
            .orElseThrow(() -> StoryEditorException.projectNotFound(projectId));
    }
}
