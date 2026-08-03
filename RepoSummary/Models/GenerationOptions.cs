namespace RepoSummary.Models;

/// <summary>What the user asked the AI to generate, and how.</summary>
public class GenerationOptions
{
    /// <summary>Artifact types to produce (see <see cref="AllTypes"/>).</summary>
    public List<string> Types { get; set; } = new() { "ResumeBullet", "InterviewStory", "ProjectSummary" };

    /// <summary>"balanced" | "confident" | "honest".</summary>
    public string Tone { get; set; } = "balanced";

    /// <summary>Optional job description to tailor the output toward.</summary>
    public string? JobDescription { get; set; }

    public bool HasJob => !string.IsNullOrWhiteSpace(JobDescription);

    /// <summary>Phase 8: optimize résumé output for applicant-tracking systems (keyword-dense,
    /// plain formatting, mirroring the job description's terms when one is provided).</summary>
    public bool AtsOptimize { get; set; }

    /// <summary>The supported artifact types, in display order.</summary>
    public static readonly string[] AllTypes =
    {
        "ResumeBullet", "InterviewStory", "ProjectSummary",
        "CoverLetter", "LinkedInAbout", "TechnicalCaseStudy", "ReadmeImprovements", "FullReadme",
        "LikelyQuestions", "JobFitGaps", "HireabilityTips", "RoleFitScore", "PortfolioNarrative"
    };

    /// <summary>The types the user can pick directly (gap/hireability are added automatically with a job description).</summary>
    public static readonly string[] SelectableTypes =
    {
        "ResumeBullet", "InterviewStory", "ProjectSummary",
        "CoverLetter", "LinkedInAbout", "TechnicalCaseStudy", "ReadmeImprovements", "FullReadme"
    };
}
