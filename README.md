# RepoSummary

Turn a public GitHub repository into interview-ready evidence.

RepoSummary is a developer career-assistance web app. You paste a public GitHub
repository, and it extracts the **real project evidence** — technologies, structure,
recent commits, and README — then suggests resume angles and interview talking points
that are each tied back to that evidence. It is deliberately *not* a generic AI resume
generator: every strong claim points at something actually found in the repo.

## What it does today

- **Repository input** — accepts `owner/repo`, `github.com/owner/repo`, or a full
  `https://github.com/owner/repo` URL, with clear validation messages.
- **GitHub fetching** — pulls repo metadata, README, recent commits, language
  breakdown, the **full recursive file tree** (one API call), and the dependencies
  declared in manifest files (`.csproj`, `package.json`, `requirements.txt`, `go.mod`,
  `Cargo.toml`, `pom.xml`).
- **Deep, evidence-first analysis** — rules scan the whole tree and parsed dependencies,
  so nested signals are caught (EF Core via a `DbContext` or `Microsoft.EntityFrameworkCore`
  package, Hangfire, Docker, CI/CD under `.github/workflows`, tests, SPA frameworks, auth,
  logging, caching, OpenAPI). Every resume angle names the concrete evidence behind it.
- **AI-generated career material** — with an Anthropic API key configured, a *Generate*
  button produces evidence-grounded **resume bullets, a STAR interview story, and a project
  summary** using Claude (Opus 4.8). The model is given only the extracted evidence and is
  forbidden from making unsupported claims; each artifact cites the evidence that backs it.
- **Structured analysis page**: Repository Overview · Detected Technologies · Detected
  Dependencies · AI Career Material · Resume Angles · Interview Talking Points · Recent
  Commits · File Structure · README.
- **Graceful error handling** for not-found repos, rate limits, network failures, missing
  API key, and model refusals.

Authentication, a database, and private-repo support are still intentionally deferred
(see `CLAUDE.md`).

## Tech stack

- ASP.NET Core **Razor Pages** on **.NET 10** (`net10.0`)
- `IHttpClientFactory` typed client for the GitHub API
- Official **Anthropic C# SDK** (`Anthropic`) for grounded AI generation with structured outputs
- Clean service layer — GitHub calls live in `GitHubRepositoryService`, AI generation in
  `AiCareerArtifactGenerator`; pages never call an external API directly

## Project layout

```
RepoSummary/
  Program.cs                       # startup + DI (Razor Pages, typed GitHub client)
  Pages/
    Index.cshtml(.cs)              # repo input + validation
    Analysis.cshtml(.cs)          # structured analysis results
    About.cshtml(.cs)             # product explanation
    Error.cshtml(.cs)
    Shared/_Layout.cshtml
  Services/
    GitHubUrlParser.cs             # parses accepted input forms -> owner/repo
    IGitHubRepositoryService.cs
    GitHubRepositoryService.cs     # all GitHub API calls + error handling
    GitHubApiDtos.cs               # internal JSON DTOs
    RepoAnalyzer.cs                # rule-based evidence + resume/interview angles
  Models/
    GitHubRepoAnalysisResult.cs    # + CareerAngle
    GitHubCommitSummary.cs
    GitHubLanguageSummary.cs
    GitHubFileTreeItem.cs
    EvidenceItem.cs
    ServiceResult.cs               # success/failure wrapper
  wwwroot/css/site.css
```

## Running it

```bash
cd RepoSummary
dotnet run
```

Then open the URL shown in the console (e.g. `http://localhost:5080`).

## Configuration

**Zero setup to start:** clone, `dotnet run`, and analyze any public repo. The two keys
below are optional and are entered **in the app on the Settings page** — no environment
variables or config edits required. Each is stored **encrypted on your machine only**
(ASP.NET Data Protection), applied per request (no restart), and is **gitignored so it can
never be committed**. This is the bring-your-own-key model: every person who clones the repo
adds their own keys once, and no key ever lives in the repository.

### AI API key (enables AI generation)

The *Generate* button works with **either** an OpenAI (ChatGPT) key **or** an Anthropic
(Claude) key — add whichever you have on the **Settings** page. Without one, the app still runs
and shows all extracted evidence; it just points you to Settings. Each generation is a single
API call (roughly a few cents).

- OpenAI key: [platform.openai.com/api-keys](https://platform.openai.com/api-keys) (starts with `sk-`)
- Anthropic key: [console.anthropic.com](https://console.anthropic.com/settings/keys) (starts with `sk-ant-`)

If both keys are set, Settings lets you choose the preferred provider. You can pre-seed keys via
`OPENAI_API_KEY` / `ANTHROPIC_API_KEY` (or `OpenAI:ApiKey` / `Anthropic:ApiKey` in user-secrets);
the Settings value takes precedence once set.

### GitHub token (raises the rate limit — recommended)

Unauthenticated GitHub API requests are limited to **60/hour per IP**, and deeper analysis
makes up to ~15 calls per repo (metadata, README, commits, languages, tree, manifest reads,
workflow files, releases). Add a
[GitHub personal access token](https://github.com/settings/tokens) (no scopes needed for
public repos) on the **Settings** page to raise it to 5,000/hour. (You can also pre-seed it
via `GitHub:Token` in user-secrets.)

## Known limitations

- Nothing is persisted — each analysis (and each *Generate*) re-fetches from GitHub live.
- Manifest parsing is bounded to ~6 files per repo to protect the rate limit, so a
  dependency in a rarely-used project file may be missed.
- Public repositories only.
- AI generation quality depends on how much real evidence the repo exposes (a repo with no
  README or dependencies yields thinner material — by design, it won't invent claims).

## Next steps

See `CLAUDE.md` for the full roadmap (evidence-first architecture, then AI generation
grounded in that evidence).
