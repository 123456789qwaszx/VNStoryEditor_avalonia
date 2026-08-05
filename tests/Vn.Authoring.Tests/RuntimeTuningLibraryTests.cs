using Ked.Presentation.Core;
using Vn.Authoring.Assets;

namespace Vn.Authoring.Tests;

/// <summary>
/// W23 — 런타임 tuning 덤프 수입. 픽스처는 실제 U12-전체 덤프(TuningFixtures/ExportedTuning)다.
/// "폴더를 통째로 복사해 넣으면 JSON을 열지 않고 읽힌다"가 이 로더의 수용 기준이므로,
/// 테스트도 파일을 가공하지 않고 그대로 읽는다.
/// </summary>
public class RuntimeTuningLibraryTests
{
    private static readonly string FixtureDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "ExportedTuning"));

    private static readonly (double Width, double Height) DefinitionResolution = (1920, 1080);

    [Fact]
    public void 실제_덤프_폴더를_그대로_읽는다()
    {
        RuntimeTuningLibrary library = RuntimeTuningLibrary.Load(FixtureDirectory, DefinitionResolution);

        Assert.True(library.IsLoaded);
        StageReducerTuning tuning = library.Tuning!;

        // 기준 해상도 — 덤프의 CanvasScaler 값이 리듀서의 1u 환산 입력이 된다 (D-core-1).
        Assert.Equal(1920f, tuning.ReferenceStageWidth);
        Assert.Equal(1080f, tuning.BaseResolution.Y);

        // 리그 스키마 — 슬롯 폴드가 캐릭터 리그를 세우는 입력.
        Assert.NotNull(tuning.RigSchemas);
        Assert.True(library.RigCount > 0);
        Assert.Contains(tuning.RigSchemas!.rigs, rig => rig.rigKind == "character");

        // depth 프리셋 — size 계열 폴드의 입력. 대표 프리셋 하나가 실제로 조회돼야 한다.
        Assert.NotNull(tuning.DepthPresets);
        Assert.True(tuning.DepthPresets!.TryGet("close", out DepthPresetDto close));
        Assert.NotNull(close.depthY);

        // focus 튜닝 — place/size의 오프셋 입력.
        Assert.NotNull(tuning.FocusTuning);

        // 초상 치수 — 사이징 폴드의 입력.
        Assert.NotNull(tuning.PortraitDimensions);
        Assert.True(library.PortraitDimensionCount > 0);

        // 전 파일이 읽혔으므로 배치 안내가 없어야 한다.
        Assert.Empty(library.Problems);
        Assert.StartsWith("tuning 로드됨", library.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void 리그_스키마가_코어_로더로_실제_트리를_세운다()
    {
        // 수입 경로의 목적은 "코어가 그대로 쓸 수 있는 모양"이다 — DTO 검증에서 멈추지 않고
        // 리듀서 초기 상태에 slot 폴드가 실제로 리그를 세우는 것까지 확인한다.
        RuntimeTuningLibrary library = RuntimeTuningLibrary.Load(FixtureDirectory, DefinitionResolution);
        StageReducerTuning tuning = library.Tuning!;

        StageState state = StageReducer.CreateInitialState(tuning);
        state = StageReducer.Apply(state, new StageCommand("slot", new[] { "c1" }), tuning);

        Assert.True(state.HasSlot("c1"));
        Assert.Empty(state.Unhandled);
        Assert.True(state.Nodes.Contains("c1/CharSlot_Track"));
    }

    [Fact]
    public void 폴더가_null이면_미수입_상태로_조용히_돌아간다()
    {
        RuntimeTuningLibrary library = RuntimeTuningLibrary.Load(null, DefinitionResolution);

        Assert.False(library.IsLoaded);
        Assert.Null(library.Tuning);
        Assert.Empty(library.Problems); // 기본 규약 폴더가 없는 것은 경고가 아니다 — 안내는 화면 몫.
    }

    [Fact]
    public void 지정한_폴더가_없으면_배치_안내를_남긴다()
    {
        string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        RuntimeTuningLibrary library = RuntimeTuningLibrary.Load(missing, DefinitionResolution);

        Assert.False(library.IsLoaded);
        string problem = Assert.Single(library.Problems);
        Assert.Contains("ExportedTuning", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void 파일이_빠지면_어느_파일을_어디에_놓을지_말한다()
    {
        // base-resolution.json 하나만 있는 폴더 — 나머지 네 파일의 배치 안내가 남아야 한다.
        string directory = CreateTempTuningFolder();

        try
        {
            File.Copy(
                Path.Combine(FixtureDirectory, "base-resolution.json"),
                Path.Combine(directory, "base-resolution.json"));

            RuntimeTuningLibrary library = RuntimeTuningLibrary.Load(directory, DefinitionResolution);

            Assert.True(library.IsLoaded); // 부분 수입도 수입이다 — 읽힌 축만 동작한다.
            Assert.Null(library.Tuning!.RigSchemas);
            Assert.Contains(library.Problems, problem => problem.Contains("rig-schemas.json", StringComparison.Ordinal));
            Assert.Contains(library.Problems, problem => problem.Contains("presets/depth.json", StringComparison.Ordinal));
            Assert.Contains(library.Problems, problem => problem.Contains("presets/focus-tuning.json", StringComparison.Ordinal));
            Assert.Contains(library.Problems, problem => problem.Contains("portrait-dimensions.json", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 기준_해상도가_정의와_다르면_경고하되_덤프_값을_쓴다()
    {
        RuntimeTuningLibrary library = RuntimeTuningLibrary.Load(FixtureDirectory, (2560, 1440));

        // 런타임 재현이 목적이므로 리듀서에는 덤프 값(1920)이 들어간다.
        Assert.Equal(1920f, library.Tuning!.ReferenceStageWidth);
        Assert.Contains(
            library.Problems,
            problem => problem.Contains("기준 해상도가 서로 다릅니다", StringComparison.Ordinal));
    }

    [Fact]
    public void 깨진_JSON은_사유를_남기고_그_축만_비운다()
    {
        string directory = CreateTempTuningFolder();

        try
        {
            File.WriteAllText(Path.Combine(directory, "rig-schemas.json"), "{ 이건 JSON이 아니다");

            RuntimeTuningLibrary library = RuntimeTuningLibrary.Load(directory, DefinitionResolution);

            Assert.Null(library.Tuning!.RigSchemas);
            Assert.Contains(
                library.Problems,
                problem => problem.Contains("읽지 못했습니다", StringComparison.Ordinal) &&
                    problem.Contains("rig-schemas.json", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 기준_해상도가_없으면_정의_해상도로_대신한다()
    {
        string directory = CreateTempTuningFolder();

        try
        {
            RuntimeTuningLibrary library = RuntimeTuningLibrary.Load(directory, (2560, 1440));

            Assert.Equal(2560f, library.Tuning!.ReferenceStageWidth);
            Assert.Contains(
                library.Problems,
                problem => problem.Contains("base-resolution.json", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempTuningFolder()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vntool-tuning-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        return directory;
    }
}
