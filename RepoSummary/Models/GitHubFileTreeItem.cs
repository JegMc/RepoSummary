namespace RepoSummary.Models;

public class GitHubFileTreeItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    /// <summary>"file" or "dir".</summary>
    public string Type { get; set; } = "";
    public bool IsDirectory => string.Equals(Type, "dir", StringComparison.OrdinalIgnoreCase);
    public string? HtmlUrl { get; set; }
}
