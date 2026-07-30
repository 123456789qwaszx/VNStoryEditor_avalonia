package com.hill.storyeditor.story.web;

import com.jayway.jsonpath.JsonPath;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest
@AutoConfigureMockMvc
class StoryEditorApiTest {

    @Autowired
    private MockMvc mockMvc;


    @Test
    void completeVerticalSliceFromCreationToExport() throws Exception {
        String createProjectBody = mockMvc.perform(post("/api/projects")
                .contentType(MediaType.APPLICATION_JSON)
                .content("""
                    {
                      "title": "마녀의 게스트하우스"
                    }
                    """))
            .andExpect(status().isCreated())
            .andExpect(jsonPath("$.title").value("마녀의 게스트하우스"))
            .andReturn()
            .getResponse()
            .getContentAsString();

        Number projectIdValue = JsonPath.read(createProjectBody, "$.id");
        long projectId = projectIdValue.longValue();

        addNode(projectId, "Start", "시작", "낡은 문이 열린다.");

        mockMvc.perform(post("/api/projects/{projectId}/nodes/{source}/choices", projectId, "Start")
                .contentType(MediaType.APPLICATION_JSON)
                .content("""
                    {
                      "text": "복도로 나간다",
                      "targetNodeKey": "Hallway",
                      "conditionExpression": "courage >= 2"
                    }
                    """))
            .andExpect(status().isCreated());

        mockMvc.perform(get("/api/projects/{projectId}/validation", projectId))
            .andExpect(status().isOk())
            .andExpect(jsonPath("$.valid").value(false))
            .andExpect(jsonPath("$.issues[0].code").value("DANGLING_CHOICE"));

        addNode(projectId, "Hallway", "복도", "긴 복도가 이어진다.");

        mockMvc.perform(get("/api/projects/{projectId}/validation", projectId))
            .andExpect(status().isOk())
            .andExpect(jsonPath("$.valid").value(true))
            .andExpect(jsonPath("$.issues").isEmpty());

        mockMvc.perform(get("/api/projects/{projectId}/export", projectId))
            .andExpect(status().isOk())
            .andExpect(header().string(
                "Content-Disposition",
                "attachment; filename=\"story-project-" + projectId + ".json\""
            ))
            .andExpect(jsonPath("$.schemaVersion").value(1))
            .andExpect(jsonPath("$.entryNodeKey").value("Start"))
            .andExpect(jsonPath("$.nodes.length()").value(2));
    }

    private void addNode(
        long projectId,
        String nodeKey,
        String title,
        String dialogue
    ) throws Exception {
        String body = """
            {
              "nodeKey": "%s",
              "title": "%s",
              "dialogue": "%s"
            }
            """.formatted(nodeKey, title, dialogue);

        mockMvc.perform(post("/api/projects/{projectId}/nodes", projectId)
                .contentType(MediaType.APPLICATION_JSON)
                .content(body))
            .andExpect(status().isCreated());
    }

}
