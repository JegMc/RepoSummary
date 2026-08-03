namespace RepoSummary.Services;

/// <summary>One rendered line of a branch-style tree: the connector prefix
/// (e.g. "│   ├── "), the entry name, and whether it's a directory.</summary>
public sealed record FileTreeLine(string Prefix, string Name, bool IsDirectory);

/// <summary>A node in the interactive file explorer (nested, for collapsible browsing).</summary>
public sealed class FileNode
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsDirectory { get; set; }
    /// <summary>Link to view this entry on GitHub (files open the blob; unused for dirs).</summary>
    public string? HtmlUrl { get; set; }
    public List<FileNode> Children { get; } = new();
}

/// <summary>
/// Builds a `tree`-command-style view from a flat list of recursive paths.
/// Directory-ness is inferred structurally: a node with children is a directory,
/// a leaf is a file (Git doesn't track empty directories, so this holds).
/// </summary>
public static class FileTreeRenderer
{
    private sealed class Node
    {
        public SortedDictionary<string, Node> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool HasChildren => Children.Count > 0;
    }

    public static List<FileTreeLine> Build(IEnumerable<string> paths, int maxLines = 400)
    {
        var root = new Node();
        foreach (var path in paths)
        {
            var node = root;
            foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!node.Children.TryGetValue(segment, out var child))
                {
                    child = new Node();
                    node.Children[segment] = child;
                }
                node = child;
            }
        }

        var lines = new List<FileTreeLine>();
        Walk(root, "", lines, maxLines);
        return lines;
    }

    private static void Walk(Node node, string prefix, List<FileTreeLine> lines, int maxLines)
    {
        // Directories first (like GitHub), then files; alphabetical within each group.
        var children = node.Children
            .OrderByDescending(kv => kv.Value.HasChildren)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < children.Count; i++)
        {
            if (lines.Count >= maxLines) return;

            var (name, child) = (children[i].Key, children[i].Value);
            var isLast = i == children.Count - 1;

            lines.Add(new FileTreeLine(prefix + (isLast ? "└── " : "├── "), name, child.HasChildren));
            Walk(child, prefix + (isLast ? "    " : "│   "), lines, maxLines);
        }
    }

    /// <summary>
    /// Builds a nested <see cref="FileNode"/> tree for the interactive explorer.
    /// Each file node gets a GitHub blob URL so files (not folders) can link out.
    /// Returns the root plus whether the node budget was hit (very large repos).
    /// </summary>
    public static (FileNode Root, bool Truncated) BuildTree(
        IEnumerable<string> paths, string? repoHtmlUrl, string? defaultBranch, int maxNodes = 2000)
    {
        var raw = new Node();
        foreach (var path in paths)
        {
            var node = raw;
            foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!node.Children.TryGetValue(segment, out var child))
                {
                    child = new Node();
                    node.Children[segment] = child;
                }
                node = child;
            }
        }

        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? "HEAD" : defaultBranch;
        var budget = maxNodes;
        var root = new FileNode { IsDirectory = true };
        Convert(raw, "", root, repoHtmlUrl, branch, ref budget);
        return (root, budget <= 0);
    }

    private static void Convert(Node raw, string basePath, FileNode target,
        string? repoUrl, string branch, ref int budget)
    {
        var children = raw.Children
            .OrderByDescending(kv => kv.Value.HasChildren)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, rawChild) in children)
        {
            if (budget <= 0) return;
            budget--;

            var path = basePath.Length == 0 ? name : $"{basePath}/{name}";
            var isDir = rawChild.HasChildren;

            var node = new FileNode
            {
                Name = name,
                Path = path,
                IsDirectory = isDir,
                HtmlUrl = BuildUrl(repoUrl, branch, path, isDir)
            };
            target.Children.Add(node);

            if (isDir) Convert(rawChild, path, node, repoUrl, branch, ref budget);
        }
    }

    private static string? BuildUrl(string? repoUrl, string branch, string path, bool isDir)
    {
        if (string.IsNullOrEmpty(repoUrl)) return null;
        var encoded = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        return $"{repoUrl}/{(isDir ? "tree" : "blob")}/{Uri.EscapeDataString(branch)}/{encoded}";
    }
}
