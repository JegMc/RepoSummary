namespace RepoSummary.Models;

/// <summary>
/// Everything the app extracted about a public repository.
/// Designed so persistence can be layered on later without reshaping.
/// </summary>
public class GitHubRepoAnalysisResult
{
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName => string.IsNullOrEmpty(Owner) ? Name : $"{Owner}/{Name}";

    public string? Description { get; set; }
    public string? DefaultBranch { get; set; }
    public int Stars { get; set; }
    public int Forks { get; set; }
    public int OpenIssues { get; set; }
    public string? PrimaryLanguage { get; set; }
    public string? License { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? HtmlUrl { get; set; }

    // --- Phase 2: deeper analysis facts ---
    public string? ArchitecturePattern { get; set; }
    public int ControllerCount { get; set; }
    public bool HasOpenApi { get; set; }
    public string? DatabaseTech { get; set; }
    public List<WorkflowSummary> Workflows { get; set; } = new();
    public List<string> SecurityTools { get; set; } = new();
    public bool HasChangelog { get; set; }
    public int? ReleaseCount { get; set; }
    public string? LatestReleaseName { get; set; }
    public DateTimeOffset? LatestReleaseDate { get; set; }
    public List<string> CommitInsights { get; set; } = new();
    public RepoMaturity? Maturity { get; set; }

    public string? ReadmeContent { get; set; }
    public bool HasReadme => !string.IsNullOrWhiteSpace(ReadmeContent);

    public List<GitHubCommitSummary> RecentCommits { get; set; } = new();
    public List<GitHubLanguageSummary> Languages { get; set; } = new();
    public List<GitHubFileTreeItem> TopLevelItems { get; set; } = new();

    /// <summary>Every file/dir path in the repo (recursive tree, one API call).</summary>
    public List<string> AllPaths { get; set; } = new();

    /// <summary>True when GitHub truncated the recursive tree (very large repos).</summary>
    public bool TreeTruncated { get; set; }

    /// <summary>Dependencies parsed from manifest files (.csproj, package.json, …).</summary>
    public List<GitHubPackageSummary> Packages { get; set; } = new();

    /// <summary>Phase 8: a bounded set of the highest-signal source files, actually read
    /// (entrypoints + the largest/most-central files). This is what lets generation and the
    /// architecture diagram reason about the real code, not just metadata and file names.</summary>
    public List<KeyFile> KeyFiles { get; set; } = new();

    // Derived, rule-based output for the first slice.
    public List<EvidenceItem> Evidence { get; set; } = new();
    public List<CareerAngle> ResumeAngles { get; set; } = new();
    public List<CareerAngle> InterviewTalkingPoints { get; set; } = new();

    /// <summary>Plain-language "make this repo stronger" tips when signals are missing.</summary>
    public List<string> Suggestions { get; set; } = new();
}

/// <summary>A source file RepoSummary actually read, with a trimmed excerpt and its imports.</summary>
public class KeyFile
{
    public string Path { get; set; } = "";
    /// <summary>Link to the file on GitHub (blob URL), for deep-linking.</summary>
    public string? HtmlUrl { get; set; }
    /// <summary>Why it was picked, e.g. "entrypoint" or "core module".</summary>
    public string Reason { get; set; } = "";
    /// <summary>Total size in bytes (from the git tree).</summary>
    public long Bytes { get; set; }
    /// <summary>A trimmed excerpt of the file (first N lines), safe to feed to a model.</summary>
    public string Snippet { get; set; } = "";
    /// <summary>Import/using/require targets parsed from the file.</summary>
    public List<string> Imports { get; set; } = new();
    public string Name => Path.Split('/').Last();
}

/// <summary>A dependency detected in a manifest file, plus where it came from.</summary>
public class GitHubPackageSummary
{
    public string Name { get; set; } = "";
    /// <summary>Which manifest declared it, e.g. "RepoSummary.csproj" or "package.json".</summary>
    public string Source { get; set; } = "";
    public string? SourcePath { get; set; }
    public string? Version { get; set; }
}

/// <summary>A CI workflow file and the high-level things it appears to do.</summary>
public class WorkflowSummary
{
    public string Name { get; set; } = "";
    /// <summary>Detected actions, e.g. "tests", "builds", "Docker", "deploys".</summary>
    public List<string> Does { get; set; } = new();
}

/// <summary>One yes/no signal that feeds the maturity score.</summary>
public class MaturitySignal
{
    public string Label { get; set; } = "";
    public bool Present { get; set; }
}

/// <summary>A production-readiness snapshot rolled up from several signals.</summary>
public class RepoMaturity
{
    public int Score { get; set; }          // 0–100
    public string Grade { get; set; } = "";  // A–F
    public List<MaturitySignal> Signals { get; set; } = new();
}

/// <summary>A rule-based resume angle / talking point plus the evidence behind it.</summary>
public class CareerAngle
{
    public string Text { get; set; } = "";
    public List<EvidenceItem> SupportingEvidence { get; set; } = new();
}
