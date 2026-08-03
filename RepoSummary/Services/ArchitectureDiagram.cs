using System.Text;
using RepoSummary.Models;

namespace RepoSummary.Services;

/// <summary>
/// Builds a Mermaid flowchart of a repo's high-level shape from its top-level code
/// directories, the imports parsed out of the key files, and detected data/DB signals.
/// Pure and unit-testable. Returns null when there isn't enough structure to draw.
/// </summary>
public static class ArchitectureDiagram
{
    private static readonly HashSet<string> CodeExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".js", ".jsx", ".ts", ".tsx", ".go", ".rb", ".php", ".java", ".kt",
        ".rs", ".c", ".cc", ".cpp", ".h", ".hpp", ".swift", ".scala", ".dart", ".vue", ".svelte"
    };

    private static readonly HashSet<string> NoiseDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".github", ".git", "node_modules", "dist", "build", "bin", "obj", "vendor", "docs",
        "doc", ".vscode", ".idea", "assets", "images", "img", "public", "static", ".config"
    };

    // Directory-name fragments that suggest a data-access layer.
    private static readonly string[] DataDirHints =
        { "data", "db", "database", "model", "entities", "entity", "repositor", "dal", "store", "persistence", "schema" };

    /// <summary>Returns Mermaid `flowchart` source, or null if the repo is too small to diagram.</summary>
    public static string? BuildMermaid(GitHubRepoAnalysisResult r)
    {
        var codeDirs = r.AllPaths
            .Where(p => p.Contains('/'))
            .Where(p => CodeExt.Contains(Extension(p)))
            .Select(p => p.Split('/')[0])
            .Where(d => !NoiseDirs.Contains(d) && !d.StartsWith('.'))
            .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Dir = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .Select(x => x.Dir)
            .ToList();

        var hasDb = !string.IsNullOrWhiteSpace(r.DatabaseTech);

        // Not enough to draw a component graph → try a simple layered fallback.
        if (codeDirs.Count < 2)
            return LayeredFallback(r, codeDirs, hasDb);

        var id = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < codeDirs.Count; i++) id[codeDirs[i]] = "d" + i;

        var edges = new HashSet<(string From, string To)>();

        // Import-derived edges: when a key file in dir A imports something that names dir B.
        foreach (var kf in r.KeyFiles)
        {
            var srcDir = kf.Path.Split('/')[0];
            if (!id.ContainsKey(srcDir)) continue;
            foreach (var imp in kf.Imports)
                foreach (var d in codeDirs)
                    if (!d.Equals(srcDir, StringComparison.OrdinalIgnoreCase) &&
                        imp.Contains(d, StringComparison.OrdinalIgnoreCase))
                        edges.Add((srcDir, d));
        }

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        foreach (var d in codeDirs)
            sb.AppendLine($"    {id[d]}[\"{Escape(d)}/\"]");

        if (hasDb)
        {
            sb.AppendLine($"    db[(\"{Escape(r.DatabaseTech!)}\")]");
            // Link data-ish directories to the database.
            foreach (var d in codeDirs)
                if (DataDirHints.Any(h => d.Contains(h, StringComparison.OrdinalIgnoreCase)))
                    edges.Add((d, "__db"));
        }

        foreach (var (from, to) in edges)
        {
            var toId = to == "__db" ? "db" : id[to];
            sb.AppendLine($"    {id[from]} --> {toId}");
        }

        return sb.ToString();
    }

    /// <summary>A small linear diagram from detected signals when the tree is too flat to map.</summary>
    private static string? LayeredFallback(GitHubRepoAnalysisResult r, List<string> codeDirs, bool hasDb)
    {
        var layers = new List<string>();
        if (r.ControllerCount > 0 || r.HasOpenApi) layers.Add("API / Controllers");
        else if (!string.IsNullOrWhiteSpace(r.ArchitecturePattern)) layers.Add(r.ArchitecturePattern!);
        else if (codeDirs.Count == 1) layers.Add(codeDirs[0] + "/");
        else if (!string.IsNullOrWhiteSpace(r.PrimaryLanguage)) layers.Add(r.PrimaryLanguage! + " app");

        if (hasDb) layers.Add(r.DatabaseTech!);

        if (layers.Count < 2) return null;

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        for (var i = 0; i < layers.Count; i++) sb.AppendLine($"    n{i}[\"{Escape(layers[i])}\"]");
        for (var i = 0; i < layers.Count - 1; i++) sb.AppendLine($"    n{i} --> n{i + 1}");
        return sb.ToString();
    }

    private static string Extension(string path)
    {
        var name = path.Split('/').Last();
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..];
    }

    // Mermaid label text inside quotes: strip quotes/brackets that would break the node.
    private static string Escape(string s) =>
        s.Replace("\"", "").Replace("[", "(").Replace("]", ")").Replace("{", "(").Replace("}", ")");
}
