using System.Text;
using System.Text.Json.Nodes;
using Vn.Authoring.Model;

namespace Vn.Authoring.Serialization;

/// <summary>
/// 프로젝트 manifest와 여러 StoryFile을 물리 파일로 읽고 쓴다.
/// StoryProject 자체는 경로 해석이나 파일 IO를 모른다.
/// </summary>
public static class ProjectStore
{
    public static ProjectLoadResult Load(string path)
    {
        string openedPath = Path.GetFullPath(path);
        string json = File.ReadAllText(openedPath, new UTF8Encoding(false));
        JsonObject root = JsonSupport.ParseObject(json, "VnTool 프로젝트");

        if (LooksLikeManifest(openedPath, root))
        {
            StoryProject project = LoadManifest(openedPath, ProjectManifestJson.Read(json));
            return new ProjectLoadResult(project, openedPath, openedPath, WasMigrated: false);
        }

        if (LooksLikeStandaloneStoryFile(root))
        {
            throw new InvalidDataException(
                "이 파일은 프로젝트가 소유하는 StoryFile입니다. " +
                $"개별 {StoryFileJson.FileExtension} 대신 {ProjectManifestJson.FileExtension} manifest를 여세요.");
        }

        StoryProject migrated = LegacyProjectJson.Read(json, openedPath);
        string manifestPath = SuggestManifestPath(openedPath);
        return new ProjectLoadResult(migrated, manifestPath, openedPath, WasMigrated: true);
    }

    public static void Save(string manifestPath, StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        JsonSupport.ValidateProject(project);

        string fullManifestPath = Path.GetFullPath(manifestPath);
        string rootDirectory = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidOperationException("프로젝트 manifest 디렉터리를 찾을 수 없습니다.");
        Directory.CreateDirectory(rootDirectory);

        // 직렬화와 경로 검증은 디스크를 건드리기 전에 모두 끝낸다.
        // 한 StoryFile의 데이터가 잘못됐다는 이유로 앞 파일만 새 내용으로 바뀌는 일을 줄인다.
        var storyWrites = project.Files
            .Select(file => new PendingWrite(
                ResolveStoryPath(rootDirectory, file.RelativePath),
                StoryFileJson.Write(file)))
            .ToList();
        string manifestText = ProjectManifestJson.Write(project);

        // StoryFile을 먼저 안전하게 교체한 뒤 manifest를 마지막에 교체한다.
        // 새 manifest가 아직 쓰이지 않은 파일을 가리키는 상태를 만들지 않기 위해서다.
        foreach (PendingWrite write in storyWrites)
        {
            JsonSupport.WriteAtomic(write.Path, write.Text);
        }

        JsonSupport.WriteAtomic(fullManifestPath, manifestText);
    }

    public static string DefaultRelativePath(string fileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        return $"story/{fileId}{StoryFileJson.FileExtension}";
    }

    public static string NormalizeRelativeStoryPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("StoryFile 상대 경로가 비어 있습니다.");
        }

        string normalized = relativePath.Replace('\\', '/').Trim();

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException($"StoryFile 경로 '{relativePath}'는 상대 경로여야 합니다.");
        }

        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"StoryFile 경로 '{relativePath}'가 프로젝트 밖을 가리킬 수 있습니다.");
        }

        normalized = string.Join('/', parts);
        if (!normalized.EndsWith(StoryFileJson.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"StoryFile 경로 '{relativePath}'는 {StoryFileJson.FileExtension}으로 끝나야 합니다.");
        }

        return normalized;
    }

    public static string SuggestManifestPath(string legacyPath)
    {
        string fullPath = Path.GetFullPath(legacyPath);
        string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string fileName = Path.GetFileName(fullPath) ?? string.Empty;
        string stem = fileName.EndsWith(StoryFileJson.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^StoryFileJson.FileExtension.Length]
            : Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(directory, stem + ProjectManifestJson.FileExtension);
    }

    private static StoryProject LoadManifest(string manifestPath, ProjectManifest manifest)
    {
        string rootDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidDataException("프로젝트 manifest 디렉터리를 찾을 수 없습니다.");
        var project = new StoryProject
        {
            FormatVersion = StoryProject.CurrentFormatVersion,
            Title = manifest.Title,
            StartNodeId = manifest.StartNodeId
        };

        foreach (ProjectStoryFileReference reference in manifest.Files)
        {
            string storyPath = ResolveStoryPath(rootDirectory, reference.RelativePath);

            if (!File.Exists(storyPath))
            {
                throw new FileNotFoundException(
                    $"프로젝트가 참조하는 StoryFile을 찾을 수 없습니다: {reference.RelativePath}",
                    storyPath);
            }

            StoryFile file = StoryFileJson.Read(File.ReadAllText(storyPath, new UTF8Encoding(false)));

            if (!string.Equals(file.Id, reference.Id, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"manifest의 StoryFile Id '{reference.Id}'와 파일의 Id '{file.Id}'가 다릅니다.");
            }

            if (!string.Equals(file.Name, reference.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"manifest의 StoryFile 이름 '{reference.Name}'과 파일의 이름 '{file.Name}'이 다릅니다.");
            }

            file.RelativePath = reference.RelativePath;
            project.Files.Add(file);
        }

        project.Links.AddRange(manifest.Links.Select(link => link.Clone()));

        JsonSupport.ValidateProject(project);
        return project;
    }

    private static bool LooksLikeStandaloneStoryFile(JsonObject root)
    {
        return (int?)root["formatVersion"] == StoryFileJson.CurrentFormatVersion &&
            root["fileId"] is not null &&
            root["nodes"] is JsonArray;
    }

    private static bool LooksLikeManifest(string path, JsonObject root)
    {
        if (path.EndsWith(ProjectManifestJson.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((int?)root["formatVersion"] != ProjectManifestJson.CurrentFormatVersion ||
            root["files"] is not JsonArray files)
        {
            return false;
        }

        // 빈 manifest도 유효하다. 확장자가 아니라 내용만으로 판정할 때는
        // inline nodes가 하나라도 있으면 이전 aggregate v2로 본다.
        return files.Count > 0 && files.All(item => item is JsonObject file && file["path"] is not null && file["nodes"] is null);
    }

    private sealed record PendingWrite(string Path, string Text);

    private static string ResolveStoryPath(string rootDirectory, string relativePath)
    {
        string normalized = NormalizeRelativeStoryPath(relativePath);
        string fullRoot = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string combined = Path.GetFullPath(Path.Combine(
            fullRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));

        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"StoryFile 경로 '{relativePath}'가 프로젝트 디렉터리 밖을 가리킵니다.");
        }

        return combined;
    }
}

public sealed record ProjectLoadResult(
    StoryProject Project,
    string ManifestPath,
    string OpenedPath,
    bool WasMigrated);
