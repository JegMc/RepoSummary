using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RepoSummary.Data;
using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Pages;

public class PortfolioModel : PageModel
{
    private readonly IAnalysisStore _store;
    private readonly ICareerArtifactGenerator _generator;

    public PortfolioModel(IAnalysisStore store, ICareerArtifactGenerator generator)
    {
        _store = store;
        _generator = generator;
    }

    public IReadOnlyList<SavedAnalysis> Available { get; private set; } = Array.Empty<SavedAnalysis>();
    public bool AiConfigured => _generator.IsConfigured;

    [BindProperty] public List<string>? Selected { get; set; }
    [BindProperty] public string? Tone { get; set; }
    [BindProperty] public string? Job { get; set; }

    public List<GeneratedCareerArtifact>? Artifacts { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Available = await _store.GetRecentAsync(100, ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Available = await _store.GetRecentAsync(100, ct);

        if (Selected is null || Selected.Count < 2)
        {
            ErrorMessage = "Pick at least two repositories to synthesize.";
            return Page();
        }

        var results = await _store.GetSavedManyAsync(Selected, ct);
        if (results.Count == 0)
        {
            ErrorMessage = "Couldn't load the selected analyses. Try re-analyzing them.";
            return Page();
        }

        var options = new GenerationOptions
        {
            Types = new List<string> { "PortfolioNarrative", "ResumeBullet" },
            Tone = string.IsNullOrWhiteSpace(Tone) ? "balanced" : Tone,
            JobDescription = Job
        };

        var outcome = await _generator.GeneratePortfolioAsync(results, options, ct);
        if (outcome.Success)
            Artifacts = outcome.Value;
        else
            ErrorMessage = outcome.ErrorMessage;

        return Page();
    }
}
