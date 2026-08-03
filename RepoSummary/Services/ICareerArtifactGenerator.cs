using RepoSummary.Models;

namespace RepoSummary.Services;

public interface ICareerArtifactGenerator
{
    /// <summary>True when an OpenAI or Anthropic API key is configured. When false,
    /// the UI can explain how to enable generation instead of showing a broken button.</summary>
    bool IsConfigured { get; }

    /// <summary>Which provider a generation would use now: "openai", "anthropic", or null.</summary>
    string? ActiveProvider { get; }

    /// <summary>Human-readable name of the active provider (for the UI).</summary>
    string ActiveProviderName { get; }

    /// <summary>
    /// Generates evidence-grounded career artifacts (resume bullets, a STAR
    /// interview story, a project summary) from an already-extracted analysis.
    /// Never throws for expected failures — returns a failed result with a
    /// readable message instead.
    /// </summary>
    Task<ServiceResult<List<GeneratedCareerArtifact>>> GenerateAsync(
        GitHubRepoAnalysisResult analysis,
        GenerationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Synthesizes one narrative + cross-cutting bullets across several repos.</summary>
    Task<ServiceResult<List<GeneratedCareerArtifact>>> GeneratePortfolioAsync(
        IReadOnlyList<GitHubRepoAnalysisResult> analyses,
        GenerationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Streams the selected artifacts as plain markdown, token by token (OpenAI).</summary>
    IAsyncEnumerable<string> StreamAsync(
        GitHubRepoAnalysisResult analysis,
        GenerationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Answers a free-form question about the repository, grounded in its evidence and
    /// key source files. Streamed as plain Markdown (OpenAI); Anthropic returns it in one chunk.</summary>
    IAsyncEnumerable<string> AnswerAsync(
        GitHubRepoAnalysisResult analysis,
        string question,
        CancellationToken cancellationToken = default);
}
