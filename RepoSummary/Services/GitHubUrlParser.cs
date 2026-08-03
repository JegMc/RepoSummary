using System.Text.RegularExpressions;

namespace RepoSummary.Services;

/// <summary>The owner/repo pair parsed out of user input.</summary>
public record RepoReference(string Owner, string Repo);

/// <summary>
/// Parses the accepted GitHub repo input forms into an owner/repo pair.
/// Pure logic, no I/O — kept separate so it stays easy to test.
///
/// Accepted:
///   https://github.com/owner/repo   (with or without .git, trailing slash, extra path)
///   github.com/owner/repo
///   owner/repo
/// </summary>
public class GitHubUrlParser
{
    // owner and repo: GitHub allows letters, digits, hyphen, underscore, dot.
    private const string Segment = @"[A-Za-z0-9_.-]+";

    private static readonly Regex Pattern = new(
        $@"^(?:https?://)?(?:www\.)?(?:github\.com/)?(?<owner>{Segment})/(?<repo>{Segment})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool TryParse(string? input, out RepoReference? reference, out string? error)
    {
        reference = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Please enter a GitHub repository URL or owner/repo.";
            return false;
        }

        var trimmed = input.Trim();
        var match = Pattern.Match(trimmed);
        if (!match.Success)
        {
            error = "That doesn't look like a GitHub repository. Try owner/repo, " +
                    "github.com/owner/repo, or a full https://github.com/owner/repo URL.";
            return false;
        }

        var owner = match.Groups["owner"].Value;
        var repo = match.Groups["repo"].Value;

        // Strip a trailing .git if present (e.g. from a clone URL).
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];

        if (repo.Length == 0)
        {
            error = "The repository name is missing. Expected owner/repo.";
            return false;
        }

        reference = new RepoReference(owner, repo);
        return true;
    }
}
