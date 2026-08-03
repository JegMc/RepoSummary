using RepoSummary.Services;

namespace RepoSummary.Tests;

public class ManifestParserTests
{
    private static Dictionary<string, string?> Deps(string fileName, string content) =>
        ManifestParser.ParseDependencies(fileName, content).ToDictionary(x => x.Name, x => x.Version);

    [Fact]
    public void Csproj_reads_package_references_with_and_without_versions()
    {
        var content = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Serilog" Version="3.1.1" />
                <PackageReference Include="Newtonsoft.Json" />
              </ItemGroup>
            </Project>
            """;

        var deps = Deps("App.csproj", content);

        Assert.Equal("3.1.1", deps["Serilog"]);
        Assert.True(deps.ContainsKey("Newtonsoft.Json"));
        Assert.Null(deps["Newtonsoft.Json"]);
    }

    [Fact]
    public void PackageJson_reads_dependencies_and_devDependencies()
    {
        var content = """{ "dependencies": { "react": "^18.2.0" }, "devDependencies": { "vitest": "1.0.0" } }""";

        var deps = Deps("package.json", content);

        Assert.Equal("^18.2.0", deps["react"]);
        Assert.Equal("1.0.0", deps["vitest"]);
    }

    [Fact]
    public void ComposerJson_reads_require_sections()
    {
        var content = """{ "require": { "laravel/framework": "^10.0" } }""";

        var deps = Deps("composer.json", content);

        Assert.Equal("^10.0", deps["laravel/framework"]);
    }

    [Fact]
    public void RequirementsTxt_parses_names_and_pinned_versions()
    {
        var content = "flask==2.3.0\n# a comment\nrequests>=2.0\n";

        var deps = Deps("requirements.txt", content);

        Assert.Equal("2.3.0", deps["flask"]);
        Assert.True(deps.ContainsKey("requests"));
        Assert.False(deps.ContainsKey("# a comment"));
    }

    [Fact]
    public void GoMod_parses_require_lines()
    {
        var content = "module example.com/x\n\nrequire (\n\tgithub.com/gin-gonic/gin v1.9.1\n)\n";

        var deps = Deps("go.mod", content);

        Assert.Equal("v1.9.1", deps["github.com/gin-gonic/gin"]);
    }

    [Fact]
    public void Gemfile_parses_gems_with_optional_versions()
    {
        var content = "gem 'rails', '7.0.0'\ngem 'puma'\n";

        var deps = Deps("Gemfile", content);

        Assert.Equal("7.0.0", deps["rails"]);
        Assert.True(deps.ContainsKey("puma"));
    }

    [Fact]
    public void Unknown_manifest_returns_nothing()
    {
        Assert.Empty(ManifestParser.ParseDependencies("unknown.txt", "whatever"));
    }

    [Fact]
    public void Malformed_json_returns_nothing()
    {
        Assert.Empty(ManifestParser.ParseDependencies("package.json", "{ not valid json"));
    }

    [Theory]
    [InlineData("^1.2.3", "1.2.3")]
    [InlineData("~2.0", "2.0")]
    [InlineData("v3.1.0", "3.1.0")]
    [InlineData(">=1.0", "1.0")]
    [InlineData("\"1.0.0\"", "1.0.0")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public void CleanVersion_normalizes(string? input, string? expected)
    {
        Assert.Equal(expected, ManifestParser.CleanVersion(input));
    }

    [Fact]
    public void Workflow_detects_high_level_actions()
    {
        var yaml = """
            jobs:
              ci:
                steps:
                  - run: dotnet test
                  - run: docker build -t app .
                  - uses: github/codeql-action/analyze@v3
            """;

        var does = ManifestParser.DetectWorkflowActions(yaml);

        Assert.Contains("runs tests", does);
        Assert.Contains("Docker", does);
        Assert.Contains("code scanning", does);
    }
}
