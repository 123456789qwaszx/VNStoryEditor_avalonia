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
        return LoadRecentProject(SettingsPath);
    }

    internal static string? LoadRecentProject(string settingsPath)
    {
        AppSettings settings = Load(settingsPath);
        string? path = settings.RecentProject;

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return File.Exists(path) ? path : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException or
                IOException or
                UnauthorizedAccessException)
        {
            // 설정에 남은 경로가 형식부터 깨져 있어도 앱은 계속 시작해야 한다.
            return null;
        }
    }

    public static void SaveRecentProject(string projectPath)
    {
        AppSettings settings = Load(SettingsPath);
        settings.RecentProject = NormalizeProjectKey(projectPath);
        Save(settings);
    }

    /// <summary>재생 속도 배율 (W34-a). 형식이 깨져 있으면 1배로 돌아간다.</summary>
    public static double LoadPlaybackSpeed()
    {
        double speed = Load(SettingsPath).PlaybackSpeed ?? 1;
        return double.IsFinite(speed) && speed > 0 ? speed : 1;
    }

    public static void SavePlaybackSpeed(double speed)
    {
        AppSettings settings = Load(SettingsPath);
        settings.PlaybackSpeed = speed;
        Save(settings);
    }

    /// <summary>미리 듣기 볼륨 (W63) — 0..1. 형식이 깨져 있으면 1(원음)로 돌아간다.</summary>
    public static (double Bgm, double Sfx) LoadAudioVolumes()
    {
        AppSettings settings = Load(SettingsPath);
        return (ClampVolume(settings.BgmVolume), ClampVolume(settings.SfxVolume));
    }

    public static void SaveAudioVolumes(double bgm, double sfx)
    {
        AppSettings settings = Load(SettingsPath);
        settings.BgmVolume = ClampVolume(bgm);
        settings.SfxVolume = ClampVolume(sfx);
        Save(settings);
    }

    private static double ClampVolume(double? volume) =>
        volume is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;

    /// <summary>
    /// 설정 파일이 없거나, 잘린 JSON이거나, 형이 전혀 다른 내용이어도 기본값으로 돌아간다.
    /// 편의 설정 하나 때문에 앱이 시작하지 못하는 일은 없어야 한다.
    /// </summary>
    internal static AppSettings Load(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(
                       File.ReadAllText(settingsPath),
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

    internal sealed class AppSettings
    {
        public string? RecentProject { get; set; }

        /// <summary>재생 속도 배율. null이면 1배 — 없던 설정 파일과의 호환.</summary>
        public double? PlaybackSpeed { get; set; }

        /// <summary>미리 듣기 BGM 볼륨 0..1 (W63). null이면 원음.</summary>
        public double? BgmVolume { get; set; }

        /// <summary>미리 듣기 효과음 볼륨 0..1 (W63). null이면 원음.</summary>
        public double? SfxVolume { get; set; }
    }
}
