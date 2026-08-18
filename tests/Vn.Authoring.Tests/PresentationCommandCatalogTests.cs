using Vn.Authoring.Definition;

namespace Vn.Authoring.Tests;

public class PresentationCommandCatalogTests
{
    [Fact]
    public void 기본_카탈로그는_내장된_런타임_카탈로그_데이터다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(GameDefinition.Empty);

        // docs/game.definition.json — 런타임 등록 테이블과 교차 검증한다.
        //
        // 2026-08-18 재검증: 런타임의 `AddCommandHandler` 등록 이름 **178개와 정확히
        // 일치**하고 런타임에만 있는 것은 0개다. 카탈로그에만 남아 있던 22개
        // (`pres_*` 6 · `overlay_*` 13 · `beat` · `beat_fx` · `seq`)는 단순화로 사라진
        // 커맨드라 걷어냈다 — 팔레트에 남겨 두면 작가가 고르는 순간 unknown command다.
        // (179 = 런타임 등록 이름 178 + 동적 별칭 템플릿 항목 `<N>fr` 하나.)
        Assert.Equal(179, catalog.Definitions.Count);
        Assert.Equal(20, catalog.Categories.Count);
        Assert.Same(catalog, PresentationCommandCatalog.For(definition: null));
    }

    [Fact]
    public void 기본_카탈로그의_커맨드는_순서_있는_파라미터를_가진다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.Default;

        PresentationCommandDefinition hop = catalog.Find("char_rig_acting.hop")!;
        Assert.Equal("hop", hop.OutputCommandName);
        Assert.Equal("char_rig_acting", hop.CategoryId);
        PresentationCommandParameter slot = Assert.Single(hop.Parameters);
        Assert.Equal("slot", slot.Name);
        Assert.True(slot.Required);

        // 기본값은 파라미터에 실려 온다. hop처럼 기본값 없는 필수 인자는 빈 사전이다.
        Assert.Empty(hop.DefaultArgumentValues());

        // 기본값이 실려 오는 예 — `control_flow.pres_hold`가 이 자리였는데 2026-08-18에
        // 카탈로그에서 걷혔다(런타임에 `pres_*`가 없다).
        PresentationCommandDefinition pause = catalog.Find("common_control.pause")!;
        Assert.Equal("0.18", pause.DefaultArgumentValues()["seconds"]);
    }

    [Fact]
    public void 메인_레인_전용_표시는_박스_셋만_남았다()
    {
        // 2026-08-18 — 옛 11개 중 `pres_*` 계열이 카탈로그에서 사라져 셋만 남았다.
        //
        // ⚠ 이 플래그는 지금 **아무도 읽지 않는다.** 이미터의 `IsMainLaneOnly` 검사가
        // 유일한 소비자였는데, 레인이 하나뿐이라 가릴 대상을 잃어 함께 걷혔다.
        // 데이터로는 남겨 둔다 — 레인이 다시 생기면 검사도 같이 돌아온다.
        string[] mainLaneOnly = PresentationCommandCatalog.Default.Definitions
            .Where(item => item.MainLaneOnly)
            .Select(item => item.OutputCommandName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "box_named", "box_protagonist", "box_reset" }, mainLaneOnly);
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
