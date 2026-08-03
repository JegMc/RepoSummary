using Microsoft.AspNetCore.Mvc.RazorPages;
using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Pages;

public class ProfileModel : PageModel
{
    private readonly IGitHubRepositoryService _service;

    public ProfileModel(IGitHubRepositoryService service) => _service = service;

    public string? User { get; private set; }
    public IReadOnlyList<UserRepoSummary> Repos { get; private set; } = Array.Empty<UserRepoSummary>();
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(string? user, CancellationToken ct)
    {
        var u = user?.Trim();
        if (string.IsNullOrEmpty(u)) return;

        // Accept a pasted profile URL or a bare username.
        u = u.Replace("https://", "").Replace("http://", "").Replace("github.com/", "").Trim('/', ' ');
        u = (u.Split('/', '?').FirstOrDefault() ?? u).Trim();
        User = u;
        if (string.IsNullOrEmpty(u)) return;

        var outcome = await _service.GetUserReposAsync(u, ct);
        if (outcome.Success) Repos = outcome.Value!;
        else ErrorMessage = outcome.ErrorMessage;
    }
}
