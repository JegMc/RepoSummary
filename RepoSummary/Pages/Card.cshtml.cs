using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Pages;

/// <summary>
/// A clean, self-contained, shareable "health report card" for a repo — built from the
/// saved analysis (zero GitHub calls). Designed to screenshot or Save-as-PDF.
/// </summary>
public class CardModel : PageModel
{
    private readonly IAnalysisStore _store;

    public CardModel(IAnalysisStore store) => _store = store;

    public GitHubRepoAnalysisResult? Result { get; private set; }
    public DateTime? SavedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>Absolute URL of this card, for the "Copy link" button.</summary>
    public string ShareUrl { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string? owner, string? repo, CancellationToken ct)
    {
        ShareUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            ErrorMessage = "No repository was specified.";
            return Page();
        }

        var snapshot = await _store.GetSavedAsync(owner, repo, ct);
        if (snapshot is null)
        {
            ErrorMessage = "No saved analysis found for this repository — analyze it first, then open its health card.";
            return Page();
        }

        Result = snapshot.Result;
        SavedAt = snapshot.AnalyzedAt;
        return Page();
    }
}
