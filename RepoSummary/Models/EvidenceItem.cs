namespace RepoSummary.Models;

/// <summary>
/// A single piece of repo evidence that can back a career claim.
/// The product rule: every strong claim ties back to one of these.
/// </summary>
public class EvidenceItem
{
    public string Type { get; set; } = "";      // e.g. README, Commit, FilePath, Technology, Directory
    public string Label { get; set; } = "";      // short human-readable label
    public string Detail { get; set; } = "";     // supporting detail
    public string? SourcePath { get; set; }      // path inside the repo, if applicable
    public string? SourceUrl { get; set; }       // link to the source on GitHub, if applicable
}
