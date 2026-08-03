namespace RepoSummary.Models;

public class GitHubCommitSummary
{
    public string Sha { get; set; } = "";
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;
    public string Message { get; set; } = "";
    public string FirstLine => Message.Split('\n', 2)[0].Trim();
    public string? Author { get; set; }
    public DateTimeOffset? Date { get; set; }
    public string? HtmlUrl { get; set; }
}
