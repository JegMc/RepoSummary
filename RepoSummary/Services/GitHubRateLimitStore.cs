namespace RepoSummary.Services;

/// <summary>
/// Remembers the most recent GitHub rate-limit headers so the UI can show how
/// many API calls are left and when the window resets. Updated by the service
/// on every response; read by the layout and the Settings page.
/// </summary>
public class GitHubRateLimitStore
{
    private readonly object _lock = new();

    public bool Known { get; private set; }
    public int Remaining { get; private set; }
    public int Limit { get; private set; }
    public DateTimeOffset? ResetsAt { get; private set; }

    /// <summary>True when a token is in use (limit well above the 60/hr anonymous cap).</summary>
    public bool Authenticated => Limit > 100;

    public void Update(int remaining, int limit, DateTimeOffset? resetsAt)
    {
        lock (_lock)
        {
            Known = true;
            Remaining = remaining;
            Limit = limit;
            ResetsAt = resetsAt;
        }
    }
}
