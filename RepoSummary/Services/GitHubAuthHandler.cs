using System.Net.Http.Headers;

namespace RepoSummary.Services;

/// <summary>
/// Adds the current GitHub token (from <see cref="GitHubTokenStore"/>) to each
/// outgoing request. Doing it per-request — rather than once at startup — means a
/// token saved on the Settings page takes effect immediately, no restart needed.
/// </summary>
public class GitHubAuthHandler : DelegatingHandler
{
    private readonly GitHubTokenStore _tokens;

    public GitHubAuthHandler(GitHubTokenStore tokens) => _tokens = tokens;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var token = _tokens.Value;
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
