namespace RepoSummary.Services;

/// <summary>
/// Picks the highest-signal source files to actually read from a repo's file tree —
/// entrypoints first, then the largest/most-central code files — while skipping
/// vendored, generated, minified, and oversized files. Pure and unit-testable.
/// </summary>
public static class SourceFileSelector
{
    /// <summary>A file in the tree we could choose to read.</summary>
    public readonly record struct Candidate(string Path, long Bytes);

    /// <summary>A chosen file plus the reason it was picked (for display + the model).</summary>
    public readonly record struct Pick(string Path, long Bytes, string Reason);

    // Source extensions worth reading. Deliberately excludes data/markup/lock files.
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".js", ".jsx", ".ts", ".tsx", ".go", ".rb", ".php", ".java", ".kt",
        ".rs", ".c", ".cc", ".cpp", ".h", ".hpp", ".swift", ".scala", ".dart", ".vue", ".svelte",
        ".ex", ".exs", ".sh", ".m", ".mm"
    };

    // Filename stems that strongly indicate an application entrypoint.
    private static readonly string[] EntrypointNames =
    {
        "program.cs", "startup.cs", "main.py", "app.py", "__main__.py", "manage.py", "wsgi.py", "asgi.py",
        "main.go", "index.js", "index.ts", "server.js", "server.ts", "app.js", "app.ts", "main.js", "main.ts",
        "main.rs", "app.rb", "application.rb", "main.java", "application.java", "main.c", "main.cpp",
        "index.jsx", "index.tsx", "app.jsx", "app.tsx", "main.kt"
    };

    // Path fragments that mark vendored / generated / non-authored code.
    private static readonly string[] ExcludedSegments =
    {
        "node_modules/", "vendor/", "third_party/", "third-party/", "dist/", "build/", "out/",
        "bin/", "obj/", ".git/", "target/", "coverage/", ".venv/", "venv/", "site-packages/",
        "migrations/", "__pycache__/", ".next/", ".nuxt/", "generated/", "gen/"
    };

    // Filename markers for minified/bundled/generated files.
    private static readonly string[] ExcludedNameMarkers =
    {
        ".min.", ".bundle.", ".generated.", ".g.cs", ".designer.cs", ".d.ts", ".lock."
    };

    private const long MaxFileBytes = 80_000;   // skip anything huge — it's rarely the "core" file
    private const long MinFileBytes = 40;        // skip near-empty stubs

    /// <summary>
    /// Returns up to <paramref name="max"/> files worth reading, entrypoints first, then the
    /// largest remaining code files. Test files are allowed but deprioritised.
    /// </summary>
    public static List<Pick> Select(IEnumerable<Candidate> candidates, int max = 6)
    {
        var eligible = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Path))
            .Where(c => c.Bytes is >= MinFileBytes and <= MaxFileBytes)
            .Where(c => CodeExtensions.Contains(Extension(c.Path)))
            .Where(c => !IsExcluded(c.Path))
            .ToList();

        var scored = eligible
            .Select(c => new
            {
                c.Path,
                c.Bytes,
                IsEntry = IsEntrypoint(c.Path),
                IsTest = IsTest(c.Path),
                Depth = c.Path.Count(ch => ch == '/')
            })
            // Entrypoints first; non-test before test; shallower before deeper; then larger files.
            .OrderByDescending(x => x.IsEntry)
            .ThenBy(x => x.IsTest)
            .ThenBy(x => x.Depth)
            .ThenByDescending(x => x.Bytes)
            .Take(max)
            .Select(x => new Pick(
                x.Path,
                x.Bytes,
                x.IsEntry ? "entrypoint" : x.IsTest ? "test" : "core module"))
            .ToList();

        return scored;
    }

    private static string Extension(string path)
    {
        var name = path.Split('/').Last();
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..];
    }

    private static bool IsEntrypoint(string path) =>
        EntrypointNames.Contains(path.Split('/').Last(), StringComparer.OrdinalIgnoreCase);

    private static bool IsTest(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.Contains("/test") || lower.Contains("test/") || lower.StartsWith("test")
               || lower.Contains(".test.") || lower.Contains(".spec.") || lower.Contains("_test.")
               || lower.Contains("spec/");
    }

    private static bool IsExcluded(string path)
    {
        var lower = ("/" + path.ToLowerInvariant());
        if (ExcludedSegments.Any(seg => lower.Contains("/" + seg))) return true;
        var name = path.Split('/').Last().ToLowerInvariant();
        return ExcludedNameMarkers.Any(m => name.Contains(m));
    }
}
