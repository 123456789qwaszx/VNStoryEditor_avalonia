using System.Text;
using System.Text.Json.Nodes;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.Authoring.Serialization;

/// <summary>
/// manifest·대본·StoryFile·발행 결과를 물리 파일로 읽고 쓴다.
/// StoryProject 자체는 경로 해석이나 파일 IO를 모른다.
///
/// 디스크 배치:
/// <code>
/// project.vnproject.json      목차와 파일을 넘나드는 관계
/// script/&lt;scriptId&gt;.vnscript.json   대본 산출물 (줄 정체성 + locale별 본문)
/// story/&lt;fileId&gt;.vnstory.json       노드
/// results.vnresults.json      발행된 불변 결과
/// </code>
/// </summary>
public static class ProjectStore
{
    public static ProjectLoadResult Load(string path)
    {
        string openedPath = Path.GetFullPath(path);
        string json = File.ReadAllText(openedPath, new UTF8Encoding(false));
        JsonObject root = JsonSupport.ParseObject(json, "VnTool 프로젝트");

        if (LooksLikeStandaloneStoryFile(root))
        {
            throw new InvalidDataException(
                "이 파일은 프로젝트가 소유하는 StoryFile입니다. " +
                $"개별 {StoryFileJson.FileExtension} 대신 {ProjectManifestJson.FileExtension} manifest를 여세요.");
        }

        if (LooksLikeStandaloneScript(root))
        {
            throw new InvalidDataException(
                "이 파일은 프로젝트가 소유하는 대본입니다. " +
                $"개별 {ScriptDocumentJson.FileExtension} 대신 {ProjectManifestJson.FileExtension} manifest를 여세요.");
        }

        StoryProject project = LoadManifest(openedPath, ProjectManifestJson.Read(json));
        return new ProjectLoadResult(project, openedPath);
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
        // 한 파일의 데이터가 잘못됐다는 이유로 앞 파일만 새 내용으로 바뀌는 일을 줄인다.
        var writes = new List<PendingWrite>();

        foreach (ScriptDocument script in project.Scripts)
        {
            writes.Add(new PendingWrite(
                Resolve(rootDirectory, DefaultScriptPath(script.Id), ScriptDocumentJson.FileExtension),
                ScriptDocumentJson.Write(script)));
        }

        foreach (StoryFile file in project.Files)
        {
            writes.Add(new PendingWrite(
                Resolve(rootDirectory, file.RelativePath, StoryFileJson.FileExtension),
                StoryFileJson.Write(file)));
        }

        if (!project.Results.IsEmpty)
        {
            writes.Add(new PendingWrite(
                Path.Combine(rootDirectory, ResultStoreJson.DefaultFileName),
                ResultStoreJson.Write(project.Results)));
        }

        string manifestText = ProjectManifestJson.Write(project);

        // 부속 파일을 먼저 안전하게 교체한 뒤 manifest를 마지막에 교체한다.
        // 새 manifest가 아직 쓰이지 않은 파일을 가리키는 상태를 만들지 않기 위해서다.
        foreach (PendingWrite write in writes)
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

    public static string DefaultScriptPath(string scriptId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptId);
        return $"script/{scriptId}{ScriptDocumentJson.FileExtension}";
    }

    public static string NormalizeRelativeStoryPath(string relativePath) =>
        NormalizeRelativePath(relativePath, StoryFileJson.FileExtension);

    /// <summary>
    /// 프로젝트 디렉터리 안의 상대 경로로 정규화한다.
    /// 프로젝트 밖을 가리킬 수 있는 경로는 읽지 않는다. manifest 하나로 임의의 파일을
    /// 덮어쓸 수 있게 되면 프로젝트를 여는 일 자체가 위험해진다.
    /// </summary>
    public static string NormalizeRelativePath(string relativePath, string expectedExtension)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("부속 파일 상대 경로가 비어 있습니다.");
        }

        string normalized = relativePath.Replace('\\', '/').Trim();

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException($"부속 파일 경로 '{relativePath}'는 상대 경로여야 합니다.");
        }

        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"부속 파일 경로 '{relativePath}'가 프로젝트 밖을 가리킬 수 있습니다.");
        }

        normalized = string.Join('/', parts);

        if (!normalized.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"부속 파일 경로 '{relativePath}'는 {expectedExtension}으로 끝나야 합니다.");
        }

        return normalized;
    }

    private static StoryProject LoadManifest(string manifestPath, ProjectManifest manifest)
    {
        string rootDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidDataException("프로젝트 manifest 디렉터리를 찾을 수 없습니다.");
        var project = new StoryProject
        {
            FormatVersion = StoryProject.CurrentFormatVersion,
            Title = manifest.Title,
            StartNodeId = manifest.StartNodeId,
            AssetRoots = manifest.AssetRoots.Clone(),
            RecentCommandIds = manifest.RecentCommandIds.ToList(),
            ExportFormats = manifest.ExportFormats.Clone(),
            OutputPath = manifest.OutputPath
        };

        foreach (ScriptFileReference reference in manifest.Scripts)
        {
            string scriptPath = Resolve(rootDirectory, reference.RelativePath, ScriptDocumentJson.FileExtension);

            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException(
                    $"프로젝트가 참조하는 대본을 찾을 수 없습니다: {reference.RelativePath}",
                    scriptPath);
            }

            ScriptDocument script = ScriptDocumentJson.Read(
                File.ReadAllText(scriptPath, new UTF8Encoding(false)));

            if (!string.Equals(script.Id, reference.Id, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"manifest의 대본 Id '{reference.Id}'와 파일의 Id '{script.Id}'가 다릅니다.");
            }

            project.Scripts.Add(script);
        }

        foreach (ProjectStoryFileReference reference in manifest.Files)
        {
            string storyPath = Resolve(rootDirectory, reference.RelativePath, StoryFileJson.FileExtension);

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

        if (manifest.ResultsRelativePath is { } resultsRelative)
        {
            string resultsPath = Resolve(rootDirectory, resultsRelative, ResultStoreJson.FileExtension);

            if (!File.Exists(resultsPath))
            {
                throw new FileNotFoundException(
                    $"프로젝트가 참조하는 발행 결과 파일을 찾을 수 없습니다: {resultsRelative}",
                    resultsPath);
            }

            ResultRepository results = ResultStoreJson.Read(
                File.ReadAllText(resultsPath, new UTF8Encoding(false)));

            foreach (DialogueResult result in results.DialogueResults)
            {
                project.Results.Add(result);
            }

            foreach (PresentationResult result in results.PresentationResults)
            {
                project.Results.Add(result);
            }
        }

        project.Links.AddRange(manifest.Links.Select(link => link.Clone()));
        project.WriterSpeakers.AddRange(manifest.WriterSpeakers.Select(speaker => speaker.Clone()));
        project.EaseCurves.AddRange(manifest.EaseCurves.Select(curve => curve.Clone()));
        project.Compositions.AddRange(manifest.Compositions.Select(item => item.Clone()));

        JsonSupport.ValidateProject(project);
        return project;
    }

    private static bool LooksLikeStandaloneStoryFile(JsonObject root)
    {
        return (int?)root["formatVersion"] == StoryFileJson.CurrentFormatVersion &&
            root["fileId"] is not null &&
            root["nodes"] is JsonArray;
    }

    private static bool LooksLikeStandaloneScript(JsonObject root)
    {
        return (int?)root["formatVersion"] == ScriptDocumentJson.CurrentFormatVersion &&
            root["scriptId"] is not null &&
            root["lines"] is JsonArray;
    }

    private sealed record PendingWrite(string Path, string Text);

    private static string Resolve(string rootDirectory, string relativePath, string expectedExtension)
    {
        string normalized = NormalizeRelativePath(relativePath, expectedExtension);
        string fullRoot = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string combined = Path.GetFullPath(Path.Combine(
            fullRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));

        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"부속 파일 경로 '{relativePath}'가 프로젝트 디렉터리 밖을 가리킵니다.");
        }

        return combined;
    }
}

public sealed record ProjectLoadResult(StoryProject Project, string ManifestPath);
