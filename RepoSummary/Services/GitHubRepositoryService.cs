using System.Net;
using System.Text;
using System.Text.Json;
using RepoSummary.Models;

namespace RepoSummary.Services;

public class GitHubRepositoryService : IGitHubRepositoryService
{
    private readonly HttpClient _http;
    private readonly ILogger<GitHubRepositoryService> _logger;
    private readonly GitHubRateLimitStore _rateLimit;
    private readonly GitHubTokenStore _tokens;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GitHubRepositoryService(
        HttpClient http,
        ILogger<GitHubRepositoryService> logger,
        GitHubRateLimitStore rateLimit,
        GitHubTokenStore tokens)
    {
        _http = http;
        _logger = logger;
        _rateLimit = rateLimit;
        _tokens = tokens;
    }

    /// <summary>GET wrapper that records the GitHub rate-limit headers on every response.</summary>
    private async Task<HttpResponseMessage> SendGetAsync(string path, CancellationToken ct)
    {
        var response = await _http.GetAsync(path, ct);
        CaptureRateLimit(response);
        return response;
    }

    private void CaptureRateLimit(HttpResponseMessage response)
    {
        try
        {
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remA) &&
                response.Headers.TryGetValues("X-RateLimit-Limit", out var limA) &&
                int.TryParse(remA.FirstOrDefault(), out var remaining) &&
                int.TryParse(limA.FirstOrDefault(), out var limit))
            {
                DateTimeOffset? reset = null;
                if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resA) &&
                    long.TryParse(resA.FirstOrDefault(), out var unix))
                    reset = DateTimeOffset.FromUnixTimeSeconds(unix);

                _rateLimit.Update(remaining, limit, reset);
            }
        }
        catch { /* rate-limit display is best-effort */ }
    }

    private static string? NormalizeLicense(LicenseDto? license)
    {
        if (license is null) return null;
        if (!string.IsNullOrWhiteSpace(license.SpdxId) &&
            !license.SpdxId.Equals("NOASSERTION", StringComparison.OrdinalIgnoreCase))
            return license.SpdxId;
        return string.IsNullOrWhiteSpace(license.Name) ? null : license.Name;
    }

    public async Task<ServiceResult<GitHubRepoAnalysisResult>> AnalyzeAsync(
        RepoReference reference, CancellationToken cancellationToken = default)
    {
        var basePath = $"repos/{Uri.EscapeDataString(reference.Owner)}/{Uri.EscapeDataString(reference.Repo)}";

        try
        {
            // 1. Core repo metadata. A failure here is fatal for the whole analysis.
            var repoResponse = await SendGetAsync(basePath, cancellationToken);
            if (!repoResponse.IsSuccessStatusCode)
                return ServiceResult<GitHubRepoAnalysisResult>.Fail(
                    DescribeFailure(repoResponse, reference));

            var repo = await ReadJsonAsync<RepoDto>(repoResponse, cancellationToken);
            if (repo is null)
                return ServiceResult<GitHubRepoAnalysisResult>.Fail(
                    "GitHub returned an unexpected response for this repository.");

            var result = new GitHubRepoAnalysisResult
            {
                Owner = repo.Owner?.Login ?? reference.Owner,
                Name = repo.Name ?? reference.Repo,
                Description = repo.Description,
                DefaultBranch = repo.DefaultBranch,
                Stars = repo.Stars,
                Forks = repo.Forks,
                OpenIssues = repo.OpenIssues,
                PrimaryLanguage = repo.Language,
                License = NormalizeLicense(repo.License),
                CreatedAt = repo.CreatedAt,
                UpdatedAt = repo.UpdatedAt,
                HtmlUrl = repo.HtmlUrl
            };

            // 2. Secondary calls. Each is best-effort — a failure just leaves that
            //    section empty rather than failing the whole page.
            //    When authenticated, one GraphQL call replaces the README + languages + commits +
            //    releases REST calls (four → one); otherwise we use the REST endpoints.
            var core = _tokens.HasValue
                ? await TryGetCoreViaGraphQlAsync(reference, cancellationToken)
                : null;

            if (core is not null)
            {
                result.ReadmeContent = core.Readme ?? await TryGetReadmeAsync(basePath, cancellationToken);
                result.RecentCommits = core.Commits;
                result.Languages = core.Languages;
                result.ReleaseCount = core.ReleaseCount;
                result.LatestReleaseName = core.LatestReleaseName;
                result.LatestReleaseDate = core.LatestReleaseDate;
            }
            else
            {
                result.ReadmeContent = await TryGetReadmeAsync(basePath, cancellationToken);
                result.RecentCommits = await TryGetCommitsAsync(basePath, cancellationToken);
                result.Languages = await TryGetLanguagesAsync(basePath, cancellationToken);
            }
            result.TopLevelItems = await TryGetContentsAsync(basePath, cancellationToken);

            // Recursive tree = the whole file structure in ONE call. This is what
            // lets analysis see nested signals (a DbContext three folders deep, a
            // workflow under .github/workflows) instead of only the top level.
            var (paths, truncated, blobs) = await TryGetRecursiveTreeAsync(
                basePath, result.DefaultBranch, cancellationToken);

            // Very large repos: cap what we keep so memory, the stored JSON snapshot, and the
            // file explorer stay bounded. We flag truncation so the UI can say so.
            if (paths.Count > MaxTrackedPaths)
            {
                paths = paths.Take(MaxTrackedPaths).ToList();
                truncated = true;
            }
            if (blobs.Count > MaxScannedBlobs)
                blobs = blobs.Take(MaxScannedBlobs).ToList();

            result.AllPaths = paths;
            result.TreeTruncated = truncated;

            // Read a bounded set of manifest files to extract real dependencies.
            result.Packages = await TryGetPackagesAsync(basePath, paths, cancellationToken);

            // Phase 8: read the highest-signal source files so we reason about real code.
            result.KeyFiles = await TryGetKeyFilesAsync(
                basePath, result.HtmlUrl, result.DefaultBranch, blobs, cancellationToken);

            // What the CI actually does + shipping signals.
            result.Workflows = await TryGetWorkflowsAsync(basePath, paths, cancellationToken);
            if (core is null)   // GraphQL already fetched releases when authenticated
            {
                var (relCount, relName, relDate) = await TryGetReleasesAsync(basePath, cancellationToken);
                result.ReleaseCount = relCount;
                result.LatestReleaseName = relName;
                result.LatestReleaseDate = relDate;
            }
            result.HasChangelog = paths.Any(p =>
            {
                var n = p.Split('/').Last();
                return n.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase)
                       || n.Equals("HISTORY.md", StringComparison.OrdinalIgnoreCase);
            });

            // 3. Derive evidence + rule-based angles.
            RepoAnalyzer.Populate(result);

            return ServiceResult<GitHubRepoAnalysisResult>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error contacting GitHub for {Owner}/{Repo}",
                reference.Owner, reference.Repo);
            return ServiceResult<GitHubRepoAnalysisResult>.Fail(
                "Could not reach GitHub. Check your internet connection and try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error analyzing {Owner}/{Repo}",
                reference.Owner, reference.Repo);
            return ServiceResult<GitHubRepoAnalysisResult>.Fail(
                "Something went wrong while analyzing this repository. Please try again.");
        }
    }

    public async Task<ServiceResult<List<UserRepoSummary>>> GetUserReposAsync(
        string user, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendGetAsync(
                $"users/{Uri.EscapeDataString(user)}/repos?per_page=100&sort=updated&type=owner", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ServiceResult<List<UserRepoSummary>>.Fail($"GitHub user '{user}' was not found.");
            if (!response.IsSuccessStatusCode)
                return ServiceResult<List<UserRepoSummary>>.Fail(
                    DescribeFailure(response, new RepoReference(user, "")));

            var dtos = await ReadJsonAsync<List<RepoDto>>(response, cancellationToken) ?? new();
            var list = dtos
                .Where(d => !d.Fork && !string.IsNullOrEmpty(d.Name))
                .Select(d => new UserRepoSummary
                {
                    Owner = d.Owner?.Login ?? user,
                    Name = d.Name!,
                    Description = d.Description,
                    Stars = d.Stars,
                    Language = d.Language,
                    UpdatedAt = d.UpdatedAt
                })
                .OrderByDescending(r => r.Stars)
                .ThenByDescending(r => r.UpdatedAt)
                .ToList();
            return ServiceResult<List<UserRepoSummary>>.Ok(list);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error listing repos for {User}", user);
            return ServiceResult<List<UserRepoSummary>>.Fail("Could not reach GitHub. Check your connection and try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing repos for {User}", user);
            return ServiceResult<List<UserRepoSummary>>.Fail("Something went wrong listing that user's repositories.");
        }
    }

    private static string DescribeFailure(HttpResponseMessage response, RepoReference reference)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return $"Repository '{reference.Owner}/{reference.Repo}' was not found. " +
                       "It may be private, renamed, or misspelled. This app only supports public repos.";

            case HttpStatusCode.Forbidden:
            case (HttpStatusCode)429:
                var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var vals)
                    ? vals.FirstOrDefault()
                    : null;
                if (remaining == "0")
                    return "GitHub's unauthenticated rate limit (60 requests/hour) has been reached. " +
                           "Wait a while, or add a GitHub token in configuration (GitHub:Token) to raise the limit.";
                return "GitHub declined the request (403). This is usually a rate limit. Try again shortly.";

            default:
                return $"GitHub returned an error ({(int)response.StatusCode} {response.ReasonPhrase}). Please try again.";
        }
    }

    private async Task<string?> TryGetReadmeAsync(string basePath, CancellationToken ct)
    {
        try
        {
            var response = await SendGetAsync($"{basePath}/readme", ct);
            if (!response.IsSuccessStatusCode) return null;

            var dto = await ReadJsonAsync<ReadmeDto>(response, ct);
            if (dto?.Content is null) return null;

            if (string.Equals(dto.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                // GitHub wraps the base64 payload at 76 chars with newlines.
                var cleaned = dto.Content.Replace("\n", "").Replace("\r", "");
                var bytes = Convert.FromBase64String(cleaned);
                return Encoding.UTF8.GetString(bytes);
            }

            return dto.Content;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch README for {Base}", basePath);
            return null;
        }
    }

    private async Task<List<GitHubCommitSummary>> TryGetCommitsAsync(string basePath, CancellationToken ct)
    {
        try
        {
            var response = await SendGetAsync($"{basePath}/commits?per_page=10", ct);
            if (!response.IsSuccessStatusCode) return new();

            var dtos = await ReadJsonAsync<List<CommitDto>>(response, ct) ?? new();
            return dtos.Select(c => new GitHubCommitSummary
            {
                Sha = c.Sha ?? "",
                Message = c.Commit?.Message ?? "",
                Author = c.Commit?.Author?.Name,
                Date = c.Commit?.Author?.Date,
                HtmlUrl = c.HtmlUrl
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch commits for {Base}", basePath);
            return new();
        }
    }

    private async Task<List<GitHubLanguageSummary>> TryGetLanguagesAsync(string basePath, CancellationToken ct)
    {
        try
        {
            var response = await SendGetAsync($"{basePath}/languages", ct);
            if (!response.IsSuccessStatusCode) return new();

            var map = await ReadJsonAsync<Dictionary<string, long>>(response, ct) ?? new();
            var total = map.Values.Sum();
            if (total == 0) return new();

            return map
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new GitHubLanguageSummary
                {
                    Name = kv.Key,
                    Bytes = kv.Value,
                    Percentage = Math.Round(kv.Value * 100.0 / total, 1)
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch languages for {Base}", basePath);
            return new();
        }
    }

    private async Task<List<GitHubFileTreeItem>> TryGetContentsAsync(string basePath, CancellationToken ct)
    {
        try
        {
            var response = await SendGetAsync($"{basePath}/contents/", ct);
            if (!response.IsSuccessStatusCode) return new();

            var dtos = await ReadJsonAsync<List<ContentItemDto>>(response, ct) ?? new();
            return dtos
                .Select(i => new GitHubFileTreeItem
                {
                    Name = i.Name ?? "",
                    Path = i.Path ?? "",
                    Type = i.Type ?? "",
                    HtmlUrl = i.HtmlUrl
                })
                .OrderByDescending(i => i.IsDirectory)   // directories first
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch contents for {Base}", basePath);
            return new();
        }
    }

    /// <summary>
    /// Fetches the entire file tree in a single recursive call. Returns the paths
    /// and whether GitHub truncated the response (only happens on very large repos).
    /// </summary>
    private async Task<(List<string> Paths, bool Truncated, List<SourceFileSelector.Candidate> Blobs)> TryGetRecursiveTreeAsync(
        string basePath, string? defaultBranch, CancellationToken ct)
    {
        try
        {
            var branch = string.IsNullOrWhiteSpace(defaultBranch) ? "HEAD" : defaultBranch;
            var response = await _http.GetAsync(
                $"{basePath}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1", ct);
            if (!response.IsSuccessStatusCode) return (new(), false, new());

            var dto = await ReadJsonAsync<TreeResponseDto>(response, ct);
            if (dto?.Tree is null) return (new(), false, new());

            var paths = dto.Tree
                .Where(t => !string.IsNullOrEmpty(t.Path))
                .Select(t => t.Path!)
                .ToList();

            // Files (blobs) with sizes — used to pick the highest-signal source files to read.
            var blobs = dto.Tree
                .Where(t => !string.IsNullOrEmpty(t.Path) && t.Type == "blob")
                .Select(t => new SourceFileSelector.Candidate(t.Path!, t.Size))
                .ToList();

            return (paths, dto.Truncated, blobs);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch recursive tree for {Base}", basePath);
            return (new(), false, new());
        }
    }

    // How many source files to actually read (each is one API call).
    private const int MaxKeyFileReads = 6;
    // Cap the excerpt we keep per file — enough for the model to reason about, small enough to bound cost.
    private const int KeyFileSnippetLines = 160;
    private const int KeyFileSnippetChars = 6000;

    /// <summary>
    /// Reads a bounded set of the highest-signal source files (entrypoints + largest/central
    /// files) so downstream generation and the architecture diagram can reason about real code.
    /// </summary>
    private async Task<List<KeyFile>> TryGetKeyFilesAsync(
        string basePath, string? htmlUrl, string? defaultBranch,
        List<SourceFileSelector.Candidate> blobs, CancellationToken ct)
    {
        var picks = SourceFileSelector.Select(blobs, MaxKeyFileReads);
        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? "HEAD" : defaultBranch;
        var files = new List<KeyFile>();

        foreach (var pick in picks)
        {
            var content = await TryGetFileContentAsync(basePath, pick.Path, ct);
            if (string.IsNullOrEmpty(content)) continue;

            files.Add(new KeyFile
            {
                Path = pick.Path,
                Reason = pick.Reason,
                Bytes = pick.Bytes,
                HtmlUrl = string.IsNullOrEmpty(htmlUrl) ? null : $"{htmlUrl}/blob/{branch}/{pick.Path}",
                Snippet = Excerpt(content),
                Imports = SourceImportParser.ExtractImports(pick.Path, content)
            });
        }

        return files;
    }

    private static string Excerpt(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var kept = lines.Length > KeyFileSnippetLines
            ? string.Join('\n', lines.Take(KeyFileSnippetLines)) + "\n… (truncated)"
            : content;
        if (kept.Length > KeyFileSnippetChars) kept = kept[..KeyFileSnippetChars] + " …";
        return kept;
    }

    // Manifest files we know how to read dependencies out of.
    private static readonly string[] ManifestNames =
    {
        "package.json", "requirements.txt", "pyproject.toml", "go.mod", "Cargo.toml", "pom.xml",
        "Gemfile", "composer.json", "build.gradle", "build.gradle.kts", "pubspec.yaml", "Podfile"
    };

    // Reading file contents costs one API call each, so cap how many we fetch.
    private const int MaxManifestReads = 8;

    // Very-large-repo guards: bound how many paths we keep and how many blobs we scan for
    // key-file selection, so a monorepo with 100k+ files can't blow up memory or the DB snapshot.
    private const int MaxTrackedPaths = 20000;
    private const int MaxScannedBlobs = 20000;

    /// <summary>
    /// Reads a bounded set of manifest files and extracts declared dependencies
    /// (with versions where the manifest states them).
    /// </summary>
    private async Task<List<GitHubPackageSummary>> TryGetPackagesAsync(
        string basePath, List<string> allPaths, CancellationToken ct)
    {
        var manifests = allPaths
            .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        || ManifestNames.Contains(
                            p.Split('/').Last(), StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p.Count(c => c == '/'))   // shallower files first (usually the main ones)
            .Take(MaxManifestReads)
            .ToList();

        var packages = new List<GitHubPackageSummary>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in manifests)
        {
            var content = await TryGetFileContentAsync(basePath, path, ct);
            if (string.IsNullOrEmpty(content)) continue;

            var fileName = path.Split('/').Last();
            foreach (var (name, version) in ManifestParser.ParseDependencies(fileName, content))
            {
                if (seen.Add(name))
                    packages.Add(new GitHubPackageSummary
                    {
                        Name = name,
                        Source = fileName,
                        SourcePath = path,
                        Version = ManifestParser.CleanVersion(version)
                    });
            }
        }

        return packages;
    }

    /// <summary>Reads CI workflow files and detects, at a high level, what each one does.</summary>
    private async Task<List<WorkflowSummary>> TryGetWorkflowsAsync(
        string basePath, List<string> allPaths, CancellationToken ct)
    {
        var files = allPaths
            .Where(p => p.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)
                        && (p.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p)
            .Take(5)
            .ToList();

        var list = new List<WorkflowSummary>();
        foreach (var path in files)
        {
            var content = await TryGetFileContentAsync(basePath, path, ct);
            if (string.IsNullOrEmpty(content)) continue;
            list.Add(new WorkflowSummary { Name = path.Split('/').Last(), Does = ManifestParser.DetectWorkflowActions(content) });
        }
        return list;
    }

    /// <summary>Fetches recent releases (one call) for the "ships tagged releases" signal.</summary>
    private async Task<(int? Count, string? Name, DateTimeOffset? Date)> TryGetReleasesAsync(
        string basePath, CancellationToken ct)
    {
        try
        {
            var response = await SendGetAsync($"{basePath}/releases?per_page=10", ct);
            if (!response.IsSuccessStatusCode) return (null, null, null);

            var dtos = await ReadJsonAsync<List<ReleaseDto>>(response, ct) ?? new();
            if (dtos.Count == 0) return (0, null, null);

            var latest = dtos[0];
            var name = !string.IsNullOrWhiteSpace(latest.Name) ? latest.Name : latest.TagName;
            return (dtos.Count, name, latest.PublishedAt);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch releases for {Base}", basePath);
            return (null, null, null);
        }
    }

    // One GraphQL query that replaces the README + languages + commits + releases REST calls
    // (four calls → one). GitHub's GraphQL API requires a token, so this only runs when one is
    // configured; any failure returns null and the caller falls back to the REST path.
    private const string CoreGraphQlQuery =
        "query($owner:String!,$name:String!){repository(owner:$owner,name:$name){" +
        "languages(first:10,orderBy:{field:SIZE,direction:DESC}){edges{size node{name}}totalSize}" +
        "readme:object(expression:\"HEAD:README.md\"){... on Blob{text}}" +
        "releases(first:10){totalCount nodes{name tagName publishedAt}}" +
        "defaultBranchRef{target{... on Commit{history(first:10){nodes{oid messageHeadline committedDate url}}}}}" +
        "}}";

    private sealed record GraphQlCore(
        string? Readme, List<GitHubLanguageSummary> Languages, List<GitHubCommitSummary> Commits,
        int? ReleaseCount, string? LatestReleaseName, DateTimeOffset? LatestReleaseDate);

    private async Task<GraphQlCore?> TryGetCoreViaGraphQlAsync(RepoReference reference, CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                query = CoreGraphQlQuery,
                variables = new { owner = reference.Owner, name = reference.Repo }
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, "graphql")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            var response = await _http.SendAsync(request, ct);
            CaptureRateLimit(response);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out _)) return null;
            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("repository", out var repo) || repo.ValueKind != JsonValueKind.Object)
                return null;

            // Languages
            var languages = new List<GitHubLanguageSummary>();
            if (repo.TryGetProperty("languages", out var langs))
            {
                long total = langs.TryGetProperty("totalSize", out var ts) ? ts.GetInt64() : 0;
                if (langs.TryGetProperty("edges", out var edges) && edges.ValueKind == JsonValueKind.Array)
                    foreach (var e in edges.EnumerateArray())
                    {
                        var size = e.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        var name = e.GetProperty("node").GetProperty("name").GetString() ?? "";
                        if (name.Length == 0 || total == 0) continue;
                        languages.Add(new GitHubLanguageSummary
                        {
                            Name = name, Bytes = size, Percentage = Math.Round(size * 100.0 / total, 1)
                        });
                    }
            }

            // README
            string? readme = null;
            if (repo.TryGetProperty("readme", out var rd) && rd.ValueKind == JsonValueKind.Object &&
                rd.TryGetProperty("text", out var rt) && rt.ValueKind == JsonValueKind.String)
                readme = rt.GetString();

            // Releases
            int? releaseCount = null; string? relName = null; DateTimeOffset? relDate = null;
            if (repo.TryGetProperty("releases", out var rel))
            {
                releaseCount = rel.TryGetProperty("totalCount", out var rc) ? rc.GetInt32() : 0;
                if (rel.TryGetProperty("nodes", out var rnodes) && rnodes.ValueKind == JsonValueKind.Array &&
                    rnodes.GetArrayLength() > 0)
                {
                    var first = rnodes[0];
                    relName = (first.TryGetProperty("name", out var rn) ? rn.GetString() : null);
                    if (string.IsNullOrWhiteSpace(relName) && first.TryGetProperty("tagName", out var tn)) relName = tn.GetString();
                    if (first.TryGetProperty("publishedAt", out var pa) && pa.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(pa.GetString(), out var pd)) relDate = pd;
                }
            }

            // Recent commits
            var commits = new List<GitHubCommitSummary>();
            if (repo.TryGetProperty("defaultBranchRef", out var dbr) && dbr.ValueKind == JsonValueKind.Object &&
                dbr.TryGetProperty("target", out var target) &&
                target.TryGetProperty("history", out var hist) &&
                hist.TryGetProperty("nodes", out var cnodes) && cnodes.ValueKind == JsonValueKind.Array)
                foreach (var c in cnodes.EnumerateArray())
                {
                    var summary = new GitHubCommitSummary
                    {
                        Sha = c.TryGetProperty("oid", out var oid) ? oid.GetString() ?? "" : "",
                        Message = c.TryGetProperty("messageHeadline", out var mh) ? mh.GetString() ?? "" : "",
                        HtmlUrl = c.TryGetProperty("url", out var u) ? u.GetString() : null
                    };
                    if (c.TryGetProperty("committedDate", out var cd) && cd.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(cd.GetString(), out var dt)) summary.Date = dt;
                    commits.Add(summary);
                }

            return new GraphQlCore(readme, languages, commits, releaseCount, relName, relDate);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GraphQL core fetch failed for {Owner}/{Repo}; falling back to REST.",
                reference.Owner, reference.Repo);
            return null;
        }
    }

    private async Task<string?> TryGetFileContentAsync(string basePath, string path, CancellationToken ct)
    {
        try
        {
            var response = await SendGetAsync(
                $"{basePath}/contents/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}", ct);
            if (!response.IsSuccessStatusCode) return null;

            var dto = await ReadJsonAsync<ContentItemDto>(response, ct);
            if (dto?.Content is null) return null;

            if (string.Equals(dto.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                var cleaned = dto.Content.Replace("\n", "").Replace("\r", "");
                return Encoding.UTF8.GetString(Convert.FromBase64String(cleaned));
            }
            return dto.Content;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch file {Path} for {Base}", path, basePath);
            return null;
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }
}
