using System.Net.Http.Headers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using RepoSummary.Data;
using RepoSummary.Models;
using RepoSummary.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Pin the Data Protection key ring to a fixed folder + application name so the keys that
// encrypt saved API tokens survive rebuilds, output-path changes, and restarts. Without this
// the discriminator can shift between runs and previously-saved secrets fail to decrypt
// (they'd silently read as "not set"). Keys stay machine-scoped and are gitignored.
builder.Services.AddDataProtection()
    .SetApplicationName("RepoSummary")
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, ".dpkeys")));

// Local SQLite database for saved analyses + interview stories. The file is
// created automatically on first run (no migration step for people who clone this).
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(
    builder.Configuration.GetConnectionString("Db") ?? "Data Source=reposummary.db"));
builder.Services.AddScoped<IAnalysisStore, AnalysisStore>();

// Short-lived cache so clicking "Generate" reuses the analysis the user just
// viewed instead of re-hitting the GitHub API.
builder.Services.AddMemoryCache();

// Structured request logging (path, status, duration) for easier debugging.
builder.Services.AddHttpLogging(o =>
    o.LoggingFields = HttpLoggingFields.RequestPath
                      | HttpLoggingFields.ResponseStatusCode
                      | HttpLoggingFields.Duration);

// URL parsing helper — pure logic, no dependencies.
builder.Services.AddSingleton<GitHubUrlParser>();

// Secrets entered on the Settings page (encrypted at rest, never committed) +
// AI provider preference + rate-limit tracking.
builder.Services.AddSingleton<GitHubTokenStore>();
builder.Services.AddSingleton<OpenAiKeyStore>();
builder.Services.AddSingleton<AnthropicKeyStore>();
builder.Services.AddSingleton<AiProviderStore>();
builder.Services.AddSingleton<GitHubRateLimitStore>();
builder.Services.AddTransient<GitHubAuthHandler>();

// AI generation layer. Works with OpenAI (ChatGPT) or Anthropic (Claude) — uses
// whichever key is set; degrades gracefully (IsConfigured == false) when neither is.
builder.Services.AddHttpClient("OpenAI", c =>
{
    c.BaseAddress = new Uri("https://api.openai.com/");
    c.Timeout = TimeSpan.FromSeconds(90);   // ceiling so a hung call fails instead of spinning forever
});
builder.Services.AddScoped<ICareerArtifactGenerator, AiCareerArtifactGenerator>();

// Typed HttpClient for the GitHub API. All GitHub calls live in the service.
builder.Services.AddHttpClient<IGitHubRepositoryService, GitHubRepositoryService>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("RepoSummary-App");
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
})
// The token is applied per-request so a token saved on the Settings page takes
// effect immediately (no restart), and retries handle transient failures.
.AddHttpMessageHandler<GitHubAuthHandler>()
.AddStandardResilienceHandler();

var app = builder.Build();

// Apply EF Core migrations on startup — creates the SQLite database on first run and
// evolves the schema cleanly on later changes (replaces the old EnsureCreated()).
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpLogging();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// Live streaming generation (OpenAI). Same-origin fetch from the analysis page;
// antiforgery is disabled here because it only spends the user's own configured key.
app.MapPost("/generate/stream", async (
    HttpContext ctx, IAnalysisStore store, ICareerArtifactGenerator generator) =>
{
    var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
    var owner = form["owner"].ToString();
    var repo = form["repo"].ToString();

    var snapshot = await store.GetSavedAsync(owner, repo, ctx.RequestAborted);
    if (snapshot is null)
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsync("No saved analysis found — run the analysis first.", ctx.RequestAborted);
        return;
    }

    var types = form["GenTypes"].Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList();
    if (types.Count == 0) types = new() { "ResumeBullet", "ProjectSummary" };
    var job = form["GenJob"].ToString();
    if (!string.IsNullOrWhiteSpace(job))
    {
        if (form["GenCategory"].ToString() == "interview") { types.Add("JobFitGaps"); types.Add("RoleFitScore"); }
        else types.Add("HireabilityTips");
    }

    var options = new GenerationOptions
    {
        Types = types,
        Tone = string.IsNullOrWhiteSpace(form["GenTone"]) ? "balanced" : form["GenTone"].ToString(),
        JobDescription = string.IsNullOrWhiteSpace(job) ? null : job,
        AtsOptimize = form["GenAts"].Any(v => v == "true")
    };

    ctx.Response.ContentType = "text/plain; charset=utf-8";
    await foreach (var chunk in generator.StreamAsync(snapshot.Result, options, ctx.RequestAborted))
    {
        await ctx.Response.WriteAsync(chunk, ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }
}).DisableAntiforgery();

// Ask a free-form question about a repo, streamed. Grounded in the saved analysis's evidence
// + key source files. Same-origin fetch from the analysis page; only spends the user's own key.
app.MapPost("/ask/stream", async (
    HttpContext ctx, IAnalysisStore store, ICareerArtifactGenerator generator) =>
{
    var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
    var owner = form["owner"].ToString();
    var repo = form["repo"].ToString();
    var question = form["question"].ToString();

    var snapshot = await store.GetSavedAsync(owner, repo, ctx.RequestAborted);
    if (snapshot is null)
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsync("No saved analysis found — run the analysis first.", ctx.RequestAborted);
        return;
    }

    ctx.Response.ContentType = "text/plain; charset=utf-8";
    await foreach (var chunk in generator.AnswerAsync(snapshot.Result, question, ctx.RequestAborted))
    {
        await ctx.Response.WriteAsync(chunk, ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }
}).DisableAntiforgery();

// In development, pop open a Chrome window pointing at the app once it's listening.
if (app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault(u => u.StartsWith("https"))
                  ?? app.Urls.FirstOrDefault();
        if (!string.IsNullOrEmpty(url))
            BrowserLauncher.OpenChrome(url, app.Logger);
    });
}

app.Run();

/// <summary>Best-effort helper to open the app in Chrome during development.</summary>
static class BrowserLauncher
{
    public static void OpenChrome(string url, ILogger logger)
    {
        try
        {
            // "start chrome" resolves Chrome via the Windows App Paths registry.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c start chrome \"{url}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not launch Chrome; falling back to the default browser.");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception fallbackEx)
            {
                logger.LogWarning(fallbackEx, "Could not launch a browser automatically. Open {Url} manually.", url);
            }
        }
    }
}
