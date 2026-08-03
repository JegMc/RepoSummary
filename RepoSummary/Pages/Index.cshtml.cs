using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RepoSummary.Data;
using RepoSummary.Services;

namespace RepoSummary.Pages;

public class IndexModel : PageModel
{
    private readonly GitHubUrlParser _parser;
    private readonly IAnalysisStore _store;

    public IndexModel(GitHubUrlParser parser, IAnalysisStore store)
    {
        _parser = parser;
        _store = store;
    }

    [BindProperty]
    public string? RepoInput { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Recently analyzed repos, for one-click re-open.</summary>
    public IReadOnlyList<SavedAnalysis> Recent { get; private set; } = Array.Empty<SavedAnalysis>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Recent = await _store.GetRecentAsync(6, ct);
    }

    public IActionResult OnPost()
    {
        if (!_parser.TryParse(RepoInput, out var reference, out var error))
        {
            ErrorMessage = error;
            return Page();
        }

        return RedirectToPage("Analysis", new { owner = reference!.Owner, repo = reference.Repo });
    }
}
