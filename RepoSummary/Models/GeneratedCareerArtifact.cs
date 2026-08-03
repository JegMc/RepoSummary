namespace RepoSummary.Models;

/// <summary>
/// A piece of AI-generated career material grounded in repo evidence.
/// The product rule holds: every artifact carries the evidence that backs it.
/// </summary>
public class GeneratedCareerArtifact
{
    /// <summary>ResumeBullet, InterviewStory, or ProjectSummary.</summary>
    public string ArtifactType { get; set; } = "";

    /// <summary>Short heading for the card (e.g. the STAR story's title).</summary>
    public string Title { get; set; } = "";

    /// <summary>The generated text.</summary>
    public string Content { get; set; } = "";

    /// <summary>Evidence labels the model tied this claim to (from the extracted evidence).</summary>
    public List<string> Evidence { get; set; } = new();
}
