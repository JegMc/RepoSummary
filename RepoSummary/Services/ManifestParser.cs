using System.Text.Json;
using System.Text.RegularExpressions;

namespace RepoSummary.Services;

/// <summary>
/// Pure, I/O-free parsing of manifest files and CI workflow YAML. Kept separate
/// from the HTTP service so the logic is easy to unit-test.
/// </summary>
public static class ManifestParser
{
    /// <summary>Normalizes a version string ("^1.2.3", "v1.2", "\"1.0\"") to "1.2.3".</summary>
    public static string? CleanVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim().Trim('"', '\'').TrimStart('^', '~', '>', '=', '<', ' ', 'v');
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    /// <summary>Extracts (dependency name, optional version) from a manifest's contents.</summary>
    public static IEnumerable<(string Name, string? Version)> ParseDependencies(string fileName, string content)
    {
        if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match m in Regex.Matches(content, "<PackageReference\\b[^>]*>"))
            {
                var inc = Regex.Match(m.Value, "Include=\"([^\"]+)\"");
                if (!inc.Success) continue;
                var ver = Regex.Match(m.Value, "Version=\"([^\"]+)\"");
                yield return (inc.Groups[1].Value, ver.Success ? ver.Groups[1].Value : null);
            }
            yield break;
        }

        switch (fileName.ToLowerInvariant())
        {
            case "package.json":
            case "composer.json":
                foreach (var pair in ParseJsonManifest(content)) yield return pair;
                break;

            case "requirements.txt":
                foreach (var line in content.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                    var name = Regex.Split(trimmed, "[=<>!~; \\[]")[0].Trim();
                    if (name.Length == 0) continue;
                    var ver = Regex.Match(trimmed, "==\\s*([\\w.]+)");
                    yield return (name, ver.Success ? ver.Groups[1].Value : null);
                }
                break;

            case "go.mod":
                foreach (Match m in Regex.Matches(content, "^\\s+([\\w./-]+)\\s+(v[\\w.\\-+]+)", RegexOptions.Multiline))
                    yield return (m.Groups[1].Value, m.Groups[2].Value);
                break;

            case "cargo.toml":
                foreach (Match m in Regex.Matches(content, "^\\s*([A-Za-z0-9_.-]+)\\s*=\\s*(?:\"([^\"]+)\"|\\{[^}]*version\\s*=\\s*\"([^\"]+)\")", RegexOptions.Multiline))
                    yield return (m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : (m.Groups[3].Success ? m.Groups[3].Value : null));
                break;

            case "pyproject.toml":
                foreach (Match m in Regex.Matches(content, "^\\s*([A-Za-z0-9_.-]+)\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline))
                    yield return (m.Groups[1].Value, m.Groups[2].Value);
                foreach (Match m in Regex.Matches(content, "\"([A-Za-z0-9_.\\-]+)\\s*[><=~!]=?\\s*([\\d.]+)\""))
                    yield return (m.Groups[1].Value, m.Groups[2].Value);
                break;

            case "pom.xml":
                foreach (Match m in Regex.Matches(content, "<artifactId>([^<]+)</artifactId>"))
                    yield return (m.Groups[1].Value, null);
                break;

            case "gemfile":
                foreach (Match m in Regex.Matches(content, "gem\\s+['\"]([^'\"]+)['\"](?:\\s*,\\s*['\"]([^'\"]+)['\"])?"))
                    yield return (m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null);
                break;

            case "build.gradle":
            case "build.gradle.kts":
                foreach (Match m in Regex.Matches(content, "(?:implementation|api|compile|testImplementation|classpath)[\\s(]+['\"]([^'\":]+):([^'\":]+):([^'\"]+)['\"]"))
                    yield return (m.Groups[2].Value, m.Groups[3].Value);
                break;

            case "pubspec.yaml":
                foreach (Match m in Regex.Matches(content, "^\\s{2,}([a-z0-9_]+):\\s*[\\^~>=]*\\s*([\\d][\\w.+-]*)?", RegexOptions.Multiline))
                {
                    var n = m.Groups[1].Value;
                    if (n is "flutter" or "sdk" or "dependencies" or "dev_dependencies") continue;
                    yield return (n, m.Groups[2].Success ? m.Groups[2].Value : null);
                }
                break;

            case "podfile":
                foreach (Match m in Regex.Matches(content, "pod\\s+['\"]([^'\"]+)['\"](?:\\s*,\\s*['\"~>=\\s]*([\\d][\\w.]*))?"))
                    yield return (m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null);
                break;
        }
    }

    /// <summary>Reads dependency name→version pairs from package.json / composer.json.</summary>
    private static IEnumerable<(string Name, string? Version)> ParseJsonManifest(string content)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(content); }
        catch { yield break; }

        using (doc)
        {
            foreach (var section in new[] { "dependencies", "devDependencies", "require", "require-dev" })
            {
                if (doc.RootElement.TryGetProperty(section, out var deps) &&
                    deps.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in deps.EnumerateObject())
                        yield return (prop.Name, prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null);
                }
            }
        }
    }

    /// <summary>Detects, at a high level, what a CI workflow YAML does.</summary>
    public static List<string> DetectWorkflowActions(string yaml)
    {
        var t = yaml.ToLowerInvariant();
        var does = new List<string>();
        void Add(string label, bool cond) { if (cond && !does.Contains(label)) does.Add(label); }

        Add("runs tests", Regex.IsMatch(t, "\\btest\\b"));
        Add("builds", Regex.IsMatch(t, "\\bbuild\\b"));
        Add("Docker", t.Contains("docker") || t.Contains("buildx"));
        Add("lints", t.Contains("lint") || t.Contains("eslint") || t.Contains("prettier") || t.Contains("dotnet format") || t.Contains("flake8"));
        Add("code scanning", t.Contains("codeql") || t.Contains("github/codeql-action"));
        Add("coverage", t.Contains("coverage") || t.Contains("codecov") || t.Contains("coveralls"));
        Add("publishes releases", t.Contains("action-gh-release") || t.Contains("semantic-release") || Regex.IsMatch(t, "\\brelease\\b"));
        Add("deploys", t.Contains("deploy") || t.Contains("gh-pages") || t.Contains("azure/webapps") || t.Contains("actions/deploy-pages"));
        return does;
    }
}
