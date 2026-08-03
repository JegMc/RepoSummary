using System.Text.RegularExpressions;

namespace RepoSummary.Services;

/// <summary>
/// Extracts import / using / require targets from a source file, and classifies whether
/// an import points inside the repo (relative/local) or at an external package. Pure and
/// unit-testable — feeds the architecture diagram and the code-grounded evidence context.
/// </summary>
public static class SourceImportParser
{
    // language → the regexes that capture an import target in capture group 1.
    private static readonly Regex[] JsTs =
    {
        new(@"import\s+(?:.+?\s+from\s+)?['""]([^'""]+)['""]", RegexOptions.Compiled),
        new(@"require\(\s*['""]([^'""]+)['""]\s*\)", RegexOptions.Compiled),
        new(@"export\s+(?:.+?\s+)?from\s+['""]([^'""]+)['""]", RegexOptions.Compiled),
    };
    private static readonly Regex Python =
        new(@"^\s*(?:from\s+([A-Za-z0-9_.]+)\s+import|import\s+([A-Za-z0-9_.]+))", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex CSharp =
        new(@"^\s*using\s+(?:static\s+)?([A-Za-z0-9_.]+)\s*;", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Go =
        new(@"^\s*(?:_\s+)?""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Java =
        new(@"^\s*import\s+(?:static\s+)?([A-Za-z0-9_.]+)\s*;", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Ruby =
        new(@"^\s*require(?:_relative)?\s+['""]([^'""]+)['""]", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Rust =
        new(@"^\s*use\s+([A-Za-z0-9_:]+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Php =
        new(@"^\s*use\s+([A-Za-z0-9_\\]+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex CFamily =
        new(@"^\s*#\s*include\s+[<""]([^>""]+)[>""]", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Swift =
        new(@"^\s*import\s+([A-Za-z0-9_.]+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Elixir =
        new(@"^\s*(?:alias|import|require|use)\s+([A-Z][A-Za-z0-9_.]+)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Returns the distinct import targets referenced by a file (raw, as written).</summary>
    public static List<string> ExtractImports(string path, string content)
    {
        if (string.IsNullOrEmpty(content)) return new();
        var ext = Extension(path);
        var found = new List<string>();

        switch (ext)
        {
            case ".js" or ".jsx" or ".ts" or ".tsx" or ".vue" or ".svelte" or ".mjs":
                foreach (var rx in JsTs) Collect(rx, content, found);
                break;
            case ".py":
                foreach (Match m in Python.Matches(content))
                    Add(found, m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                break;
            case ".cs":
                foreach (Match m in CSharp.Matches(content)) Add(found, m.Groups[1].Value);
                break;
            case ".go":
                foreach (Match m in Go.Matches(content)) Add(found, m.Groups[1].Value);
                break;
            case ".java" or ".kt":
                foreach (Match m in Java.Matches(content)) Add(found, m.Groups[1].Value);
                break;
            case ".rb":
                foreach (Match m in Ruby.Matches(content)) Add(found, m.Groups[1].Value);
                break;
            case ".rs":
                foreach (Match m in Rust.Matches(content)) Add(found, m.Groups[1].Value);
                break;
            case ".php":
                foreach (Match m in Php.Matches(content)) Add(found, m.Groups[1].Value);
                break;
            case ".c" or ".cc" or ".cpp" or ".h" or ".hpp" or ".hh" or ".cxx":
                foreach (Match m in CFamily.Matches(content)) Add(found, m.Groups[1].Value);
                break;
            case ".swift":
                foreach (Match m in Swift.Matches(content)) Add(found, m.Groups[1].Value);
                break;
            case ".scala":
                foreach (Match m in Swift.Matches(content)) Add(found, m.Groups[1].Value);   // import foo.bar (no semicolon)
                break;
            case ".ex" or ".exs":
                foreach (Match m in Elixir.Matches(content)) Add(found, m.Groups[1].Value);
                break;
        }

        return found.Distinct(StringComparer.Ordinal).Take(60).ToList();
    }

    /// <summary>A relative/local import (starts with '.' or '/') points inside the repo.</summary>
    public static bool IsInternal(string import) =>
        import.StartsWith('.') || import.StartsWith('/');

    private static void Collect(Regex rx, string content, List<string> into)
    {
        foreach (Match m in rx.Matches(content)) Add(into, m.Groups[1].Value);
    }

    private static void Add(List<string> into, string value)
    {
        value = value.Trim();
        if (value.Length is > 0 and < 200) into.Add(value);
    }

    private static string Extension(string path)
    {
        var name = path.Split('/').Last();
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..].ToLowerInvariant();
    }
}
