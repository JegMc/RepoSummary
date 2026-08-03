using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Tests;

public class GitHubServiceIntegrationTests
{
    private sealed class NoToken : IGitHubTokenSource { public bool HasValue => false; }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> r) => _responder = r;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_responder(request));
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage File(string text) =>
        Json($"{{\"content\":\"{Convert.ToBase64String(Encoding.UTF8.GetBytes(text))}\",\"encoding\":\"base64\"}}");

    private static GitHubRepositoryService Build(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var http = new HttpClient(new FakeHandler(responder)) { BaseAddress = new Uri("https://api.github.com/") };
        return new GitHubRepositoryService(
            http, NullLogger<GitHubRepositoryService>.Instance, new GitHubRateLimitStore(), new NoToken());
    }

    [Fact]
    public async Task AnalyzeAsync_assembles_a_full_result_from_canned_responses()
    {
        var svc = Build(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/contents/Program.cs")) return File("using System;\nclass Program { static void Main() { } }");
            if (path.EndsWith("/contents/widget.csproj")) return File("<Project><ItemGroup><PackageReference Include=\"Serilog\" Version=\"3.0.0\" /></ItemGroup></Project>");
            if (path.EndsWith("/contents/")) return Json("[{\"name\":\"Program.cs\",\"path\":\"Program.cs\",\"type\":\"file\"}]");
            if (path.EndsWith("/readme")) return File("# Widget\nDoes widget things.");
            if (path.EndsWith("/commits")) return Json("[{\"sha\":\"abc1234def\",\"html_url\":\"u\",\"commit\":{\"message\":\"init\",\"author\":{\"name\":\"a\",\"date\":\"2024-01-01T00:00:00Z\"}}}]");
            if (path.EndsWith("/languages")) return Json("{\"C#\":1000,\"HTML\":200}");
            if (path.Contains("/git/trees/")) return Json("{\"truncated\":false,\"tree\":[{\"path\":\"Program.cs\",\"type\":\"blob\",\"size\":500},{\"path\":\"widget.csproj\",\"type\":\"blob\",\"size\":300}]}");
            if (path.EndsWith("/releases")) return Json("[]");
            if (path.EndsWith("/repos/acme/widget")) return Json("{\"name\":\"widget\",\"description\":\"A widget\",\"default_branch\":\"main\",\"stargazers_count\":42,\"forks_count\":3,\"open_issues_count\":1,\"language\":\"C#\",\"owner\":{\"login\":\"acme\"},\"html_url\":\"https://github.com/acme/widget\"}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var outcome = await svc.AnalyzeAsync(new RepoReference("acme", "widget"));

        Assert.True(outcome.Success);
        var r = outcome.Value!;
        Assert.Equal("widget", r.Name);
        Assert.Equal("acme", r.Owner);
        Assert.Equal(42, r.Stars);
        Assert.True(r.HasReadme);
        Assert.Contains(r.Languages, l => l.Name == "C#");
        Assert.Contains(r.Packages, p => p.Name == "Serilog");
        Assert.Contains(r.KeyFiles, k => k.Path == "Program.cs");
        Assert.NotNull(r.Maturity);   // RepoAnalyzer ran
    }

    [Fact]
    public async Task AnalyzeAsync_returns_a_friendly_failure_on_404()
    {
        var svc = Build(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var outcome = await svc.AnalyzeAsync(new RepoReference("ghost", "nope"));

        Assert.False(outcome.Success);
        Assert.Contains("not found", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
