using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RepoSummary.Data;
using RepoSummary.Models;

namespace RepoSummary.Services;

/// <summary>A restored analysis plus when it was saved (UTC).</summary>
public record SavedSnapshot(GitHubRepoAnalysisResult Result, DateTime AnalyzedAt);

/// <summary>One aggregated skill across all saved repos.</summary>
public record SkillCount(string Name, string Kind, int Repos);

public interface IAnalysisStore
{
    Task SaveAnalysisAsync(GitHubRepoAnalysisResult result, CancellationToken ct = default);
    Task<SavedSnapshot?> GetSavedAsync(string owner, string repo, CancellationToken ct = default);
    Task<IReadOnlyList<SavedAnalysis>> GetRecentAsync(int take, CancellationToken ct = default);
    Task<IReadOnlyList<GitHubRepoAnalysisResult>> GetSavedManyAsync(IEnumerable<string> fullNames, CancellationToken ct = default);
    Task<IReadOnlyList<SkillCount>> GetSkillInventoryAsync(CancellationToken ct = default);
    Task SaveStoriesAsync(string repoFullName, IEnumerable<GeneratedCareerArtifact> stories, CancellationToken ct = default);
    Task<IReadOnlyList<SavedStory>> GetStoriesAsync(CancellationToken ct = default);
    Task DeleteStoryAsync(int id, CancellationToken ct = default);
}

public class AnalysisStore : IAnalysisStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<AnalysisStore> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AnalysisStore(AppDbContext db, ILogger<AnalysisStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SaveAnalysisAsync(GitHubRepoAnalysisResult result, CancellationToken ct = default)
    {
        var row = await _db.Analyses.FirstOrDefaultAsync(a => a.FullName == result.FullName, ct)
                  ?? new SavedAnalysis();

        row.Owner = result.Owner;
        row.Name = result.Name;
        row.FullName = result.FullName;
        row.AnalyzedAt = DateTime.UtcNow;
        row.PrimaryLanguage = result.PrimaryLanguage;
        row.MaturityScore = result.Maturity?.Score;
        row.MaturityGrade = result.Maturity?.Grade;
        row.ResultJson = JsonSerializer.Serialize(result, JsonOpts);

        if (row.Id == 0) _db.Analyses.Add(row);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<SavedSnapshot?> GetSavedAsync(string owner, string repo, CancellationToken ct = default)
    {
        var full = $"{owner}/{repo}";
        var row = await _db.Analyses.AsNoTracking()
            .FirstOrDefaultAsync(a => a.FullName == full, ct);
        if (row is null) return null;

        try
        {
            var result = JsonSerializer.Deserialize<GitHubRepoAnalysisResult>(row.ResultJson, JsonOpts);
            return result is null ? null : new SavedSnapshot(result, row.AnalyzedAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not deserialize saved analysis for {Full}", full);
            return null;
        }
    }

    public async Task<IReadOnlyList<SavedAnalysis>> GetRecentAsync(int take, CancellationToken ct = default) =>
        await _db.Analyses.AsNoTracking()
            .OrderByDescending(a => a.AnalyzedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<GitHubRepoAnalysisResult>> GetSavedManyAsync(IEnumerable<string> fullNames, CancellationToken ct = default)
    {
        var names = fullNames.Distinct().ToList();
        var rows = await _db.Analyses.AsNoTracking()
            .Where(a => names.Contains(a.FullName))
            .Select(a => a.ResultJson)
            .ToListAsync(ct);

        var results = new List<GitHubRepoAnalysisResult>();
        foreach (var json in rows)
        {
            try
            {
                var r = JsonSerializer.Deserialize<GitHubRepoAnalysisResult>(json, JsonOpts);
                if (r is not null) results.Add(r);
            }
            catch { /* skip unreadable */ }
        }
        return results;
    }

    public async Task<IReadOnlyList<SkillCount>> GetSkillInventoryAsync(CancellationToken ct = default)
    {
        // Aggregate languages + dependencies across the most recent saved analyses.
        var rows = await _db.Analyses.AsNoTracking()
            .OrderByDescending(a => a.AnalyzedAt)
            .Take(50)
            .Select(a => a.ResultJson)
            .ToListAsync(ct);

        var langs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pkgs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var json in rows)
        {
            GitHubRepoAnalysisResult? r;
            try { r = JsonSerializer.Deserialize<GitHubRepoAnalysisResult>(json, JsonOpts); }
            catch { continue; }
            if (r is null) continue;

            foreach (var l in r.Languages.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase))
                langs[l] = langs.GetValueOrDefault(l) + 1;
            foreach (var p in r.Packages.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase))
                pkgs[p] = pkgs.GetValueOrDefault(p) + 1;
        }

        return langs.Select(kv => new SkillCount(kv.Key, "Language", kv.Value))
            .Concat(pkgs.Select(kv => new SkillCount(kv.Key, "Dependency", kv.Value)))
            .OrderByDescending(s => s.Repos)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToList();
    }

    public async Task SaveStoriesAsync(string repoFullName, IEnumerable<GeneratedCareerArtifact> stories, CancellationToken ct = default)
    {
        var existing = await _db.Stories
            .Where(s => s.RepoFullName == repoFullName)
            .Select(s => s.Content)
            .ToListAsync(ct);
        var seen = new HashSet<string>(existing);

        var added = false;
        foreach (var story in stories)
        {
            if (string.IsNullOrWhiteSpace(story.Content) || !seen.Add(story.Content)) continue;
            _db.Stories.Add(new SavedStory
            {
                RepoFullName = repoFullName,
                Title = story.Title,
                Content = story.Content,
                EvidenceCsv = story.Evidence.Count > 0 ? string.Join(", ", story.Evidence) : null,
                SavedAt = DateTime.UtcNow
            });
            added = true;
        }
        if (added) await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SavedStory>> GetStoriesAsync(CancellationToken ct = default) =>
        await _db.Stories.AsNoTracking().OrderByDescending(s => s.SavedAt).ToListAsync(ct);

    public async Task DeleteStoryAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.Stories.FindAsync(new object?[] { id }, ct);
        if (row is not null) { _db.Stories.Remove(row); await _db.SaveChangesAsync(ct); }
    }
}
