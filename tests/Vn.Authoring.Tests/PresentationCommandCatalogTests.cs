using Vn.Authoring.Definition;

namespace Vn.Authoring.Tests;

public class PresentationCommandCatalogTests
{
    [Fact]
    public void 기본_카탈로그는_내장된_런타임_카탈로그_데이터다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(GameDefinition.Empty);

        // docs/game.definition.json — 런타임 등록 테이블과 교차 검증된 201 커맨드, 22 범주.
        Assert.Equal(201, catalog.Definitions.Count);
        Assert.Equal(22, catalog.Categories.Count);
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

        PresentationCommandDefinition presHold = catalog.Find("control_flow.pres_hold")!;
        Assert.True(presHold.MainLaneOnly);
        Assert.Equal("1", presHold.DefaultArgumentValues()["lines"]);
    }

    [Fact]
    public void 기본_카탈로그는_메인_레인_전용_커맨드를_구분한다()
    {
        // 계약서 E2 — 이 11개는 Pres/Set 노드에 출력하면 런타임이 unknown command로 깨진다.
        Assert.Equal(
            11,
            PresentationCommandCatalog.Default.Definitions.Count(item => item.MainLaneOnly));
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
