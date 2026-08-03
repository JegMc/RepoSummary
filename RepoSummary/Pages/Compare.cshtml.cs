using Microsoft.AspNetCore.Mvc.RazorPages;
using RepoSummary.Data;
using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Pages;

/// <summary>
/// Side-by-side comparison of two previously-analyzed repos. Free — reads saved
/// snapshots only (no GitHub calls).
/// </summary>
public class CompareModel : PageModel
{
    private readonly IAnalysisStore _store;

    public CompareModel(IAnalysisStore store) => _store = store;

    public IReadOnlyList<SavedAnalysis> Available { get; private set; } = Array.Empty<SavedAnalysis>();
    public GitHubRepoAnalysisResult? Left { get; private set; }
    public GitHubRepoAnalysisResult? Right { get; private set; }
    public string? SelectedA { get; private set; }
    public string? SelectedB { get; private set; }
    public string? Notice { get; private set; }

    public async Task OnGetAsync(string? a, string? b, CancellationToken ct)
    {
        Available = await _store.GetRecentAsync(100, ct);
        SelectedA = a;
        SelectedB = b;

        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            Notice = "Pick two different repositories to compare.";
            return;
        }

        var results = await _store.GetSavedManyAsync(new[] { a, b }, ct);
        Left = results.FirstOrDefault(r => r.FullName == a);
        Right = results.FirstOrDefault(r => r.FullName == b);

        if (Left is null || Right is null)
            Notice = "Couldn't load one of the selected analyses. Try re-analyzing it, then compare again.";
    }
}
