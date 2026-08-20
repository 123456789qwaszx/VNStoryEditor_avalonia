using Vn.Authoring.Definition;

namespace Vn.Authoring.Tests;

public class PresentationCommandCatalogTests
{
    [Fact]
    public void 기본_카탈로그는_내장된_런타임_카탈로그_데이터다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(GameDefinition.Empty);

        // docs/game.definition.json — 런타임 등록 테이블과 교차 검증한다.
        // 실제 대조는 아래 `카탈로그_어휘는_런타임_등록_목록과_일치한다`가 진다.
        //
        // 2026-08-20 (W65): 런타임이 연기 커맨드를 스파인으로 넘기며 어휘가 줄었다.
        // 179 → 126 = 삭제 56(emoji 18 · idle 5 · 몸짓 13 · 배경 모션 7 · 표정/시각 4 ·
        // 대사창 종류 3 · 기타 6) + 추가 2(focus_on · focus_clear) + 유지 1(face_swap).
        // 카테고리 20 → 15: emoji_preset·emoji_basic·char_rig_idle·char_rig_acting·
        // char_rig_acting_preset이 통째로 비었다.
        Assert.Equal(126, catalog.Definitions.Count);
        Assert.Equal(15, catalog.Categories.Count);
        Assert.Same(catalog, PresentationCommandCatalog.For(definition: null));
    }

    /// <summary>
    /// 카탈로그 어휘 = 런타임 등록 어휘. 이 둘이 갈리면 작가가 고르는 순간 unknown command이거나
    /// (반대로) 쓸 수 있는 커맨드가 팔레트에 없다. 실측 목록은 픽스처가 지고, 갱신 방법은
    /// 그 파일 머리에 적혀 있다 — 런타임이 커맨드를 늘리거나 줄이면 이 테스트가 먼저 운다.
    /// </summary>
    [Fact]
    public void 카탈로그_어휘는_런타임_등록_목록과_일치한다()
    {
        HashSet<string> runtime = RuntimeCommandFixture.Load();

        HashSet<string> catalog = PresentationCommandCatalog.Default.Definitions
            .Select(item => item.OutputCommandName)
            .ToHashSet(StringComparer.Ordinal);

        // 예외는 둘뿐이고, 둘 다 사유가 있다. 늘리려면 사유를 여기 적어야 한다.
        //   <N>fr    — 1fr~48fr 동적 별칭군을 묶은 합성 항목이라 등록 이름과 글자가 다르다.
        //   face_swap — 툴 전용 표현 (2026-08-20 소유자 결정). 실제 전환은 스파인이 맡는다.
        string[] toolOnly = ["<N>fr", "face_swap"];

        Assert.Equal(
            Array.Empty<string>(),
            catalog.Except(runtime).Except(toolOnly).OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal(
            Array.Empty<string>(),
            runtime.Except(catalog).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void 기본_카탈로그의_커맨드는_순서_있는_파라미터를_가진다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        // 인자 하나짜리 표본. 예전에는 `char_rig_acting.hop`이 이 자리였는데 W65에서
        // 연기 커맨드가 스파인으로 넘어가며 카탈로그에서 빠졌다.
        PresentationCommandDefinition front = catalog.Find("char_rig_staging.sibling_front")!;
        Assert.Equal("sibling_front", front.OutputCommandName);
        Assert.Equal("char_rig_staging", front.CategoryId);
        PresentationCommandParameter slot = Assert.Single(front.Parameters);
        Assert.Equal("slot", slot.Name);
        Assert.True(slot.Required);

        // 기본값은 파라미터에 실려 온다. 기본값 없는 필수 인자뿐이면 빈 사전이다.
        Assert.Empty(front.DefaultArgumentValues());

        // 기본값이 실려 오는 예 — `control_flow.pres_hold`가 이 자리였는데 2026-08-18에
        // 카탈로그에서 걷혔다(런타임에 `pres_*`가 없다).
        PresentationCommandDefinition pause = catalog.Find("common_control.pause")!;
        Assert.Equal("0.18", pause.DefaultArgumentValues()["seconds"]);
    }

    [Fact]
    public void 메인_레인_전용_표시는_이제_아무도_달고_있지_않다()
    {
        // 2026-08-18에 셋(box_named·box_protagonist·box_reset)만 남았었고,
        // W65에서 그 셋이 런타임에서 사라지며 **표시를 단 커맨드가 0이 됐다.**
        //
        // ⚠ 플래그는 읽는 쪽도 없다 — 이미터의 `IsMainLaneOnly` 검사가 유일한 소비자였는데
        // 레인이 하나뿐이라 가릴 대상을 잃고 걷혔다. 이제 **읽는 쪽도 다는 쪽도 없다.**
        // 스키마에서 지우는 것은 레인이 정말 안 돌아온다고 정해질 때 한다(그때 이 테스트도 간다).
        string[] mainLaneOnly = PresentationCommandCatalog.Default.Definitions
            .Where(item => item.MainLaneOnly)
            .Select(item => item.OutputCommandName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(mainLaneOnly);
    }

    [Fact]
    public void 모션_선언은_move_by에만_있고_선언_없는_커맨드는_null이다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        // W66 — "이 커맨드가 무슨 축을 미는가"의 유일한 근거. 이름·정규식 추측 금지.
        PresentationMotionDeclaration motion = catalog.Find("char_rig_staging.move_by")!.Motion!;
        Assert.Equal("CharSlot_Track", motion.NodeId);
        Assert.True(motion.Relative);
        Assert.Equal("duration", motion.DurationParameterName);
        Assert.Equal("ease", motion.EaseParameterName); // W67 — 런타임 다섯째 인자 개통
        Assert.Equal("OutCubic", motion.DefaultEase); // 런타임 스펙 기본값의 기록
        Assert.Equal("x", motion.FindAxis("x")!.ParameterName);
        Assert.Equal("y", motion.FindAxis("y")!.ParameterName);
        Assert.Equal("u", motion.FindAxis("x")!.Unit);

        // ease 파라미터는 다섯째 자리이고 기본값이 없다 — 미지정이면 다섯째 토큰이
        // 통째로 생략돼(트레일링 생략 규칙) 기존 대본이 한 글자도 안 바뀐다.
        PresentationCommandParameter ease = catalog.Find("char_rig_staging.move_by")!.Parameters[^1];
        Assert.Equal("ease", ease.Name);
        Assert.Equal("ease", ease.Type);
        Assert.False(ease.Required);
        Assert.Null(ease.Default);

        // 로더 무해성 — 선언이 없는 커맨드는 예전과 완전히 같다.
        Assert.Null(catalog.Find("char_rig_staging.scale_by")!.Motion);
        Assert.Single(catalog.Definitions, item => item.Motion is not null);
    }

    [Fact]
    public void 범주별_드롭다운_후보는_범주_Id로_거른다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        Assert.NotEmpty(catalog.For("shot"));
        Assert.All(catalog.For("shot"), item => Assert.Equal("shot", item.CategoryId));
        Assert.Empty(catalog.For("없는_범주"));
        Assert.Equal("샷·카메라", catalog.FindCategory("shot")!.DisplayName);
    }

    [Fact]
    public void 게임_정의가_있으면_기본값_대신_사용한다()
    {
        var definition = new GameDefinition
        {
            PresentationCommandCategories =
            {
                new PresentationCategorySpec { Id = "camera", Name = "카메라" }
            },
            PresentationCommands =
            {
                new PresentationCommandSpec
                {
                    Id = "camera.custom",
                    Name = "커스텀 카메라",
                    Category = "camera",
                    OutputCommand = "cam",
                    Parameters =
                    {
                        new PresentationParameterSpec
                        {
                            Name = "preset",
                            Type = "string",
                            Default = "custom"
                        },
                        new PresentationParameterSpec
                        {
                            Name = "duration",
                            Type = "float",
                            Required = true
                        }
                    }
                }
            }
        };

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(definition);
        PresentationCommandDefinition command = Assert.Single(catalog.Definitions);

        Assert.Equal("camera.custom", command.Id);
        Assert.Equal("cam", command.OutputCommandName);
        Assert.Equal("camera", command.CategoryId);
        Assert.Equal(new[] { "preset", "duration" }, command.Parameters.Select(item => item.Name));
        Assert.Equal("custom", command.DefaultArgumentValues()["preset"]);
        Assert.Equal("카메라", Assert.Single(catalog.Categories).DisplayName);
    }

    [Fact]
    public void 범주_선언이_없으면_커맨드가_쓰는_범주를_등장_순서대로_파생한다()
    {
        var definition = new GameDefinition
        {
            PresentationCommands =
            {
                new PresentationCommandSpec { Id = "b.one", Category = "b" },
                new PresentationCommandSpec { Id = "a.one", Category = "a" },
                new PresentationCommandSpec { Id = "b.two", Category = "b" }
            }
        };

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(definition);

        Assert.Equal(new[] { "b", "a" }, catalog.Categories.Select(item => item.Id));
    }
}
