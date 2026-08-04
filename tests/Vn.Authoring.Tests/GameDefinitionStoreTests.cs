using Vn.Authoring.Definition;

namespace Vn.Authoring.Tests;

/// <summary>
/// X5 — 화자 목록의 원천은 game.definition.json 하나다 (D-4).
/// 쓰기는 speakers만 바꾸고 나머지 키(변수·카탈로그)는 그대로 보존해야 한다.
/// </summary>
public class GameDefinitionStoreTests
{
    private static string TempProject()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.GameDef.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "project.vnproject.json");
    }

    private static SpeakerSpec Speaker(string name, string characterId) =>
        new() { Name = name, CharacterId = characterId };

    [Fact]
    public void 파일이_없으면_만들고_speakers를_쓴다()
    {
        string projectPath = TempProject();

        try
        {
            GameDefinitionStore.SaveSpeakers(projectPath, [Speaker("라루", "laru")]);

            GameDefinition definition = GameDefinition.LoadBeside(projectPath);
            Assert.Equal("laru", definition.FindSpeakerCharacterId("라루"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(projectPath)!, recursive: true);
        }
    }

    [Fact]
    public void 다른_키는_보존하고_speakers만_바꾼다()
    {
        string projectPath = TempProject();
        string definitionPath = GameDefinition.PathFor(projectPath);

        try
        {
            File.WriteAllText(definitionPath, """
                {
                  "variables": [ { "name": "favor", "type": "number" } ],
                  "speakers": [ { "name": "옛화자", "characterId": "old" } ],
                  "presentationCommandCategories": [ { "id": "camera", "name": "Camera" } ]
                }
                """);

            GameDefinitionStore.SaveSpeakers(
                projectPath,
                [Speaker("라루", "laru"), Speaker("윌로", "willo"), Speaker("  ", "무시됨")]);

            GameDefinition definition = GameDefinition.LoadBeside(projectPath);

            // speakers는 교체됐고(빈 이름은 걸러짐) —
            Assert.Equal(["라루", "윌로"], definition.Speakers.Select(item => item.Name));
            Assert.Null(definition.FindSpeakerCharacterId("옛화자"));

            // — 나머지 어휘는 그대로다.
            Assert.Single(definition.Variables);
            Assert.Equal("favor", definition.Variables[0].Name);
            Assert.Single(definition.PresentationCommandCategories);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(projectPath)!, recursive: true);
        }
    }

    [Fact]
    public void 깨진_파일_위에는_덮어쓰지_않는다()
    {
        string projectPath = TempProject();
        string definitionPath = GameDefinition.PathFor(projectPath);

        try
        {
            File.WriteAllText(definitionPath, "{ 이건 JSON이 아님");

            Assert.Throws<InvalidDataException>(() =>
                GameDefinitionStore.SaveSpeakers(projectPath, [Speaker("라루", "laru")]));

            // 원본은 그대로 — 카탈로그를 잃는 것보다 거부가 낫다.
            Assert.Contains("이건 JSON이 아님", File.ReadAllText(definitionPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(projectPath)!, recursive: true);
        }
    }
}
