package com.hill.storyeditor.story.persistence;

import com.hill.storyeditor.story.domain.StoryProject;
import org.springframework.data.jpa.repository.JpaRepository;

public interface StoryProjectRepository extends JpaRepository<StoryProject, Long> {
}
