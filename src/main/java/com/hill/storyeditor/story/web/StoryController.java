package com.hill.storyeditor.story.web;

import com.hill.storyeditor.story.application.StoryEditorService;
import com.hill.storyeditor.story.application.StoryExportDocument;
import com.hill.storyeditor.story.application.StoryProjectSnapshot;
import com.hill.storyeditor.story.application.StoryValidationReport;
import com.hill.storyeditor.story.web.StoryApiModels.ChangeEntryNodeRequest;
import com.hill.storyeditor.story.web.StoryApiModels.CreateChoiceRequest;
import com.hill.storyeditor.story.web.StoryApiModels.CreateNodeRequest;
import com.hill.storyeditor.story.web.StoryApiModels.CreateProjectRequest;
import jakarta.validation.Valid;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/projects")
public class StoryController {

    private final StoryEditorService storyEditorService;

    public StoryController(StoryEditorService storyEditorService) {
        this.storyEditorService = storyEditorService;
    }

    @PostMapping
    public ResponseEntity<StoryProjectSnapshot> createProject(
        @Valid @RequestBody CreateProjectRequest request
    ) {
        StoryProjectSnapshot result = storyEditorService.createProject(
            request.title()
        );
        return ResponseEntity.status(HttpStatus.CREATED).body(result);
    }

    @PostMapping("/{projectId}/nodes")
    public ResponseEntity<StoryProjectSnapshot> addNode(
        @PathVariable long projectId,
        @Valid @RequestBody CreateNodeRequest request
    ) {
        StoryProjectSnapshot result = storyEditorService.addNode(
            projectId,
            request.nodeKey(),
            request.title(),
            request.dialogue()
        );
        return ResponseEntity.status(HttpStatus.CREATED).body(result);
    }

    @PostMapping("/{projectId}/nodes/{sourceNodeKey}/choices")
    public ResponseEntity<StoryProjectSnapshot> addChoice(
        @PathVariable long projectId,
        @PathVariable String sourceNodeKey,
        @Valid @RequestBody CreateChoiceRequest request
    ) {
        StoryProjectSnapshot result = storyEditorService.addChoice(
            projectId,
            sourceNodeKey,
            request.text(),
            request.targetNodeKey(),
            request.conditionExpression()
        );
        return ResponseEntity.status(HttpStatus.CREATED).body(result);
    }

    @PutMapping("/{projectId}/entry-node")
    public StoryProjectSnapshot changeEntryNode(
        @PathVariable long projectId,
        @Valid @RequestBody ChangeEntryNodeRequest request
    ) {
        return storyEditorService.changeEntryNode(
            projectId,
            request.nodeKey()
        );
    }

    @GetMapping("/{projectId}")
    public StoryProjectSnapshot getProject(
        @PathVariable long projectId
    ) {
        return storyEditorService.getProjectSnapshot(projectId);
    }

    @GetMapping("/{projectId}/validation")
    public StoryValidationReport validateProject(
        @PathVariable long projectId
    ) {
        return storyEditorService.validateProject(projectId);
    }

    @GetMapping("/{projectId}/export")
    public ResponseEntity<StoryExportDocument> exportProject(
        @PathVariable long projectId
    ) {
        StoryExportDocument document = storyEditorService.exportProject(projectId);
        String filename = "story-project-" + projectId + ".json";

        return ResponseEntity.ok()
            .contentType(MediaType.APPLICATION_JSON)
            .header(
                HttpHeaders.CONTENT_DISPOSITION,
                "attachment; filename=\"" + filename + "\""
            )
            .body(document);
    }
}
