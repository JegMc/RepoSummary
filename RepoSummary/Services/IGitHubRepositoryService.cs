using RepoSummary.Models;

namespace RepoSummary.Services;

public interface IGitHubRepositoryService
{
    /// <summary>
    /// Fetches and analyzes a public repository. Never throws for expected
    /// failures (not found, rate limited, network) — returns a failed
    /// <see cref="ServiceResult{T}"/> with a readable message instead.
    /// </summary>
    Task<ServiceResult<GitHubRepoAnalysisResult>> AnalyzeAsync(
        RepoReference reference,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a user's public, non-fork repositories, ranked by stars — for the
    /// profile-level view. Never throws for expected failures.</summary>
    Task<ServiceResult<List<UserRepoSummary>>> GetUserReposAsync(
        string user,
        CancellationToken cancellationToken = default);
}
