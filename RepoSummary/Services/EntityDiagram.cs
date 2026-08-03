using System.Text;
using System.Text.RegularExpressions;
using RepoSummary.Models;

namespace RepoSummary.Services;

/// <summary>
/// Builds a Mermaid entity-relationship diagram from model/entity classes found in the key
/// source files. C# gets typed fields + inferred relationships (navigation properties); other
/// languages contribute entity names. Pure and unit-testable. Null when there's too little.
/// </summary>
public static class EntityDiagram
{
    private static readonly Regex CSharpClass =
        new(@"(?:public|internal)?\s*(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex CSharpProp =
        new(@"public\s+(?:virtual\s+|required\s+)?([\w<>?\[\],\s\.]+?)\s+(\w+)\s*\{\s*get;", RegexOptions.Compiled);
    private static readonly Regex PyClass =
        new(@"^\s*class\s+(\w+)", RegexOptions.Compiled);

    private static readonly string[] ModelHints =
        { "model", "entity", "entities", "domain", "schema", "dto", "dtos", "record" };

    public readonly record struct Field(string Type, string Name);

    /// <summary>Returns Mermaid `erDiagram` source, or null when fewer than two entities are found.</summary>
    public static string? BuildMermaid(GitHubRepoAnalysisResult r)
    {
        var entities = new Dictionary<string, List<Field>>(StringComparer.Ordinal);

        foreach (var kf in r.KeyFiles)
        {
            var ext = ExtensionOf(kf.Path);
            var modelish = ModelHints.Any(h => kf.Path.Contains(h, StringComparison.OrdinalIgnoreCase));
            if (ext == ".cs") ExtractCSharp(kf.Snippet, modelish, entities);
            else if (ext == ".py") ExtractPython(kf.Snippet, modelish, entities);
        }

        // Keep the strongest: prefer entities that actually have fields; cap for legibility.
        var chosen = entities
            .OrderByDescending(e => e.Value.Count)
            .Take(10)
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

        if (chosen.Count < 2) return null;

        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");
        foreach (var (name, fields) in chosen)
        {
            if (fields.Count == 0) { sb.AppendLine($"    {name} {{ }}"); continue; }
            sb.AppendLine($"    {name} {{");
            foreach (var f in fields.Take(8))
                sb.AppendLine($"        {SafeType(f.Type)} {f.Name}");
            sb.AppendLine("    }");
        }

        // Relationships: a field whose (de-collectioned) type is another entity.
        var rels = new HashSet<string>();
        foreach (var (name, fields) in chosen)
            foreach (var f in fields)
            {
                var target = BaseType(f.Type);
                if (target != name && chosen.ContainsKey(target))
                {
                    var many = f.Type.Contains("List<") || f.Type.Contains("[]") ||
                               f.Type.Contains("Collection<") || f.Type.Contains("IEnumerable<");
                    rels.Add($"    {name} {(many ? "||--o{" : "||--||")} {target} : {f.Name}");
                }
            }
        foreach (var rel in rels) sb.AppendLine(rel);

        return sb.ToString();
    }

    private static void ExtractCSharp(string content, bool modelish, Dictionary<string, List<Field>> into)
    {
        // Walk lines, tracking the current class and collecting its get-set properties.
        var lines = content.Replace("\r\n", "\n").Split('\n');
        string? current = null;
        var fields = new List<Field>();

        void Flush()
        {
            if (current is null) return;
            // Include a class if it has fields, or lives in a model-ish file (entity with no props read yet).
            if (fields.Count > 0 || modelish)
                Merge(into, current, fields);
            current = null;
            fields = new List<Field>();
        }

        foreach (var line in lines)
        {
            var cm = CSharpClass.Match(line);
            if (cm.Success) { Flush(); current = cm.Groups[1].Value; continue; }
            if (current is null) continue;
            var pm = CSharpProp.Match(line);
            if (pm.Success)
                fields.Add(new Field(pm.Groups[1].Value.Trim(), pm.Groups[2].Value.Trim()));
        }
        Flush();
    }

    private static void ExtractPython(string content, bool modelish, Dictionary<string, List<Field>> into)
    {
        if (!modelish) return;   // without types, only trust model-ish files
        foreach (Match m in PyClass.Matches(content))
            Merge(into, m.Groups[1].Value, new List<Field>());
    }

    private static void Merge(Dictionary<string, List<Field>> into, string name, List<Field> fields)
    {
        if (name.Length == 0) return;
        if (into.TryGetValue(name, out var existing))
        {
            foreach (var f in fields) if (!existing.Any(x => x.Name == f.Name)) existing.Add(f);
        }
        else into[name] = new List<Field>(fields);
    }

    // Strip nullability/collections to the inner type name for relationship matching.
    private static string BaseType(string type)
    {
        var t = type.Trim().TrimEnd('?');
        var lt = t.IndexOf('<');
        if (lt >= 0)
        {
            var gt = t.LastIndexOf('>');
            if (gt > lt) t = t[(lt + 1)..gt];   // inner of List<X>, ICollection<X>, …
        }
        t = t.Replace("[]", "").Trim().TrimEnd('?');
        var dot = t.LastIndexOf('.');
        if (dot >= 0) t = t[(dot + 1)..];
        return t;
    }

    // Mermaid attribute types must be a single alphanumeric token.
    private static string SafeType(string type)
    {
        var t = BaseType(type);
        t = Regex.Replace(t, @"[^A-Za-z0-9_]", "");
        return t.Length == 0 ? "field" : t;
    }

    private static string ExtensionOf(string path)
    {
        var name = path.Split('/').Last();
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..].ToLowerInvariant();
    }
}
