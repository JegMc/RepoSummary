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
}
