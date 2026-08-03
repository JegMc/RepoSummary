using RepoSummary.Models;

namespace RepoSummary.Services;

/// <summary>
/// Extra rule-based evidence derived from the file tree and parsed dependencies:
/// how many tests there are (and the framework), and dependency-hygiene notes.
/// Pure and unit-testable.
/// </summary>
public static class EvidenceExtras
{
    private static readonly HashSet<string> CodeExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".js", ".jsx", ".ts", ".tsx", ".go", ".rb", ".php", ".java", ".kt",
        ".rs", ".c", ".cc", ".cpp", ".swift", ".scala", ".dart", ".ex", ".exs", ".m"
    };

    // package-name fragment → test framework display name.
    private static readonly (string Needle, string Name)[] FrameworkByPackage =
    {
        ("xunit", "xUnit"), ("nunit", "NUnit"), ("mstest", "MSTest"),
        ("vitest", "Vitest"), ("jest", "Jest"), ("mocha", "Mocha"), ("jasmine", "Jasmine"),
        ("@testing-library", "Testing Library"), ("cypress", "Cypress"), ("playwright", "Playwright"),
        ("pytest", "pytest"), ("rspec", "RSpec"), ("phpunit", "PHPUnit"),
        ("testng", "TestNG"), ("junit", "JUnit"),
    };

    /// <summary>Counts test files and names the framework (from packages, then file patterns).</summary>
    public static (int Count, string? Framework) DetectTests(
        IReadOnlyList<string> paths, IReadOnlyList<GitHubPackageSummary> packages)
    {
        var count = paths.Count(IsTestFile);

        string? framework = null;
        foreach (var (needle, name) in FrameworkByPackage)
            if (packages.Any(p => p.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                framework = name;
                break;
            }

        // Fall back to signals in the file names when no package pinned it.
        if (framework is null && count > 0)
        {
            if (paths.Any(p => p.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase))) framework = "Go testing";
            else if (paths.Any(p => p.Contains(".spec.", StringComparison.OrdinalIgnoreCase))) framework = "spec-based";
        }

        return (count, framework);
    }

    private static bool IsTestFile(string path)
    {
        var name = path.Split('/').Last();
        var ext = ExtensionOf(name);
        if (!CodeExt.Contains(ext)) return false;

        var lowerPath = path.ToLowerInvariant();
        var lowerName = name.ToLowerInvariant();

        var inTestDir = lowerPath.Split('/').Any(seg =>
            seg is "test" or "tests" or "spec" or "specs" or "__tests__" or "e2e");
        var testName =
            lowerName.Contains(".test.") || lowerName.Contains(".spec.") ||
            lowerName.Contains("_test.") || lowerName.EndsWith("test" + ext) ||
            lowerName.EndsWith("tests" + ext) || lowerName.StartsWith("test_");

        return inTestDir || testName;
    }

    /// <summary>Honest, registry-free dependency notes: version-pin rate and pre-1.0 count.</summary>
    public static List<string> DependencyNotes(IReadOnlyList<GitHubPackageSummary> packages)
    {
        var notes = new List<string>();
        if (packages.Count < 3) return notes;

        var pinned = packages.Count(p => !string.IsNullOrWhiteSpace(p.Version));
        if (pinned > 0)
            notes.Add($"{pinned} of {packages.Count} dependencies pin an explicit version.");

        var preRelease = packages.Count(p =>
            !string.IsNullOrWhiteSpace(p.Version) && p.Version!.TrimStart('^', '~', '=', 'v', ' ').StartsWith("0."));
        if (preRelease > 0)
            notes.Add($"{preRelease} dependenc{(preRelease == 1 ? "y is" : "ies are")} pre-1.0 (0.x — APIs may still be unstable).");

        return notes;
    }

    private static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..];
    }
}
