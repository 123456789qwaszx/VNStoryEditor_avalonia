using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Vn.App.Services;

/// <summary>
/// 앱 자체의 설정. 프로젝트가 아니라 이 컴퓨터의 이 사용자에게 딸린 것이다.
///
/// 최근 연 프로젝트는 프로젝트 폴더에 둘 수 없다 — 아직 아무 프로젝트도 안 열었을 때
/// 읽어야 하는 값이기 때문이다. 그래서 <c>vn.workspace.json</c> 옆이 아니라 여기 둔다.
///
/// 읽지 못하면 조용히 비어 있는 것으로 본다. 편의 기능 하나 때문에 앱이 안 열리면 안 된다.
/// </summary>
internal static class AppSettingsService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
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

    /// <summary>마지막으로 연 프로젝트 경로. 없거나 읽을 수 없으면 null이다.</summary>
    public static string? LoadRecentProject()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(SettingsPath),
                ReadOptions);

            string? path = settings?.RecentProject;

            // 지워진 프로젝트를 계속 들이밀지 않는다.
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                ? null
                : path;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                NotSupportedException or
                ArgumentException)
        {
            return null;
        }
    }

    public static void SaveRecentProject(string projectPath)
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
                JsonSerializer.Serialize(
                    new AppSettings { RecentProject = projectPath },
                    WriteOptions),
                new UTF8Encoding(false));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException or
                ArgumentException)
        {
            // 기억하지 못해도 경로를 다시 고르면 그만이다.
        }
    }

    private sealed class AppSettings
    {
        public string? RecentProject { get; set; }
    }
}
