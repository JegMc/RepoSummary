using Microsoft.AspNetCore.Mvc.RazorPages;
using RepoSummary.Data;
using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Pages;

/// <summary>
/// A printable, per-repo interview prep sheet: key facts, rule-based talking points,
/// and any STAR stories saved for this repo. Assembles saved data — no GitHub or AI
/// call is made when opening it.
/// </summary>
public class PrepPackModel : PageModel
{
    private readonly IAnalysisStore _store;

    public PrepPackModel(IAnalysisStore store) => _store = store;

    public GitHubRepoAnalysisResult? Result { get; private set; }
    public IReadOnlyList<SavedStory> Stories { get; private set; } = Array.Empty<SavedStory>();
    public DateTime? SavedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(string? owner, string? repo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            ErrorMessage = "No repository was specified.";
            return;
        }

        var snapshot = await _store.GetSavedAsync(owner, repo, ct);
        if (snapshot is null)
        {
            ErrorMessage = "No saved analysis found for this repository — analyze it first, then open its prep pack.";
            return;
        }

        Result = snapshot.Result;
        SavedAt = snapshot.AnalyzedAt;

        var full = $"{owner}/{repo}";
        var all = await _store.GetStoriesAsync(ct);
        Stories = all.Where(s => string.Equals(s.RepoFullName, full, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
