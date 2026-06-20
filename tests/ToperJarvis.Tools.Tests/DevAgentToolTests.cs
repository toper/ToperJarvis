using ToperJarvis.Tools.Dev;

namespace ToperJarvis.Tools.Tests;

public class DevAgentToolTests
{
    [Fact]
    public void ParsePlan_czyta_poprawny_json()
    {
        const string json = """
        {
          "project_name": "snake_game",
          "entry_point": "main.py",
          "run_command": "python main.py",
          "files": [
            { "path": "main.py", "description": "Wejście", "imports": ["utils.board"] },
            { "path": "utils/board.py", "description": "Plansza", "imports": [] }
          ],
          "dependencies": ["pygame"]
        }
        """;

        var plan = DevAgentTool.ParsePlan(json);

        Assert.NotNull(plan);
        Assert.Equal("snake_game", plan!.ProjectName);
        Assert.Equal("main.py", plan.EntryPoint);
        Assert.Equal(2, plan.Files.Count);
        Assert.Equal("utils/board.py", plan.Files[1].Path);
        Assert.Single(plan.Dependencies);
    }

    [Fact]
    public void ParsePlan_w_plotkach_markdown()
    {
        const string json = "```json\n{ \"project_name\": \"x\", \"files\": [ { \"path\": \"a.py\" } ] }\n```";

        var plan = DevAgentTool.ParsePlan(json);

        Assert.NotNull(plan);
        Assert.Equal("x", plan!.ProjectName);
        Assert.Single(plan.Files);
    }

    [Fact]
    public void ParsePlan_niepoprawny_json_zwraca_null()
    {
        Assert.Null(DevAgentTool.ParsePlan("to nie jest json"));
    }

    [Theory]
    [InlineData("snake game!", "snake_game")]
    [InlineData("My/Project", "My_Project")]
    [InlineData("", "jarvis_project")]
    [InlineData("   ", "jarvis_project")]
    [InlineData("już-ok", "już-ok")]
    public void SanitizeProjectName_bezpieczna_nazwa(string input, string expected)
    {
        Assert.Equal(expected, DevAgentTool.SanitizeProjectName(input));
    }

    [Theory]
    [InlineData("ModuleNotFoundError: No module named 'requests'", "requests")]
    [InlineData("No module named 'my_pkg.sub'", "my-pkg")] // _→-, obcięte do pierwszego segmentu
    [InlineData("zwykły błąd składni", null)]
    public void ExtractMissingModule_wyluskuje_pakiet(string output, string? expected)
    {
        Assert.Equal(expected, DevAgentTool.ExtractMissingModule(output));
    }

    [Fact]
    public void FindErrorFile_dopasowuje_znany_plik()
    {
        var known = new[] { "main.py", "utils/board.py" };

        Assert.Equal("utils/board.py", DevAgentTool.FindErrorFile("Traceback ... File \"board.py\", line 3", known));
        Assert.Null(DevAgentTool.FindErrorFile("brak pliku w błędzie", known));
    }
}
