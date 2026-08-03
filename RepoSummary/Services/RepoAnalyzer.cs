using System.Text.RegularExpressions;
using RepoSummary.Models;

namespace RepoSummary.Services;

/// <summary>
/// Turns raw repo facts into evidence, resume angles, and interview talking
/// points using simple, transparent rules. No AI here — every angle is tied
/// to the concrete evidence that produced it (the core product rule).
///
/// Detection scans the full recursive tree and parsed dependencies, so nested
/// signals (a DbContext in a subfolder, a workflow under .github/workflows,
/// Hangfire in a .csproj) are caught — not just the top level.
/// </summary>
public static class RepoAnalyzer
{
    public static void Populate(GitHubRepoAnalysisResult result)
    {
        // Prefer the full recursive tree; fall back to the top level if it wasn't available.
        var paths = result.AllPaths.Count > 0
            ? result.AllPaths
            : result.TopLevelItems.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();

        // --- Search helpers over paths and packages ---
        bool HasFile(string name) =>
            paths.Any(p => p.Split('/').Last().Equals(name, StringComparison.OrdinalIgnoreCase));
        bool HasFileExt(string ext) =>
            paths.Any(p => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        bool HasDir(string dir) =>
            paths.Any(p => p.Split('/').Any(seg => seg.Equals(dir, StringComparison.OrdinalIgnoreCase)));
        bool PathContains(string sub) =>
            paths.Any(p => p.Contains(sub, StringComparison.OrdinalIgnoreCase));
        bool FileNameContains(string sub) =>
            paths.Any(p => p.Split('/').Last().Contains(sub, StringComparison.OrdinalIgnoreCase));

        GitHubPackageSummary? Package(params string[] needles) =>
            result.Packages.FirstOrDefault(pkg =>
                needles.Any(n => pkg.Name.Contains(n, StringComparison.OrdinalIgnoreCase)));

        EvidenceItem PathEvidence(string sub)
        {
            var match = paths.First(p => p.Contains(sub, StringComparison.OrdinalIgnoreCase));
            return new EvidenceItem { Type = "FilePath", Label = match, Detail = "Found in the repository tree.", SourcePath = match };
        }
        EvidenceItem PkgEvidence(GitHubPackageSummary pkg) => new()
        {
            Type = "Package",
            Label = pkg.Name,
            Detail = $"Declared in {pkg.Source}.",
            SourcePath = pkg.SourcePath
        };

        // --- Evidence: languages, packages, README ---
        var evidence = new List<EvidenceItem>();

        foreach (var lang in result.Languages)
            evidence.Add(new EvidenceItem
            {
                Type = "Technology",
                Label = lang.Name,
                Detail = $"{lang.Percentage:0.#}% of detected code ({lang.Bytes:N0} bytes)."
            });

        foreach (var pkg in result.Packages)
            evidence.Add(PkgEvidence(pkg));

        if (result.HasReadme)
            evidence.Add(new EvidenceItem
            {
                Type = "README",
                Label = "README present",
                Detail = $"{result.ReadmeContent!.Length:N0} characters of project documentation."
            });

        result.Evidence = evidence;

        // --- Rule-based resume angles ---
        var angles = new List<CareerAngle>();

        void AddAngle(string text, params EvidenceItem?[] ev)
        {
            var supporting = ev.Where(e => e is not null).Cast<EvidenceItem>().ToList();
            angles.Add(new CareerAngle { Text = text, SupportingEvidence = supporting });
        }

        // ASP.NET Core / .NET web application.
        if (HasFileExt(".csproj") && (HasDir("Controllers") || HasDir("Pages") || HasDir("Views")
                                      || HasFile("Program.cs") || HasFile("appsettings.json")))
        {
            AddAngle(
                "Built a .NET / ASP.NET Core web application (project files, entry point, and MVC/Razor structure present).",
                HasDir("Controllers") ? PathEvidence("Controllers") : null,
                HasDir("Pages") ? PathEvidence("Pages") : null,
                HasFile("Program.cs") ? PathEvidence("Program.cs") : null,
                HasFile("appsettings.json") ? PathEvidence("appsettings.json") : null);
        }

        // EF Core-backed persistence.
        var efPkg = Package("EntityFrameworkCore");
        if (efPkg is not null || HasDir("Migrations") || FileNameContains("DbContext"))
        {
            AddAngle(
                "Implemented an Entity Framework Core persistence layer with a data model and migrations.",
                efPkg is not null ? PkgEvidence(efPkg) : null,
                HasDir("Migrations") ? PathEvidence("Migrations") : null,
                FileNameContains("DbContext") ? PathEvidence("DbContext") : null);
        }

        // Background job processing.
        var hangfire = Package("Hangfire", "Quartz");
        if (hangfire is not null || PathContains("Hangfire"))
            AddAngle(
                "Added background job processing and scheduling to offload work from the request path.",
                hangfire is not null ? PkgEvidence(hangfire) : PathEvidence("Hangfire"));

        // Front-end framework / SPA.
        var spa = Package("react", "vue", "@angular", "next", "svelte");
        if (spa is not null)
            AddAngle(
                $"Built an interactive front end with a modern JavaScript framework ({spa.Name}).",
                PkgEvidence(spa));
        else if (HasFile("package.json"))
            AddAngle(
                "Worked across the stack with a JavaScript/Node front-end toolchain (package.json present).",
                PathEvidence("package.json"));

        // Containerization.
        if (HasFile("Dockerfile") || FileNameContains("docker-compose") || HasFile("compose.yaml"))
            AddAngle(
                "Containerized the application for reproducible builds and deployment (Docker configuration present).",
                HasFile("Dockerfile") ? PathEvidence("Dockerfile") : PathEvidence("compose"));

        // CI/CD.
        if (PathContains(".github/workflows") || HasFile(".gitlab-ci.yml") || HasFile("azure-pipelines.yml"))
            AddAngle(
                "Set up automated CI/CD pipelines to build and test the project on every change.",
                PathContains(".github/workflows") ? PathEvidence(".github/workflows")
                    : HasFile(".gitlab-ci.yml") ? PathEvidence(".gitlab-ci.yml") : PathEvidence("azure-pipelines.yml"));

        // Automated tests.
        if (HasDir("tests") || HasDir("test") || FileNameContains(".Tests")
            || FileNameContains(".test.") || FileNameContains(".spec.") || Package("xunit", "nunit", "jest", "pytest") is not null)
        {
            var testPkg = Package("xunit", "nunit", "jest", "pytest");
            AddAngle(
                "Wrote automated tests to protect application behavior against regressions.",
                testPkg is not null ? PkgEvidence(testPkg)
                    : FileNameContains(".Tests") ? PathEvidence(".Tests")
                    : HasDir("tests") ? PathEvidence("tests") : PathEvidence("test"));
        }

        // Authentication / security.
        var authPkg = Package("Authentication", "IdentityModel", "Identity", "jwt", "passport");
        if (authPkg is not null)
            AddAngle(
                "Implemented authentication / authorization to secure the application.",
                PkgEvidence(authPkg));

        // Structured logging.
        var logPkg = Package("Serilog", "NLog", "winston");
        if (logPkg is not null)
            AddAngle(
                $"Added structured logging for observability ({logPkg.Name}).",
                PkgEvidence(logPkg));

        // Caching / distributed data.
        var cachePkg = Package("Redis", "StackExchange.Redis", "MemoryCache");
        if (cachePkg is not null)
            AddAngle(
                "Used caching to improve performance and reduce load on backing services.",
                PkgEvidence(cachePkg));

        // API documentation.
        var apiPkg = Package("Swashbuckle", "Swagger", "NSwag");
        if (apiPkg is not null)
            AddAngle(
                "Documented the HTTP API with OpenAPI/Swagger for discoverability.",
                PkgEvidence(apiPkg));

        // --- Phase 2: architecture, database, API surface, security, releases ---

        // Reusable signals (also used by the maturity score and coaching below).
        var hasTests = HasDir("tests") || HasDir("test") || FileNameContains(".Tests")
            || FileNameContains(".test.") || FileNameContains(".spec.")
            || Package("xunit", "nunit", "jest", "pytest") is not null;
        var hasCi = result.Workflows.Count > 0 || PathContains(".github/workflows")
            || HasFile(".gitlab-ci.yml") || HasFile("azure-pipelines.yml");

        // Architecture pattern (shown in the Project signals card). Gated on an actual
        // .NET project so a JS repo with an examples/mvc/controllers folder isn't mislabeled.
        var isDotNet = HasFileExt(".csproj");
        result.ArchitecturePattern =
            (isDotNet && HasDir("Controllers") && HasDir("Views")) ? "ASP.NET Core MVC"
            : (isDotNet && HasDir("Pages")) ? "ASP.NET Core Razor Pages"
            : (isDotNet && HasDir("Controllers")) ? "ASP.NET Core Web API"
            : (isDotNet && HasDir("Domain") && (HasDir("Application") || HasDir("Infrastructure"))) ? "Clean / layered (Domain · Application · Infrastructure)"
            : null;
        if (result.ArchitecturePattern is not null && result.ArchitecturePattern.StartsWith("Clean"))
            AddAngle("Organized the codebase with a clean, layered architecture (separated domain, application, and infrastructure concerns).",
                HasDir("Domain") ? PathEvidence("Domain") : null);

        // Specific database, not just "a database".
        var dbPkg = Package("Npgsql", "Postgres", "SqlServer", "Microsoft.Data.SqlClient",
                            "Sqlite", "MySql", "Pomelo", "MongoDB");
        result.DatabaseTech = dbPkg is null ? null
            : dbPkg.Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) || dbPkg.Name.Contains("Postgres", StringComparison.OrdinalIgnoreCase) ? "PostgreSQL"
            : dbPkg.Name.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) || dbPkg.Name.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) ? "SQL Server"
            : dbPkg.Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "SQLite"
            : dbPkg.Name.Contains("MySql", StringComparison.OrdinalIgnoreCase) || dbPkg.Name.Contains("Pomelo", StringComparison.OrdinalIgnoreCase) ? "MySQL"
            : dbPkg.Name.Contains("MongoDB", StringComparison.OrdinalIgnoreCase) ? "MongoDB"
            : null;
        if (result.DatabaseTech is not null)
            AddAngle($"Designed and queried a {result.DatabaseTech} database.", PkgEvidence(dbPkg!));

        // API surface.
        result.ControllerCount = paths.Count(p => p.Split('/').Last().EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase));
        result.HasOpenApi = Package("Swashbuckle", "Swagger", "NSwag") is not null
            || PathContains("swagger.json") || PathContains("openapi.json")
            || PathContains("openapi.yaml") || PathContains("openapi.yml");

        // Security tooling.
        var sec = new List<string>();
        if (PathContains(".github/dependabot.yml") || PathContains(".github/dependabot.yaml")) sec.Add("Dependabot");
        if (result.Workflows.Any(w => w.Name.Contains("codeql", StringComparison.OrdinalIgnoreCase) || w.Does.Contains("code scanning")) || PathContains("codeql"))
            sec.Add("CodeQL");
        if (HasFile("SECURITY.md")) sec.Add("Security policy");
        result.SecurityTools = sec;
        if (sec.Count > 0)
            AddAngle($"Applied secure-development practices ({string.Join(", ", sec)}).",
                new EvidenceItem { Type = "Config", Label = string.Join(", ", sec), Detail = "Security tooling detected in the repo." });

        // Releases.
        if (result.ReleaseCount is > 0)
            AddAngle($"Shipped {(result.ReleaseCount >= 10 ? "10+" : result.ReleaseCount.ToString())} tagged release(s) — evidence of finishing and versioning work, not just experimenting.",
                new EvidenceItem { Type = "Release", Label = result.LatestReleaseName ?? "latest release", Detail = "Published GitHub release." });

        result.ResumeAngles = angles;

        // --- Interview talking points ---
        var talking = new List<CareerAngle>();

        if (result.RecentCommits.Count > 0)
            talking.Add(new CareerAngle
            {
                Text = "Walk through your recent commits: what problem was each change solving, " +
                       "and what tradeoffs did you weigh?",
                SupportingEvidence = result.RecentCommits.Take(5).Select(c => new EvidenceItem
                {
                    Type = "Commit",
                    Label = c.ShortSha,
                    Detail = c.FirstLine,
                    SourceUrl = c.HtmlUrl
                }).ToList()
            });

        if (result.HasReadme)
            talking.Add(new CareerAngle
            {
                Text = "Give the 60-second project pitch from your README: the problem, your approach, and the outcome.",
                SupportingEvidence = new List<EvidenceItem>
                {
                    new() { Type = "README", Label = "README", Detail = "Use it as the backbone of your pitch." }
                }
            });

        if (result.Packages.Count > 0)
            talking.Add(new CareerAngle
            {
                Text = "Justify a key dependency choice: why this library over the alternatives, and what did it cost you?",
                SupportingEvidence = result.Packages.Take(5).Select(PkgEvidence).ToList()
            });

        if (result.Languages.Count > 1)
            talking.Add(new CareerAngle
            {
                Text = $"Explain why you chose this technology mix ({string.Join(", ", result.Languages.Take(4).Select(l => l.Name))}) " +
                       "and what each part is responsible for.",
                SupportingEvidence = result.Languages.Take(4).Select(l => new EvidenceItem
                {
                    Type = "Technology",
                    Label = l.Name,
                    Detail = $"{l.Percentage:0.#}% of detected code."
                }).ToList()
            });

        result.InterviewTalkingPoints = talking;

        // --- Commit insights ---
        if (result.RecentCommits.Count > 0)
        {
            var conventional = result.RecentCommits.Count(c =>
                Regex.IsMatch(c.FirstLine ?? "", "^(feat|fix|chore|docs|refactor|test|build|ci|perf|style|revert)(\\(.+\\))?!?:", RegexOptions.IgnoreCase));
            if (conventional >= Math.Max(2, result.RecentCommits.Count / 2))
                result.CommitInsights.Add("Uses Conventional Commits — structured, machine-readable commit messages.");
        }
        if (result.CreatedAt is not null && result.UpdatedAt is not null)
            result.CommitInsights.Add($"Worked on from {result.CreatedAt:MMM yyyy} to {result.UpdatedAt:MMM yyyy}.");

        // --- Project maturity score ---
        var hasDocs = HasDir("docs") || paths.Count(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) >= 2;
        var signals = new (string Label, bool Present, int Weight)[]
        {
            ("README", result.HasReadme, 15),
            ("Description", !string.IsNullOrWhiteSpace(result.Description), 5),
            ("Automated tests", hasTests, 20),
            ("CI / CD", hasCi, 20),
            ("License", !string.IsNullOrWhiteSpace(result.License), 10),
            ("Tagged releases", result.ReleaseCount is > 0, 10),
            ("Changelog", result.HasChangelog, 5),
            ("Security tooling", result.SecurityTools.Count > 0, 10),
            ("Docs", hasDocs, 5),
        };
        var score = signals.Where(s => s.Present).Sum(s => s.Weight);
        result.Maturity = new RepoMaturity
        {
            Score = score,
            Grade = score >= 85 ? "A" : score >= 70 ? "B" : score >= 55 ? "C" : score >= 40 ? "D" : "F",
            Signals = signals.Select(s => new MaturitySignal { Label = s.Label, Present = s.Present }).ToList()
        };

        // --- Thin-repo coaching: concrete, plain-language ways to strengthen the repo ---
        var tips = new List<string>();
        if (!result.HasReadme)
            tips.Add("Add a README that says what the project does, how to run it, and why you built it — it's the first thing anyone (including an interviewer) reads.");
        if (string.IsNullOrWhiteSpace(result.Description))
            tips.Add("Add a short description to the GitHub repo so its purpose is clear at a glance.");
        if (!hasTests)
            tips.Add("Add a few automated tests — even a handful signals that you care about correctness.");
        if (!hasCi)
            tips.Add("Add a CI workflow (like GitHub Actions) so the project builds and tests itself on every push.");
        if (string.IsNullOrWhiteSpace(result.License))
            tips.Add("Add a LICENSE file so others know how they're allowed to use your code.");
        result.Suggestions = tips;
    }
}
