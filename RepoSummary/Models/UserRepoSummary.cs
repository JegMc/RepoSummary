namespace RepoSummary.Models;

/// <summary>A public repository owned by a GitHub user, for the profile-level view.</summary>
public class UserRepoSummary
{
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName => $"{Owner}/{Name}";
    public string? Description { get; set; }
    public int Stars { get; set; }
    public string? Language { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
