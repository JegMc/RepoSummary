namespace RepoSummary.Models;

public class GitHubLanguageSummary
{
    public string Name { get; set; } = "";

    /// <summary>Bytes of code GitHub attributes to this language.</summary>
    public long Bytes { get; set; }

    /// <summary>Share of the repo's detected code, 0-100.</summary>
    public double Percentage { get; set; }
}
