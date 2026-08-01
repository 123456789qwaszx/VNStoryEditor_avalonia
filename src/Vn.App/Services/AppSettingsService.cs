using System.Text;
using System.Text.Json;

namespace Vn.App.Services;

/// <summary>
/// 프로젝트 원본과 무관한 사용자별 편의 설정.
/// 읽기·쓰기에 실패해도 저작 작업 자체를 막지 않는다.
/// </summary>
internal static class AppSettingsService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VnTool",
        "settings.json");

    public static string? LoadRecentProject()
    {
        AppSettings settings = Load();
        string? path = settings.RecentProject;

        return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            ? null
            : path;
    }

    public static string? LoadRecentNode(string projectPath)
    {
        string key = NormalizeProjectKey(projectPath);
        AppSettings settings = Load();

        return settings.RecentNodes.TryGetValue(key, out string? title) &&
            !string.IsNullOrWhiteSpace(title)
                ? title
                : null;
    }

    public static void SaveRecentProject(string projectPath)
    {
        AppSettings settings = Load();
        settings.RecentProject = Path.GetFullPath(projectPath);
        Save(settings);
    }

    public static void SaveRecentNode(string projectPath, string nodeTitle)
    {
        if (string.IsNullOrWhiteSpace(nodeTitle))
        {
            return;
        }

        AppSettings settings = Load();
        settings.RecentNodes[NormalizeProjectKey(projectPath)] = nodeTitle;
        Save(settings);
    }

    private static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(
                       File.ReadAllText(SettingsPath),
                       ReadOptions)
                   ?? new AppSettings();
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                NotSupportedException or
                ArgumentException)
        {
            return new AppSettings();
        }
    }

    private static void Save(AppSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(SettingsPath);

            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(settings, WriteOptions),
                new UTF8Encoding(false));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException or
                ArgumentException)
        {
            // 편의 설정을 기억하지 못해도 프로젝트와 원고는 계속 사용할 수 있다.
        }
    }

    private static string NormalizeProjectKey(string projectPath)
    {
        try
        {
            return Path.GetFullPath(projectPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            return projectPath;
        }
    }

    private sealed class AppSettings
    {
        private Dictionary<string, string> _recentNodes =
            new(StringComparer.OrdinalIgnoreCase);

        public string? RecentProject { get; set; }

        public Dictionary<string, string> RecentNodes
        {
            get => _recentNodes;
            set => _recentNodes = value is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
        }
    }
}
