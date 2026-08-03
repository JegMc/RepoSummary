using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Tests;

public class RepoAnalyzerTests
{
    private static GitHubRepoAnalysisResult Analyze(Action<GitHubRepoAnalysisResult> setup)
    {
        var result = new GitHubRepoAnalysisResult { Owner = "o", Name = "n" };
        setup(result);
        RepoAnalyzer.Populate(result);
        return result;
    }

    [Fact]
    public void Detects_ef_core_and_names_the_database_provider()
    {
        var r = Analyze(x =>
        {
            x.AllPaths = new() { "src/App.csproj", "src/Data/AppDbContext.cs", "src/Migrations/Init.cs" };
            x.Packages = new() { new() { Name = "Microsoft.EntityFrameworkCore.SqlServer", Source = "App.csproj" } };
        });

        Assert.Equal("SQL Server", r.DatabaseTech);
        Assert.Contains(r.ResumeAngles, a => a.Text.Contains("Entity Framework Core"));
        Assert.Contains(r.ResumeAngles, a => a.Text.Contains("SQL Server"));
    }

    [Fact]
    public void Detects_aspnet_mvc_and_counts_controllers()
    {
        var r = Analyze(x =>
            x.AllPaths = new() { "Web.csproj", "Controllers/HomeController.cs", "Views/Index.cshtml", "Program.cs" });

        Assert.Equal("ASP.NET Core MVC", r.ArchitecturePattern);
        Assert.Equal(1, r.ControllerCount);
    }

    [Fact]
    public void Architecture_requires_a_dotnet_project()
    {
        // A JS repo with an examples/mvc/controllers folder must NOT be mislabelled.
        var r = Analyze(x =>
            x.AllPaths = new() { "package.json", "examples/mvc/controllers/home.js", "examples/mvc/views/index.ejs" });

        Assert.Null(r.ArchitecturePattern);
    }

    [Fact]
    public void Detects_openapi_from_packages()
    {
        var r = Analyze(x =>
        {
            x.AllPaths = new() { "Api.csproj", "Controllers/ValuesController.cs" };
            x.Packages = new() { new() { Name = "Swashbuckle.AspNetCore", Source = "Api.csproj" } };
        });

        Assert.True(r.HasOpenApi);
    }

    [Fact]
    public void Maturity_is_grade_a_when_all_signals_present()
    {
        var r = Analyze(x =>
        {
            x.ReadmeContent = "# Title\nplenty of documentation here";
            x.Description = "A useful project";
            x.License = "MIT";
            x.ReleaseCount = 3;
            x.HasChangelog = true;
            x.AllPaths = new()
            {
                "App.csproj", "tests/AppTests.csproj", "docs/guide.md", "docs/api.md",
                ".github/workflows/ci.yml", ".github/dependabot.yml"
            };
            x.Workflows = new() { new() { Name = "ci.yml", Does = new() { "runs tests" } } };
        });

        Assert.NotNull(r.Maturity);
        Assert.Equal("A", r.Maturity!.Grade);
        Assert.True(r.Maturity.Score >= 85);
        Assert.Contains("Dependabot", r.SecurityTools);
    }

    [Fact]
    public void Bare_repo_gets_coaching_and_grade_f()
    {
        var r = Analyze(x => x.AllPaths = new() { "index.js" });

        Assert.Contains(r.Suggestions, s => s.Contains("README"));
        Assert.Contains(r.Suggestions, s => s.Contains("tests"));
        Assert.Contains(r.Suggestions, s => s.Contains("LICENSE"));
        Assert.NotNull(r.Maturity);
        Assert.Equal("F", r.Maturity!.Grade);
    }

    [Fact]
    public void Detects_conventional_commits()
    {
        var r = Analyze(x => x.RecentCommits = new()
        {
            new() { Message = "feat: add login" },
            new() { Message = "fix: null reference" },
            new() { Message = "chore: bump deps" },
        });

        Assert.Contains(r.CommitInsights, s => s.Contains("Conventional Commits"));
    }
}
