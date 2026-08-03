using System.Collections.Concurrent;
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
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    // Stale-while-revalidate: a snapshot younger than this is served without a background refresh.
    private static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(10);
    // Guards against firing more than one background refresh per repo at a time.
    private static readonly ConcurrentDictionary<string, byte> Refreshing = new();

    public AnalysisModel(
        IGitHubRepositoryService service,
        ICareerArtifactGenerator generator,
        IAnalysisStore store,
        IMemoryCache cache,
        IServiceScopeFactory scopeFactory)
    {
        _service = service;
        _generator = generator;
        _store = store;
        _cache = cache;
        _scopeFactory = scopeFactory;
    }

    public GitHubRepoAnalysisResult? Result { get; private set; }
    public string? ErrorMessage { get; private set; }

    // Set when this view is a re-opened saved snapshot rather than a fresh fetch.
    public bool IsSaved { get; private set; }
    public DateTime? SavedAt { get; private set; }

    // Stale-while-revalidate: this view was served instantly from a saved snapshot, and
    // (when stale) a background refresh is updating it for next time.
    public bool ServedFromCache { get; private set; }
    public bool RefreshingInBackground { get; private set; }

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

    public async Task<IActionResult> OnGetAsync(string? owner, string? repo, bool saved, bool fresh, CancellationToken ct)
    {
        if (!fresh && !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo))
        {
            // Re-open a saved snapshot instantly (no GitHub calls) when explicitly asked.
            if (saved)
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
            else
            {
                // Stale-while-revalidate: if we already have a snapshot, render it instantly and,
                // when it's older than the fresh window, refresh it in the background for next time.
                var snapshot = await _store.GetSavedAsync(owner, repo, ct);
                if (snapshot is not null)
                {
                    Result = snapshot.Result;
                    SavedAt = snapshot.AnalyzedAt;
                    ServedFromCache = true;
                    _cache.Set(CacheKey(owner, repo), Result, CacheFor);

                    if (DateTime.UtcNow - snapshot.AnalyzedAt > FreshWindow)
                        RefreshingInBackground = QueueBackgroundRefresh(owner, repo);

                    return Page();
                }
            }
        }

        await LoadFreshAsync(owner, repo, ct);
        return Page();
    }

    /// <summary>Fire-and-forget re-analysis in its own DI scope, so a stale view refreshes for
    /// next time without blocking this request. Deduped per repo.</summary>
    private bool QueueBackgroundRefresh(string owner, string repo)
    {
        var key = $"{owner}/{repo}".ToLowerInvariant();
        if (!Refreshing.TryAdd(key, 0)) return true;   // one already in flight

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IGitHubRepositoryService>();
                var store = scope.ServiceProvider.GetRequiredService<IAnalysisStore>();
                var outcome = await svc.AnalyzeAsync(new RepoReference(owner, repo), CancellationToken.None);
                if (outcome.Success && outcome.Value is not null)
                    await store.SaveAnalysisAsync(outcome.Value, CancellationToken.None);
            }
            catch { /* best-effort background refresh */ }
            finally { Refreshing.TryRemove(key, out _); }
        });
        return true;
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
