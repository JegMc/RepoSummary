using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Tests;

public class SourceFileSelectorTests
{
    private static SourceFileSelector.Candidate C(string path, long bytes) => new(path, bytes);

    [Fact]
    public void Entrypoints_are_picked_first_even_when_smaller()
    {
        var picks = SourceFileSelector.Select(new[]
        {
            C("src/BigUtil.cs", 9000),
            C("src/Program.cs", 700),
        });

        Assert.Equal("src/Program.cs", picks[0].Path);
        Assert.Equal("entrypoint", picks[0].Reason);
    }

    [Fact]
    public void Vendored_generated_and_minified_files_are_excluded()
    {
        var picks = SourceFileSelector.Select(new[]
        {
            C("node_modules/react/index.js", 5000),
            C("dist/app.bundle.min.js", 8000),
            C("obj/Debug/App.g.cs", 4000),
            C("src/app.py", 2000),
        });

        Assert.Single(picks);
        Assert.Equal("src/app.py", picks[0].Path);
    }

    [Fact]
    public void Non_code_files_are_ignored()
    {
        var picks = SourceFileSelector.Select(new[]
        {
            C("package.json", 3000),
            C("README.md", 3000),
            C("logo.png", 3000),
            C("cmd/main.go", 2000),
        });

        Assert.Single(picks);
        Assert.Equal("cmd/main.go", picks[0].Path);
    }

    [Fact]
    public void Oversized_and_empty_files_are_skipped()
    {
        var picks = SourceFileSelector.Select(new[]
        {
            C("huge.cs", 90_000),   // > 80KB cap
            C("stub.cs", 10),       // < min bytes
            C("core/Service.cs", 3000),
        });

        Assert.Single(picks);
        Assert.Equal("core/Service.cs", picks[0].Path);
    }

    [Fact]
    public void Respects_the_max_cap_and_prefers_larger_core_files()
    {
        var picks = SourceFileSelector.Select(new[]
        {
            C("a/One.cs", 1000),
            C("a/Two.cs", 5000),
            C("a/Three.cs", 3000),
        }, max: 2);

        Assert.Equal(2, picks.Count);
        Assert.Equal("a/Two.cs", picks[0].Path);   // largest core file first
    }

    [Fact]
    public void Tests_are_deprioritised_below_regular_code()
    {
        var picks = SourceFileSelector.Select(new[]
        {
            C("tests/BigTests.cs", 9000),
            C("src/Small.cs", 500),
        });

        Assert.Equal("src/Small.cs", picks[0].Path);
        Assert.Equal("test", picks[1].Reason);
    }
}

public class SourceImportParserTests
{
    [Fact]
    public void Extracts_csharp_usings()
    {
        var imports = SourceImportParser.ExtractImports("A.cs", "using System.Text;\nusing App.Services;\nnamespace X;");
        Assert.Contains("System.Text", imports);
        Assert.Contains("App.Services", imports);
    }

    [Fact]
    public void Extracts_python_imports()
    {
        var imports = SourceImportParser.ExtractImports("m.py", "from app.models import User\nimport os");
        Assert.Contains("app.models", imports);
        Assert.Contains("os", imports);
    }

    [Fact]
    public void Extracts_js_import_and_require()
    {
        var imports = SourceImportParser.ExtractImports("i.ts", "import { a } from './svc';\nconst y = require('lodash');");
        Assert.Contains("./svc", imports);
        Assert.Contains("lodash", imports);
    }

    [Fact]
    public void Extracts_go_block_imports()
    {
        var src = "package main\n\nimport (\n\t\"fmt\"\n\t\"github.com/gin-gonic/gin\"\n)\n";
        var imports = SourceImportParser.ExtractImports("main.go", src);
        Assert.Contains("fmt", imports);
        Assert.Contains("github.com/gin-gonic/gin", imports);
    }

    [Fact]
    public void Classifies_relative_imports_as_internal()
    {
        Assert.True(SourceImportParser.IsInternal("./services/auth"));
        Assert.True(SourceImportParser.IsInternal("../db"));
        Assert.False(SourceImportParser.IsInternal("react"));
        Assert.False(SourceImportParser.IsInternal("System.Text"));
    }
}

public class ArchitectureDiagramTests
{
    [Fact]
    public void Builds_a_component_graph_with_import_edge_and_db()
    {
        var r = new GitHubRepoAnalysisResult
        {
            DatabaseTech = "PostgreSQL",
            AllPaths = new()
            {
                "api/UsersController.cs", "services/UserService.cs", "data/UserRepository.cs", "README.md"
            },
            KeyFiles = new()
            {
                new KeyFile { Path = "api/UsersController.cs", Imports = new() { "app/services/UserService" } }
            }
        };

        var mm = ArchitectureDiagram.BuildMermaid(r);

        Assert.NotNull(mm);
        Assert.Contains("flowchart", mm);
        Assert.Contains("api/", mm);
        Assert.Contains("services/", mm);
        Assert.Contains("PostgreSQL", mm);   // database node
        Assert.Contains("-->", mm);          // at least one edge (import- or data-derived)
    }

    [Fact]
    public void Returns_null_when_there_is_nothing_to_diagram()
    {
        var r = new GitHubRepoAnalysisResult { AllPaths = new() { "README.md", "LICENSE" } };
        Assert.Null(ArchitectureDiagram.BuildMermaid(r));
    }

    [Fact]
    public void Falls_back_to_layers_from_detected_signals()
    {
        var r = new GitHubRepoAnalysisResult
        {
            AllPaths = new() { "app.py" },       // one flat code file → no component graph
            ControllerCount = 3,
            DatabaseTech = "SQLite"
        };

        var mm = ArchitectureDiagram.BuildMermaid(r);

        Assert.NotNull(mm);
        Assert.Contains("SQLite", mm);
        Assert.Contains("-->", mm);
    }
}
