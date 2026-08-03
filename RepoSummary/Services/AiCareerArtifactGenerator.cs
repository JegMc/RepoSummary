using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using RepoSummary.Models;

namespace RepoSummary.Services;

/// <summary>
/// Generates career artifacts with an LLM, grounded in the evidence RepoSummary
/// already extracted. Supports either OpenAI (ChatGPT) or Anthropic (Claude) — it
/// uses whichever API key is configured on the Settings page (and a saved
/// preference when both are set). The product thesis lives here: we hand the model
/// real repo facts and forbid claims that aren't backed by them.
/// </summary>
public class AiCareerArtifactGenerator : ICareerArtifactGenerator
{
    private readonly OpenAiKeyStore _openAi;
    private readonly AnthropicKeyStore _anthropic;
    private readonly AiProviderStore _preference;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AiCareerArtifactGenerator> _logger;

    private const string OpenAiModel = "gpt-4o";
    private const string AnthropicModel = "claude-opus-4-8";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AiCareerArtifactGenerator(
        OpenAiKeyStore openAi,
        AnthropicKeyStore anthropic,
        AiProviderStore preference,
        IHttpClientFactory httpFactory,
        ILogger<AiCareerArtifactGenerator> logger)
    {
        _openAi = openAi;
        _anthropic = anthropic;
        _preference = preference;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public bool IsConfigured => _openAi.HasValue || _anthropic.HasValue;

    /// <summary>Which provider a generation would use right now: "openai", "anthropic", or null.</summary>
    public string? ActiveProvider
    {
        get
        {
            var hasOpenAi = _openAi.HasValue;
            var hasAnthropic = _anthropic.HasValue;
            if (!hasOpenAi && !hasAnthropic) return null;
            if (hasOpenAi && !hasAnthropic) return AiProviderStore.OpenAi;
            if (hasAnthropic && !hasOpenAi) return AiProviderStore.Anthropic;
            // Both set → honour the saved preference, defaulting to OpenAI.
            return _preference.Preference == AiProviderStore.Anthropic ? AiProviderStore.Anthropic : AiProviderStore.OpenAi;
        }
    }

    /// <summary>Human-readable name of the active provider (for the UI).</summary>
    public string ActiveProviderName => ActiveProvider switch
    {
        AiProviderStore.OpenAi => "OpenAI (ChatGPT)",
        AiProviderStore.Anthropic => "Anthropic (Claude)",
        _ => "none"
    };

    public async Task<ServiceResult<List<GeneratedCareerArtifact>>> GenerateAsync(
        GitHubRepoAnalysisResult analysis, GenerationOptions options, CancellationToken cancellationToken = default)
    {
        var provider = ActiveProvider;
        if (provider is null)
            return ServiceResult<List<GeneratedCareerArtifact>>.Fail(
                "Add an OpenAI or Anthropic API key on the Settings page to enable AI generation.");

        var brief = BuildBrief(analysis, options);

        try
        {
            return provider == AiProviderStore.Anthropic
                ? await GenerateWithAnthropicAsync(brief, cancellationToken)
                : await GenerateWithOpenAiAsync(brief, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI generation failed for {Repo} via {Provider}", analysis.FullName, provider);
            return ServiceResult<List<GeneratedCareerArtifact>>.Fail(
                "Something went wrong while generating career material. This can be a rate limit or " +
                "a temporary API issue — please try again in a moment.");
        }
    }

    // ---------------- Multi-repo synthesis ----------------

    public async Task<ServiceResult<List<GeneratedCareerArtifact>>> GeneratePortfolioAsync(
        IReadOnlyList<GitHubRepoAnalysisResult> analyses, GenerationOptions options, CancellationToken cancellationToken = default)
    {
        var provider = ActiveProvider;
        if (provider is null)
            return ServiceResult<List<GeneratedCareerArtifact>>.Fail(
                "Add an OpenAI or Anthropic API key on the Settings page to enable AI generation.");
        if (analyses.Count == 0)
            return ServiceResult<List<GeneratedCareerArtifact>>.Fail("Select at least one repository to synthesize.");

        var brief = BuildPortfolioBrief(analyses, options);
        try
        {
            return provider == AiProviderStore.Anthropic
                ? await GenerateWithAnthropicAsync(brief, cancellationToken)
                : await GenerateWithOpenAiAsync(brief, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Portfolio synthesis failed via {Provider}", provider);
            return ServiceResult<List<GeneratedCareerArtifact>>.Fail(
                "Something went wrong while synthesizing. This can be a rate limit or a temporary API issue — please try again.");
        }
    }

    private static string BuildPortfolioBrief(IReadOnlyList<GitHubRepoAnalysisResult> analyses, GenerationOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are given evidence from SEVERAL repositories built by the same developer. Synthesize across all of them.");
        sb.AppendLine();
        var n = 1;
        foreach (var r in analyses)
        {
            sb.AppendLine($"--- Repository {n++}: {r.FullName} ---");
            if (!string.IsNullOrWhiteSpace(r.Description)) sb.AppendLine($"Description: {r.Description}");
            if (r.Languages.Count > 0)
                sb.AppendLine("Languages: " + string.Join(", ", r.Languages.Take(6).Select(l => l.Name)));
            if (r.Packages.Count > 0)
                sb.AppendLine("Key dependencies: " + string.Join(", ", r.Packages.Select(p => p.Name).Distinct().Take(15)));
            if (r.Maturity is not null) sb.AppendLine($"Maturity: {r.Maturity.Grade} ({r.Maturity.Score}/100)");
            foreach (var a in r.ResumeAngles.Take(3)) sb.AppendLine($"- {a.Text}");
            sb.AppendLine();
        }
        sb.AppendLine(ProductionSpec(options));
        sb.AppendLine("Draw the through-line across these projects. Ground everything strictly in the evidence above; do not invent facts.");
        return sb.ToString();
    }

    // ---------------- Streaming (OpenAI plain-Markdown; Anthropic falls back to one chunk) ----------------

    public async IAsyncEnumerable<string> StreamAsync(
        GitHubRepoAnalysisResult analysis, GenerationOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (ActiveProvider is null)
        {
            yield return "Add an OpenAI or Anthropic API key on the Settings page to enable AI generation.";
            yield break;
        }

        // For Anthropic we don't stream — run the structured generate and emit it as Markdown once.
        if (ActiveProvider == AiProviderStore.Anthropic)
        {
            var res = await GenerateAsync(analysis, options, cancellationToken);
            if (res.Success)
                foreach (var a in res.Value!)
                    yield return $"## {a.Title}\n\n{a.Content}\n\n{(a.Evidence.Count > 0 ? "Evidence: " + string.Join(", ", a.Evidence) + "\n\n" : "")}";
            else
                yield return res.ErrorMessage ?? "Generation failed.";
            yield break;
        }

        // OpenAI: true token streaming of plain Markdown.
        await foreach (var piece in StreamOpenAiChatAsync(SystemPrompt, BuildStreamBrief(analysis, options), 4096, cancellationToken))
            yield return piece;
    }

    /// <summary>Shared OpenAI chat-completions token stream (SSE) → plain text pieces.</summary>
    private async IAsyncEnumerable<string> StreamOpenAiChatAsync(
        string system, string user, int maxTokens, [EnumeratorCancellation] CancellationToken ct)
    {
        var body = new
        {
            model = OpenAiModel,
            stream = true,
            max_tokens = maxTokens,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.Value);

        var http = _httpFactory.CreateClient("OpenAI");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            yield return DescribeOpenAiError(response.StatusCode, errBody);
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;

            string? piece = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    piece = c.GetString();
            }
            catch { /* keep-alive or non-JSON line */ }

            if (!string.IsNullOrEmpty(piece)) yield return piece;
        }
    }

    // ---------------- Ask this repo (code Q&A) ----------------

    private const string QaSystemPrompt =
        """
        You are a precise technical assistant answering questions about ONE GitHub repository.
        You are given evidence extracted from it: languages, dependencies, structure, recent
        commits, the README, and excerpts of its key source files. That evidence is your ONLY
        ground truth.

        Rules:
        - Answer only from the evidence. If it doesn't contain the answer, say so plainly
          ("The provided evidence doesn't show …") instead of guessing.
        - Be concrete: cite file paths, class/function names, dependencies, or commit shas from the evidence.
        - Keep it tight — a few short paragraphs or a bullet list, in GitHub-flavored Markdown.
        - Never invent files, APIs, metrics, or behavior that aren't visible in the evidence.
        """;

    public async IAsyncEnumerable<string> AnswerAsync(
        GitHubRepoAnalysisResult analysis, string question, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (ActiveProvider is null)
        {
            yield return "Add an OpenAI or Anthropic API key on the Settings page to ask questions.";
            yield break;
        }
        if (string.IsNullOrWhiteSpace(question))
        {
            yield return "Type a question about this repository.";
            yield break;
        }

        var sb = new StringBuilder();
        AppendEvidenceContext(sb, analysis, new GenerationOptions());
        sb.AppendLine();
        sb.AppendLine("QUESTION: " + question.Trim());
        var user = sb.ToString();

        if (ActiveProvider == AiProviderStore.Anthropic)
        {
            yield return await AnswerWithAnthropicAsync(user, cancellationToken);
            yield break;
        }

        await foreach (var piece in StreamOpenAiChatAsync(QaSystemPrompt, user, 2048, cancellationToken))
            yield return piece;
    }

    private async Task<string> AnswerWithAnthropicAsync(string user, CancellationToken ct)
    {
        try
        {
            var client = new AnthropicClient { ApiKey = _anthropic.Value };
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = AnthropicModel,
                MaxTokens = 2048,
                System = QaSystemPrompt,
                Messages = [new() { Role = Role.User, Content = user }]
            }, ct);
            return response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text
                   ?? "The model returned an empty answer.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anthropic Q&A failed.");
            return "Something went wrong answering that — this can be a rate limit or a temporary API issue. Please try again.";
        }
    }

    // ---------------- OpenAI (Chat Completions + structured outputs) ----------------

    private async Task<ServiceResult<List<GeneratedCareerArtifact>>> GenerateWithOpenAiAsync(
        string brief, CancellationToken ct)
    {
        var body = new
        {
            model = OpenAiModel,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = brief }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new { name = "career_artifacts", strict = true, schema = OpenAiSchema() }
            },
            max_tokens = 4096
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.Value);

        var http = _httpFactory.CreateClient("OpenAI");
        var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return ServiceResult<List<GeneratedCareerArtifact>>.Fail(DescribeOpenAiError(response.StatusCode, text));

        using var doc = JsonDocument.Parse(text);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        if (message.TryGetProperty("refusal", out var refusal) &&
            refusal.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(refusal.GetString()))
            return ServiceResult<List<GeneratedCareerArtifact>>.Fail(
                "The model declined to generate for this repository. Try a different repo.");

        var content = message.TryGetProperty("content", out var c) ? c.GetString() : null;
        return MapArtifacts(content);
    }

    private static string DescribeOpenAiError(HttpStatusCode status, string body)
    {
        var detail = TryExtractMessage(body);
        return status switch
        {
            HttpStatusCode.Unauthorized => "OpenAI rejected the API key. Check it on the Settings page.",
            HttpStatusCode.TooManyRequests => "OpenAI rate limit or quota reached. Check your account's usage/billing, then try again.",
            _ => $"OpenAI returned an error ({(int)status}). {detail}".Trim()
        };
    }

    private static string TryExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "";
        }
        catch { /* body wasn't JSON */ }
        return "";
    }

    private static object OpenAiSchema() => new
    {
        type = "object",
        properties = new
        {
            artifacts = new
            {
                type = "array",
                description = "The generated career artifacts.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        artifactType = new { type = "string", @enum = GenerationOptions.AllTypes },
                        title = new { type = "string" },
                        content = new { type = "string" },
                        evidence = new { type = "array", items = new { type = "string" } }
                    },
                    required = new[] { "artifactType", "title", "content", "evidence" },
                    additionalProperties = false
                }
            }
        },
        required = new[] { "artifacts" },
        additionalProperties = false
    };

    // ---------------- Anthropic (Messages + structured outputs) ----------------

    private async Task<ServiceResult<List<GeneratedCareerArtifact>>> GenerateWithAnthropicAsync(
        string brief, CancellationToken ct)
    {
        var client = new AnthropicClient { ApiKey = _anthropic.Value };

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = AnthropicModel,
            MaxTokens = 4096,
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Medium,
                Format = new JsonOutputFormat { Schema = AnthropicSchema() }
            },
            System = SystemPrompt,
            Messages = [new() { Role = Role.User, Content = brief }]
        }, ct);

        if (response.StopReason == "refusal")
            return ServiceResult<List<GeneratedCareerArtifact>>.Fail(
                "The model declined to generate for this repository. Try a different repo.");

        var json = response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text;
        return MapArtifacts(json);
    }

    // ---------------- Shared ----------------

    private ServiceResult<List<GeneratedCareerArtifact>> MapArtifacts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ServiceResult<List<GeneratedCareerArtifact>>.Fail("The model returned an empty response. Please try again.");

        ArtifactsWrapper? parsed;
        try { parsed = JsonSerializer.Deserialize<ArtifactsWrapper>(json, JsonOpts); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse AI response JSON.");
            return ServiceResult<List<GeneratedCareerArtifact>>.Fail("The model's response couldn't be read. Please try again.");
        }

        var artifacts = (parsed?.Artifacts ?? new()).Select(a => new GeneratedCareerArtifact
        {
            ArtifactType = a.ArtifactType ?? "",
            Title = a.Title ?? "",
            Content = a.Content ?? "",
            Evidence = a.Evidence ?? new()
        }).ToList();

        return artifacts.Count == 0
            ? ServiceResult<List<GeneratedCareerArtifact>>.Fail("No artifacts were generated. Please try again.")
            : ServiceResult<List<GeneratedCareerArtifact>>.Ok(artifacts);
    }

    private const string SystemPrompt =
        """
        You help a developer describe a real GitHub project in credible, interview-ready language.

        You are given EVIDENCE extracted from the repository: languages, dependencies, file
        structure, recent commit messages, and README text. This is the only ground truth you have.

        Hard rules:
        - Make ONLY claims that the evidence supports. Never invent technologies, metrics, team
          sizes, user counts, or outcomes that aren't in the evidence.
        - Be specific and concrete. Name the actual technologies and implementation details from
          the evidence rather than vague phrases like "scalable full-stack platform".
        - Do not exaggerate. If the evidence is thin, write modest, honest claims.
        - Every artifact must cite the evidence that backs it: in the `evidence` array, list the
          exact labels from the provided evidence (a language name, a package name, a file path,
          a commit short-sha, or "README") that justify the claim.
        - Write for a junior developer who built this and wants to sound confident but truthful.
        - Produce exactly the artifacts requested below, using the exact `artifactType` values given,
          and set a short `title` for each.
        - Format each artifact's `content` with light Markdown for readability: use a "- " bullet list
          when there are multiple discrete points (gaps, tips, README suggestions), and separate
          paragraphs with a blank line. Keep each résumé bullet to a single line with no Markdown.
        - For prose artifacts (project summary, cover letter, LinkedIn about, case study, interview
          story), write 2-4 short paragraphs separated by a blank line — never one long slab.
        - Never indent lines with tabs or leading spaces (indentation renders as a code block). Start
          every line flush left.
        """;

    // Per-type production instructions.
    private static string TypeInstruction(string type) => type switch
    {
        "ResumeBullet" => "3 to 5 ResumeBullet artifacts (one strong resume line each; start with an action verb).",
        "InterviewStory" => "1 InterviewStory artifact in STAR form (Situation, Task, Action, Result), grounded in what the repo shows; if outcomes aren't in the evidence, frame the Result around what was built and learned, not fabricated impact.",
        "ProjectSummary" => "1 ProjectSummary artifact (2-4 sentences describing what the project is and does).",
        "CoverLetter" => "1 CoverLetter artifact: one concise cover-letter paragraph (4-6 sentences) the candidate could adapt.",
        "LinkedInAbout" => "1 LinkedInAbout artifact: a first-person LinkedIn 'About' blurb (3-5 sentences).",
        "TechnicalCaseStudy" => "1 TechnicalCaseStudy artifact: a longer write-up in 2-4 short paragraphs — the problem, the approach, key decisions/tradeoffs, and the outcome.",
        "ReadmeImprovements" => "1 ReadmeImprovements artifact: a bulleted list of specific, concrete ways to improve this repo's README (or state plainly that it's already strong).",
        "FullReadme" => "1 FullReadme artifact: a complete, ready-to-paste README.md in GitHub-flavored Markdown — a title, a one-line tagline, a short overview, a Tech Stack section (as a bullet list or table from the detected languages/dependencies), a Getting Started section with install/run steps inferred from the manifests, a Project Structure note, and a Features list. Use only what the evidence supports; where a real command isn't knowable, use a clearly-generic placeholder.",
        "LikelyQuestions" => "1 LikelyQuestions artifact: 5-7 questions an interviewer would realistically ask about THIS specific project (grounded in the code, architecture, and choices visible in the evidence), each as a '- ' bullet followed by a one-sentence italic hint on how to answer it well. Make them specific to this repo, not generic.",
        "JobFitGaps" => "1 JobFitGaps artifact: an honest, specific list of gaps between the target job and what this repo demonstrates — what's missing or weak for THIS role.",
        "HireabilityTips" => "1 HireabilityTips artifact: a short, prioritized list of concrete changes to the project that would make the candidate more competitive for THIS role.",
        "PortfolioNarrative" => "1 PortfolioNarrative artifact: 3-5 sentences describing the developer's overall body of work across these projects and the through-line in their skills.",
        _ => $"1 {type} artifact."
    };

    private static string ProductionSpec(GenerationOptions o)
    {
        var types = o.Types.Distinct().Where(t => GenerationOptions.AllTypes.Contains(t)).ToList();
        if (types.Count == 0) types = new() { "ResumeBullet", "ProjectSummary" };

        var sb = new StringBuilder();
        sb.AppendLine("Produce exactly these artifacts (use the exact artifactType value shown at the start of each line):");
        foreach (var t in types) sb.AppendLine($"- [{t}] {TypeInstruction(t)}");
        sb.AppendLine(o.Tone switch
        {
            "confident" => "Voice: confident and achievement-forward — still strictly truthful, no fabrication.",
            "honest" => "Voice: candid self-assessment — surface weaknesses and what's underdeveloped, not only strengths.",
            _ => "Voice: clear, professional, and honest."
        });
        if (o.AtsOptimize)
            sb.AppendLine("ATS MODE: optimize résumé bullets to pass applicant-tracking-system screeners. " +
                          "Front-load concrete, screenable keywords (specific technologies, frameworks, and skills from the evidence" +
                          (o.HasJob ? ", and mirror the exact terminology used in the target job description above" : "") +
                          "). Keep each bullet a single plain-text line with no special characters or Markdown.");
        return sb.ToString();
    }

    private static string BuildBrief(GitHubRepoAnalysisResult r, GenerationOptions options)
    {
        var sb = new StringBuilder();
        AppendEvidenceContext(sb, r, options);
        sb.AppendLine();
        sb.AppendLine(ProductionSpec(options));
        sb.AppendLine("Ground everything strictly in the evidence above; do not invent facts.");
        return sb.ToString();
    }

    // Plain-Markdown brief for the streaming path (structured JSON can't stream readably).
    private static string BuildStreamBrief(GitHubRepoAnalysisResult r, GenerationOptions options)
    {
        var sb = new StringBuilder();
        AppendEvidenceContext(sb, r, options);
        sb.AppendLine();
        var types = options.Types.Distinct().Where(t => GenerationOptions.AllTypes.Contains(t)).ToList();
        if (types.Count == 0) types = new() { "ResumeBullet", "ProjectSummary" };
        sb.AppendLine("Write the following as clean GitHub-flavored Markdown. Begin each item with a level-2 heading ('## <short title>'), then the content, then a line 'Evidence: <comma-separated labels drawn from the evidence above>'. Produce:");
        foreach (var t in types) sb.AppendLine($"- {TypeInstruction(t)}");
        sb.AppendLine(options.Tone switch
        {
            "confident" => "Voice: confident and achievement-forward — still strictly truthful.",
            "honest" => "Voice: candid self-assessment — surface weaknesses too, not only strengths.",
            _ => "Voice: clear, professional, and honest."
        });
        if (options.AtsOptimize)
            sb.AppendLine("ATS mode: front-load concrete, screenable keywords (specific technologies from the evidence" +
                          (options.HasJob ? ", mirroring the job description's terminology" : "") + ").");
        sb.AppendLine("Ground everything strictly in the evidence above; do not invent facts. Output only the Markdown, with no preamble.");
        return sb.ToString();
    }

    private static void AppendEvidenceContext(StringBuilder sb, GitHubRepoAnalysisResult r, GenerationOptions options)
    {
        sb.AppendLine($"Repository: {r.FullName}");
        if (!string.IsNullOrWhiteSpace(r.Description)) sb.AppendLine($"Description: {r.Description}");
        if (!string.IsNullOrWhiteSpace(r.PrimaryLanguage)) sb.AppendLine($"Primary language: {r.PrimaryLanguage}");

        if (r.Languages.Count > 0)
            sb.AppendLine("Languages: " + string.Join(", ",
                r.Languages.Select(l => $"{l.Name} ({l.Percentage:0.#}%)")));

        if (r.Packages.Count > 0)
            sb.AppendLine("Dependencies: " + string.Join(", ",
                r.Packages.Select(p => p.Name).Distinct().Take(40)));

        if (r.TestFileCount > 0)
            sb.AppendLine($"Tests: {r.TestFileCount} test file(s)" +
                (string.IsNullOrEmpty(r.TestFramework) ? "" : $" using {r.TestFramework}"));

        if (r.DependencyNotes.Count > 0)
            sb.AppendLine("Dependency notes: " + string.Join(" ", r.DependencyNotes));

        var dirs = r.AllPaths
            .Select(p => p.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();
        if (dirs.Count > 0)
            sb.AppendLine("Top-level entries: " + string.Join(", ", dirs));

        if (r.ResumeAngles.Count > 0)
        {
            sb.AppendLine("\nRule-based signals already detected (build on these, don't contradict them):");
            foreach (var a in r.ResumeAngles) sb.AppendLine($"- {a.Text}");
        }

        if (r.RecentCommits.Count > 0)
        {
            sb.AppendLine("\nRecent commits:");
            foreach (var c in r.RecentCommits.Take(10)) sb.AppendLine($"- {c.ShortSha}: {c.FirstLine}");
        }

        // Phase 8: real source code from the highest-signal files. This is what lets claims be
        // specific ("JWT rotation in auth/tokens.py") instead of inferred from file names.
        if (r.KeyFiles.Count > 0)
        {
            sb.AppendLine("\nKEY SOURCE FILES (excerpts of the actual code — cite file paths as evidence):");
            var budget = 7000;
            foreach (var kf in r.KeyFiles)
            {
                if (budget <= 0) break;
                var snip = kf.Snippet;
                if (snip.Length > budget) snip = snip[..budget] + " …";
                budget -= snip.Length;
                sb.AppendLine($"\n--- {kf.Path} ({kf.Reason}) ---");
                sb.AppendLine(snip);
            }
        }

        if (r.HasReadme)
        {
            // Include more of the README when the user asked to improve it.
            var cap = options.Types.Contains("ReadmeImprovements") ? 6000 : 1800;
            var readme = r.ReadmeContent!.Trim();
            if (readme.Length > cap) readme = readme[..cap] + " …";
            sb.AppendLine("\nREADME:");
            sb.AppendLine(readme);
        }
        else if (options.Types.Contains("ReadmeImprovements"))
        {
            sb.AppendLine("\nNote: this repository has NO README. The ReadmeImprovements artifact should explain what a good README for it would contain.");
        }

        if (options.HasJob)
        {
            var jd = options.JobDescription!.Trim();
            if (jd.Length > 4000) jd = jd[..4000] + " …";
            sb.AppendLine("\nTARGET JOB DESCRIPTION (tailor ResumeBullet and InterviewStory content toward the evidence most relevant to this role):");
            sb.AppendLine(jd);
        }

    }

    /// <summary>Anthropic's structured-output schema (a dictionary of top-level JSON Schema keywords).</summary>
    private static Dictionary<string, JsonElement> AnthropicSchema()
    {
        var itemSchema = new
        {
            type = "object",
            properties = new
            {
                artifactType = new { type = "string", @enum = GenerationOptions.AllTypes },
                title = new { type = "string" },
                content = new { type = "string" },
                evidence = new { type = "array", items = new { type = "string" } }
            },
            required = new[] { "artifactType", "title", "content", "evidence" },
            additionalProperties = false
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                artifacts = new { type = "array", items = itemSchema }
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "artifacts" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
        };
    }

    // Shapes for parsing the structured JSON response (same for both providers).
    private sealed record ArtifactsWrapper(List<ArtifactDto>? Artifacts);
    private sealed record ArtifactDto(string? ArtifactType, string? Title, string? Content, List<string>? Evidence);
}
