using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Pages;

public class AnalysisModel : PageModel
{
    private readonly IGitHubRepositoryService _service;
    private readonly ICareerArtifactGenerator _generator;
    private readonly IAnalysisStore _store;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    public AnalysisModel(
        IGitHubRepositoryService service,
        ICareerArtifactGenerator generator,
        IAnalysisStore store,
        IMemoryCache cache)
    {
        _service = service;
        _generator = generator;
        _store = store;
        _cache = cache;
    }

    public GitHubRepoAnalysisResult? Result { get; private set; }
    public string? ErrorMessage { get; private set; }

    // Set when this view is a re-opened saved snapshot rather than a fresh fetch.
    public bool IsSaved { get; private set; }
    public DateTime? SavedAt { get; private set; }

    // AI generation state.
    public bool AiConfigured => _generator.IsConfigured;
    public bool GenerationAttempted { get; private set; }
    public List<GeneratedCareerArtifact>? Artifacts { get; private set; }
    public string? GenerationError { get; private set; }

    // Generation options (bound from the Generate form; also drives the sticky form state).
    [BindProperty] public List<string>? GenTypes { get; set; }
    [BindProperty] public string? GenTone { get; set; }
    [BindProperty] public string? GenJob { get; set; }
    [BindProperty] public string? GenCategory { get; set; }   // "resume" | "interview"
    [BindProperty] public bool GenAts { get; set; }
    public GenerationOptions Options { get; private set; } = new();

    /// <summary>Which tab to open on load ("resume" default; the just-generated one after a POST).</summary>
    public string ActiveTab { get; private set; } = "resume";

    public async Task<IActionResult> OnGetAsync(string? owner, string? repo, bool saved, CancellationToken ct)
    {
        // Re-open a saved snapshot instantly (no GitHub calls) when asked.
        if (saved && !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo))
        {
            var snapshot = await _store.GetSavedAsync(owner, repo, ct);
            if (snapshot is not null)
            {
                Result = snapshot.Result;
                SavedAt = snapshot.AnalyzedAt;
                IsSaved = true;
                _cache.Set(CacheKey(owner, repo), Result, CacheFor);
                return Page();
            }
        }

        await LoadFreshAsync(owner, repo, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostGenerateAsync(string? owner, string? repo, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo) &&
            _cache.TryGetValue(CacheKey(owner, repo), out GitHubRepoAnalysisResult? cached) && cached is not null)
        {
            Result = cached;
        }
        else
        {
            await LoadFreshAsync(owner, repo, ct);
        }

        if (Result is null) return Page();

        var interview = GenCategory == "interview";
        ActiveTab = interview ? "interview" : "resume";

        var types = GenTypes is { Count: > 0 }
            ? new List<string>(GenTypes)
            : (interview ? new List<string> { "InterviewStory" } : new List<string> { "ResumeBullet", "ProjectSummary" });

        // Add the job-tailored extra relevant to this workspace when a JD is provided.
        if (!string.IsNullOrWhiteSpace(GenJob))
            types.Add(interview ? "JobFitGaps" : "HireabilityTips");

        Options = new GenerationOptions
        {
            Types = types,
            Tone = string.IsNullOrWhiteSpace(GenTone) ? "balanced" : GenTone,
            JobDescription = GenJob,
            AtsOptimize = GenAts
        };

        GenerationAttempted = true;
        var outcome = await _generator.GenerateAsync(Result, Options, ct);
        if (outcome.Success)
        {
            Artifacts = outcome.Value;
            // Keep generated interview stories in the STAR story bank.
            var stories = Artifacts!.Where(a => a.ArtifactType == "InterviewStory").ToList();
            if (stories.Count > 0)
                await _store.SaveStoriesAsync(Result.FullName, stories, ct);
        }
        else
        {
            GenerationError = outcome.ErrorMessage;
        }

        return Page();
    }

    private async Task LoadFreshAsync(string? owner, string? repo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            ErrorMessage = "No repository was specified.";
            return;
        }

        var outcome = await _service.AnalyzeAsync(new RepoReference(owner, repo), ct);
        if (outcome.Success)
        {
            Result = outcome.Value;
            _cache.Set(CacheKey(owner, repo), Result, CacheFor);
            await _store.SaveAnalysisAsync(Result!, ct);   // persist for history / re-open
        }
        else
        {
            ErrorMessage = outcome.ErrorMessage;
        }
    }

    private static string CacheKey(string owner, string repo) =>
        $"analysis:{owner.ToLowerInvariant()}/{repo.ToLowerInvariant()}";
}
