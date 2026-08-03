using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RepoSummary.Data;
using RepoSummary.Services;

namespace RepoSummary.Pages;

public class HistoryModel : PageModel
{
    private readonly IAnalysisStore _store;

    public HistoryModel(IAnalysisStore store) => _store = store;

    public IReadOnlyList<SavedAnalysis> Analyses { get; private set; } = Array.Empty<SavedAnalysis>();
    public IReadOnlyList<SkillCount> Skills { get; private set; } = Array.Empty<SkillCount>();
    public IReadOnlyList<SavedStory> Stories { get; private set; } = Array.Empty<SavedStory>();

    [TempData] public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Analyses = await _store.GetRecentAsync(100, ct);
        Skills = await _store.GetSkillInventoryAsync(ct);
        Stories = await _store.GetStoriesAsync(ct);
    }

    public async Task<IActionResult> OnPostDeleteStoryAsync(int id, CancellationToken ct)
    {
        await _store.DeleteStoryAsync(id, ct);
        StatusMessage = "Story removed from your bank.";
        return RedirectToPage();
    }
}
