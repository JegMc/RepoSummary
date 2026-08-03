# RepoSummary

**Turn a GitHub repository into evidence-backed résumé bullets, interview stories, and a maturity report — grounded in the actual code, not generic AI fluff.**

RepoSummary is an evidence-first alternative to generic AI résumé tools. Point it at a public
GitHub repository (or a whole profile) and it extracts real project evidence — README, commits,
dependencies, the file tree, and the **actual source of the key files** — then turns that into a
maturity score, architecture and data-model diagrams, "ask this repo" code Q&A, and career
material that **cites the evidence behind every claim**. If it can't point to something in the
repo, it won't say it.

<!-- Add a screenshot when you have one, e.g.:  ![Analysis page](docs/analysis.png)  -->

## Features

**Analyze**
- Repo overview, detected technologies (with versions), dependencies, project signals, recent
  commits, an interactive file explorer, and a rendered README.
- **Maturity score** (A–F / 100) with a signal checklist, a **"Raise your grade"** tab of
  step-by-step how-tos, and a **grade-history** sparkline over time.
- Reads the highest-signal source files and builds an **architecture diagram** and an **ER
  data-model diagram** (Mermaid), plus **"Ask this repo"** — conversational Q&A grounded in that code.

**Generate** (optional — bring your own AI key)
- Résumé bullets, STAR interview stories, project summaries, cover letters, a LinkedIn "About",
  technical case studies, README drafts, likely-interview-questions, a role-fit score, and JD gap
  analysis — each **citing its evidence**.
- An optional **fact-check** pass that flags any statement the evidence doesn't support, plus an
  **ATS** mode for keyword-dense résumé output.

**Library & share**
- History, a cross-repo skill inventory, **compare two repos**, **portfolio synthesis**, a printable
  interview **prep pack**, a shareable **health card**, PDF / Markdown / JSON **exports**, and an
  embeddable grade **badge**.
- A **profile view** (rank a user's repos, analyze the strongest) and a **browser extension** that
  adds an "Analyze in RepoSummary" button to GitHub.

## Tech stack

- **ASP.NET Core Razor Pages** on **.NET 10**, C#.
- **EF Core + SQLite** (schema via migrations, created/updated on startup).
- Typed `HttpClient` for the GitHub REST API, with a **GraphQL fast-path** when a token is set.
- **OpenAI (ChatGPT)** or **Anthropic (Claude)** for AI generation — your key, your choice.
- Inline SVG + CSS for charts; **Mermaid** (bundled locally) for diagrams.
- **71 unit + integration tests** (`dotnet test`).

## Getting started

```bash
git clone git@github.com:JegMc/RepoSummary.git
cd RepoSummary/RepoSummary
dotnet run
```

Then open the URL shown in the console (in Development, Chrome opens automatically). Analyze any
public repo immediately — no keys required. One-time trusted HTTPS cert (optional):
`dotnet dev-certs https --trust`.

## Configuration

Everything below is **optional** and entered **in the app on the Settings page** — no config edits.

- **AI key** (enables generation): add an **OpenAI** (`sk-…`) *or* **Anthropic** (`sk-ant-…`) key.
  Without one, all evidence, diagrams, and rule-based angles still work.
- **GitHub token** (recommended): unauthenticated requests are capped at **60/hour**; a
  [fine-grained token](https://github.com/settings/tokens?type=beta) (no scopes needed for public
  repos) raises that to **5,000/hour** and unlocks the faster GraphQL path.

## Security & secrets

Keys **never touch the repository**. You enter them at runtime on the Settings page; they're stored
**encrypted** (ASP.NET Data Protection) in per-machine files (`.githubtoken`, `.openaikey`,
`.anthropickey`) — all **gitignored**, along with the encryption key ring (`.dpkeys/`) and the local
database. There is no code path that writes a key into a tracked file, and `appsettings.json` ships
empty. **Never put a real token in `appsettings.json`** (it's committed) — use the Settings page or
.NET user-secrets. Before pushing, you can verify with a scanner like
[`gitleaks`](https://github.com/gitleaks/gitleaks): `gitleaks detect --source .`

## Tests

```bash
dotnet test
```

## Project structure

```
RepoSummary/            # the ASP.NET Core app
  Pages/                # Razor Pages (Index, Analysis, Profile, Compare, Portfolio, Card, …)
  Services/             # GitHub client, analyzers, AI generator, exports — all external I/O here
  Models/               # analysis result + generation models
  Data/                 # EF Core DbContext + migrations
  wwwroot/              # css, js, bundled Mermaid
RepoSummary.Tests/      # xUnit unit + integration tests
browser-extension/      # MV3 "Analyze in RepoSummary" button for GitHub
ROADMAP.md              # phased roadmap (Phases 1–9 complete)
STATUS.md               # current-state snapshot
```

## Roadmap

See [`ROADMAP.md`](./ROADMAP.md) — Phases 1–9 are complete. Natural next steps are the remaining
Phase 7 items (GitHub OAuth, hosting, user accounts).

## License

Released under the [MIT License](./LICENSE).
