using Vn.Authoring.Definition;

namespace Vn.Authoring.Tests;

public class PresentationCommandCatalogTests
{
    [Fact]
    public void 기본_카탈로그는_세_편집_범주에_드롭다운_후보를_제공한다()
    {
        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(GameDefinition.Empty);

        Assert.NotEmpty(catalog.For(PresentationCategory.Camera));
        Assert.NotEmpty(catalog.For(PresentationCategory.ScreenEffect));
        Assert.NotEmpty(catalog.For(PresentationCategory.CharacterActing));
    }

    [Fact]
    public void 게임_정의가_있으면_기본값_대신_사용한다()
    {
        var definition = new GameDefinition
        {
            PresentationCommands =
            {
                new PresentationCommandSpec
                {
                    Id = "camera.custom",
                    Name = "커스텀 카메라",
                    Category = "Camera",
                    OutputCommand = "cam",
                    Arguments = new Dictionary<string, string> { ["preset"] = "custom" }
                }
            }
        };

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(definition);
        PresentationCommandDefinition command = Assert.Single(catalog.Definitions);

        Assert.Equal("camera.custom", command.Id);
        Assert.Equal("cam", command.OutputCommandName);
        Assert.Equal("custom", command.DefaultArguments["preset"]);
    }
}
