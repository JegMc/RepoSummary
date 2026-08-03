using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Tests;

public class ImportParserLanguageTests
{
    [Fact]
    public void Php_use_statements()
    {
        var imports = SourceImportParser.ExtractImports("Controller.php", "<?php\nuse App\\Models\\User;\nuse App\\Services\\Auth;");
        Assert.Contains("App\\Models\\User", imports);
        Assert.Contains("App\\Services\\Auth", imports);
    }

    [Fact]
    public void C_family_includes()
    {
        var imports = SourceImportParser.ExtractImports("main.c", "#include <stdio.h>\n#include \"app.h\"");
        Assert.Contains("stdio.h", imports);
        Assert.Contains("app.h", imports);
    }

    [Fact]
    public void Swift_and_elixir_imports()
    {
        var swift = SourceImportParser.ExtractImports("View.swift", "import Foundation\nimport UIKit");
        Assert.Contains("Foundation", swift);
        Assert.Contains("UIKit", swift);

        var elixir = SourceImportParser.ExtractImports("repo.ex", "defmodule X do\n  alias App.Repo\n  import Ecto.Query\nend");
        Assert.Contains("App.Repo", elixir);
        Assert.Contains("Ecto.Query", elixir);
    }
}

public class EvidenceExtrasTests
{
    [Fact]
    public void DetectTests_counts_files_and_names_framework()
    {
        var paths = new List<string> { "src/App.cs", "tests/AppTests.cs", "tests/UserTests.cs", "src/util.js", "src/util.test.js" };
        var pkgs = new List<GitHubPackageSummary> { new() { Name = "xunit" } };

        var (count, framework) = EvidenceExtras.DetectTests(paths, pkgs);

        Assert.Equal(3, count);
        Assert.Equal("xUnit", framework);
    }

    [Fact]
    public void DependencyNotes_reports_pin_rate_and_pre_release()
    {
        var pkgs = new List<GitHubPackageSummary>
        {
            new() { Name = "a", Version = "1.2.3" },
            new() { Name = "b", Version = "0.5.0" },
            new() { Name = "c", Version = "" },
        };

        var notes = EvidenceExtras.DependencyNotes(pkgs);

        Assert.Contains(notes, n => n.Contains("2 of 3"));
        Assert.Contains(notes, n => n.Contains("pre-1.0"));
    }

    [Fact]
    public void DependencyNotes_empty_for_tiny_manifests()
    {
        Assert.Empty(EvidenceExtras.DependencyNotes(new List<GitHubPackageSummary> { new() { Name = "a" } }));
    }
}

public class EntityDiagramTests
{
    [Fact]
    public void Builds_er_diagram_with_fields_and_relationships()
    {
        var r = new GitHubRepoAnalysisResult
        {
            KeyFiles = new()
            {
                new KeyFile
                {
                    Path = "Models/User.cs",
                    Snippet = "public class User\n{\n    public int Id { get; set; }\n    public string Name { get; set; }\n    public List<Order> Orders { get; set; }\n}"
                },
                new KeyFile
                {
                    Path = "Models/Order.cs",
                    Snippet = "public class Order\n{\n    public int Id { get; set; }\n    public User Owner { get; set; }\n}"
                }
            }
        };

        var mm = EntityDiagram.BuildMermaid(r);

        Assert.NotNull(mm);
        Assert.Contains("erDiagram", mm);
        Assert.Contains("User", mm);
        Assert.Contains("Order", mm);
        Assert.Contains("--", mm);   // at least one relationship
    }

    [Fact]
    public void Returns_null_with_fewer_than_two_entities()
    {
        var r = new GitHubRepoAnalysisResult
        {
            KeyFiles = new() { new KeyFile { Path = "Program.cs", Snippet = "public class Program { }" } }
        };
        Assert.Null(EntityDiagram.BuildMermaid(r));
    }
}
