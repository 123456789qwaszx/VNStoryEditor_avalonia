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

    /// <summary>
    /// 테스트 격리의 손잡이 (2026-08-26) — <b>피해가 양방향이었다.</b> 헤드리스
    /// MainWindow가 이 파일을 진짜 %APPDATA%에서 읽으면 개발자의 실제 최근 프로젝트를
    /// 열어 버리고(OnOpened — 그 프로젝트의 그날 상태에 따라 무관한 테스트들이 무작위로
    /// 죽고, 세션이 실제 원고 폴더에 감시자를 걸고 노드를 더한다), 반대로 테스트가 여는
    /// 임시 프로젝트가 <b>사용자의 진짜 최근 프로젝트를 덮어썼다</b> — 실제로 사용자의
    /// settings.json이 이미 지워진 임시 폴더를 가리키고 있었다.
    ///
    /// 돌려세우는 자리는 <see cref="Vn.App.Tests.TestProcessIsolation"/> 하나다(어셈블리가
    /// 실리는 순간 = 어떤 테스트보다 먼저). ⚠ 경로를 바꾸는 것만으로는 모자란다 —
    /// 복원 자체를 끄는 <see cref="MainWindow.RestoreRecentProjectOnOpen"/>이 짝이다.
    /// 임시 경로가 비어 있어 복원이 <b>조용히 실패하던 것</b>에 기대고 있었을 뿐이라,
    /// 그 폴더에 프로젝트가 남으면 같은 흔들림이 돌아온다.
    /// </summary>
    internal static string? SettingsPathOverride { get; set; }

    public static string SettingsPath => SettingsPathOverride ?? DefaultSettingsPath;

    private static readonly string DefaultSettingsPath = Path.Combine(
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
