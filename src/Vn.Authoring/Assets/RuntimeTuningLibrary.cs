using System.Text.Json;
using Ked.Presentation.Core;

namespace Vn.Authoring.Assets;

/// <summary>
/// 런타임 U12-전체 덤프(<c>ExportedTuning/</c>)를 읽어 코어 <see cref="StageReducerTuning"/>을
/// 채우는 유일한 경로 (W23).
///
/// 별도 DTO를 짓지 않는다 — 코어 <c>Tuning/</c>의 DTO가 곧 덤프의 스키마다(사본 금지).
/// 역직렬화만 여기서 한다: 코어는 외부 의존성 0이라 JSON 파서를 갖지 않고, DTO가
/// 프로퍼티가 아니라 <b>필드</b>라 <see cref="JsonSerializerOptions.IncludeFields"/>가 필요하다.
///
/// 파일이 없어도 예외를 내지 않는다. 저작은 계속되고, 없는 축은 코어가 Unhandled로
/// 남긴다(규칙 14). 무엇이 없고 어디에 놓으면 되는지는 <see cref="Problems"/>가 말한다.
/// </summary>
public sealed class RuntimeTuningLibrary
{
    /// <summary>런타임 내보내기 폴더명 그대로 — 폴더를 통째로 복사하면 규약이 맞는다.</summary>
    public const string DefaultFolderName = "ExportedTuning";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static RuntimeTuningLibrary Empty { get; } = new(
        directory: null,
        tuning: null,
        rigCount: 0,
        portraitDimensionCount: 0,
        hasDepthPresets: false,
        hasFocusTuning: false,
        surfaceLayouts: null,
        problems: Array.Empty<string>());

    private RuntimeTuningLibrary(
        string? directory,
        StageReducerTuning? tuning,
        int rigCount,
        int portraitDimensionCount,
        bool hasDepthPresets,
        bool hasFocusTuning,
        SurfaceLayoutSet? surfaceLayouts,
        IReadOnlyList<string> problems)
    {
        Directory = directory;
        Tuning = tuning;
        RigCount = rigCount;
        PortraitDimensionCount = portraitDimensionCount;
        HasDepthPresets = hasDepthPresets;
        HasFocusTuning = hasFocusTuning;
        SurfaceLayouts = surfaceLayouts;
        Problems = problems;
    }

    /// <summary>해석된 덤프 폴더 절대 경로. null이면 폴더 자체가 없다(미수입 상태).</summary>
    public string? Directory { get; }

    /// <summary>
    /// 코어 리듀서에 넘길 tuning. 폴더가 없으면 null — 프리뷰는 좌표 근사로 계속 간다.
    /// 폴더가 있으면 읽힌 파일만 채워진 채 만들어진다(없는 축은 코어가 Unhandled로 남긴다).
    /// </summary>
    public StageReducerTuning? Tuning { get; }

    public bool IsLoaded => Tuning is not null;

    public int RigCount { get; }

    public int PortraitDimensionCount { get; }

    public bool HasDepthPresets { get; }

    public bool HasFocusTuning { get; }

    /// <summary>
    /// 대사창 surface 레이아웃 (presets/surface-layout.json). 코어 리듀서의 입력이 아니라
    /// 컴포저(대사창 배치)의 입력이라 <see cref="Tuning"/> 밖에 산다. null이면 고정 비율 근사.
    /// </summary>
    public SurfaceLayoutSet? SurfaceLayouts { get; }

    /// <summary>로드 중 발견한 문제 전부 — 없는 파일의 배치 안내 포함. 프리뷰가 그대로 보여 준다.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>상태줄용 요약. 예: "tuning 로드됨 (리그 1종 · depth 프리셋 · focus 튜닝 · 초상 치수 21)".</summary>
    public string Summary
    {
        get
        {
            if (!IsLoaded)
            {
                return "tuning 없음";
            }

            var parts = new List<string>();

            if (RigCount > 0)
            {
                parts.Add($"리그 {RigCount}종");
            }

            if (HasDepthPresets)
            {
                parts.Add("depth 프리셋");
            }

            if (HasFocusTuning)
            {
                parts.Add("focus 튜닝");
            }

            if (PortraitDimensionCount > 0)
            {
                parts.Add($"초상 치수 {PortraitDimensionCount}");
            }

            if (SurfaceLayouts is { Count: > 0 })
            {
                parts.Add($"대사창 레이아웃 {SurfaceLayouts.Count}종");
            }

            return parts.Count == 0
                ? "tuning 폴더는 있지만 읽힌 파일이 없음"
                : "tuning 로드됨 (" + string.Join(" · ", parts) + ")";
        }
    }

    /// <summary>
    /// 덤프 폴더를 읽는다. <paramref name="directory"/>가 null이거나 없으면 배치 안내만 남기고
    /// <see cref="Tuning"/> 없이 돌아간다 — 오류가 아니다.
    /// <paramref name="fallbackResolution"/>은 정의 파일의 프리뷰 해상도다:
    /// 덤프에 기준 해상도가 없을 때 대신 쓰고, 있는데 서로 다르면 경고를 남기되 덤프를 쓴다
    /// (런타임 재현이 목적이므로 런타임이 내보낸 값이 이긴다).
    /// </summary>
    public static RuntimeTuningLibrary Load(string? directory, (double Width, double Height) fallbackResolution)
    {
        var problems = new List<string>();

        if (directory is null || !System.IO.Directory.Exists(directory))
        {
            if (directory is not null)
            {
                problems.Add(
                    $"런타임 tuning 폴더가 없습니다: {directory} — 런타임에서 내보낸 " +
                    $"{DefaultFolderName} 폴더를 통째로 복사해 넣으면 좌표 배치가 실제 값으로 표시됩니다.");
            }

            return new RuntimeTuningLibrary(
                directory: null,
                tuning: null,
                rigCount: 0,
                portraitDimensionCount: 0,
                hasDepthPresets: false,
                hasFocusTuning: false,
                surfaceLayouts: null,
                problems);
        }

        string root = Path.GetFullPath(directory);
        var tuning = new StageReducerTuning();

        // ── 기준 해상도 (base-resolution.json) ──
        BaseResolutionFileDto? baseResolution = ReadFile<BaseResolutionFileDto>(
            root, "base-resolution.json", problems,
            guidance: "기준 해상도가 없어 정의 파일의 프리뷰 해상도를 대신 씁니다.");

        double width = fallbackResolution.Width;
        double height = fallbackResolution.Height;

        if (baseResolution?.referenceResolution is { } resolution && resolution.x > 0 && resolution.y > 0)
        {
            if (Math.Abs(resolution.x - fallbackResolution.Width) > 0.5 ||
                Math.Abs(resolution.y - fallbackResolution.Height) > 0.5)
            {
                problems.Add(
                    $"기준 해상도가 서로 다릅니다: 런타임 덤프 {resolution.x}×{resolution.y} vs " +
                    $"정의 파일 {fallbackResolution.Width}×{fallbackResolution.Height} — " +
                    "좌표 계산에는 런타임 덤프 값을 씁니다. 정의 파일의 preview.resolution을 맞춰 주세요.");
            }

            width = resolution.x;
            height = resolution.y;
        }

        tuning.ReferenceStageWidth = (float)width;
        tuning.BaseResolution = new Vec2((float)width, (float)height);

        // ── 리그 스키마 (rig-schemas.json) ──
        RigSchemasFileDto? rigSchemas = ReadFile<RigSchemasFileDto>(
            root, "rig-schemas.json", problems,
            guidance: "리그 스키마가 없어 슬롯 좌표를 세울 수 없습니다(해당 축은 '반영 안 됨'으로 남습니다).");
        tuning.RigSchemas = rigSchemas;

        // ── depth 프리셋 (presets/depth.json) ──
        DepthTuningFileDto? depth = ReadFile<DepthTuningFileDto>(
            root, Path.Combine("presets", "depth.json"), problems,
            guidance: "size 계열(뎁스)이 '반영 안 됨'으로 남습니다.");
        tuning.DepthPresets = depth?.MonoBehaviour?.presets;

        // ── focus 튜닝 (presets/focus-tuning.json) ──
        FocusTuningFileDto? focus = ReadFile<FocusTuningFileDto>(
            root, Path.Combine("presets", "focus-tuning.json"), problems,
            guidance: "place/size의 focus 오프셋이 기본값 0으로 접힙니다.");
        tuning.FocusTuning = focus?.MonoBehaviour;

        // ── role-anchor 튜닝 (presets/role-anchor.json) — 코어 사본 최신화(W64)로 생긴 축 ──
        RoleAnchorTuningFileDto? roleAnchors = ReadFile<RoleAnchorTuningFileDto>(
            root, Path.Combine("presets", "role-anchor.json"), problems,
            guidance: "show의 캐릭터별 앵커가 기본값으로 접힙니다(비기본 앵커 캐릭터가 어긋납니다).");
        tuning.RoleAnchors = roleAnchors?.MonoBehaviour;

        // ── 초상 치수 (portrait-dimensions.json) ──
        PortraitDimensionsFileDto? portraits = ReadFile<PortraitDimensionsFileDto>(
            root, "portrait-dimensions.json", problems,
            guidance: "초상 사이징이 '반영 안 됨'으로 남습니다.");
        tuning.PortraitDimensions = portraits;

        // ── 대사창 surface 레이아웃 (presets/surface-layout.json) — 컴포저 입력 ──
        SurfaceLayoutFileDto? surfaces = ReadFile<SurfaceLayoutFileDto>(
            root, Path.Combine("presets", "surface-layout.json"), problems,
            guidance: "대사창 배치가 고정 비율 근사로 남습니다.");

        SurfaceLayoutSet? surfaceLayouts = null;

        if (surfaces?.MonoBehaviour?.entries is { Count: > 0 } entries)
        {
            surfaceLayouts = new SurfaceLayoutSet(entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry?.key))
                .Select(entry => new SurfaceLayoutPreset(
                    entry!.key!.Trim(),
                    entry.lineAnchorMin?.x ?? 0,
                    entry.lineAnchorMin?.y ?? 0,
                    entry.lineAnchorMax?.x ?? 1,
                    entry.lineAnchorMax?.y ?? 1,
                    entry.useName,
                    entry.nameAnchorMin?.x ?? 0,
                    entry.nameAnchorMin?.y ?? 0,
                    entry.nameAnchorMax?.x ?? 0,
                    entry.nameAnchorMax?.y ?? 0)));
        }

        return new RuntimeTuningLibrary(
            root,
            tuning,
            rigCount: rigSchemas?.rigs?.Count ?? 0,
            portraitDimensionCount: portraits?.entries?.Count ?? 0,
            hasDepthPresets: tuning.DepthPresets is not null,
            hasFocusTuning: tuning.FocusTuning is not null,
            surfaceLayouts,
            problems);
    }

    /// <summary>
    /// 파일 하나를 읽는다. 없거나 깨졌으면 null + <paramref name="problems"/>에 사유와 배치 안내 —
    /// 어느 파일을 어디에 놓으면 되는지가 항상 남는다(빈칸 안내 철학).
    /// </summary>
    private static T? ReadFile<T>(string root, string relativePath, List<string> problems, string guidance)
        where T : class
    {
        string path = Path.Combine(root, relativePath);
        string display = relativePath.Replace('\\', '/');

        if (!File.Exists(path))
        {
            problems.Add($"tuning 파일이 없습니다: {display} — {guidance}");
            return null;
        }

        try
        {
            T? parsed = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);

            if (parsed is null)
            {
                problems.Add($"tuning 파일이 비어 있습니다: {display} — {guidance}");
            }

            return parsed;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            problems.Add($"tuning 파일을 읽지 못했습니다: {display} — {exception.Message}");
            return null;
        }
    }

    // base-resolution.json·surface-layout.json은 코어 리듀서의 입력이 아니라 코어에 대응
    // 타입이 없다. 여기 로더 전용으로만 둔다 — 규칙 해석이 아니라 모양이다.
    private sealed class BaseResolutionFileDto
    {
#pragma warning disable CS0649 // 역직렬화가 채우는 필드
        public Float2Dto? referenceResolution;
#pragma warning restore CS0649
    }

#pragma warning disable CS0649 // 역직렬화가 채우는 필드
    private sealed class SurfaceLayoutFileDto
    {
        public SurfaceLayoutBodyDto? MonoBehaviour;
    }

    private sealed class SurfaceLayoutBodyDto
    {
        public List<SurfaceLayoutEntryDto?>? entries;
    }

    private sealed class SurfaceLayoutEntryDto
    {
        public string? key;
        public Float2Dto? lineAnchorMin;
        public Float2Dto? lineAnchorMax;
        public bool useName;
        public Float2Dto? nameAnchorMin;
        public Float2Dto? nameAnchorMax;
    }
#pragma warning restore CS0649
}
