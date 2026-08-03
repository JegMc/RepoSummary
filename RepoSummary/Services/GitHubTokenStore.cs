using Microsoft.AspNetCore.DataProtection;

namespace RepoSummary.Services;

/// <summary>
/// Base for secrets a user enters at runtime (on the Settings page). Kept in memory
/// for fast access and persisted encrypted-at-rest (ASP.NET Data Protection) to a
/// gitignored file, so it survives restarts and is never committed to the repo.
/// The encryption keys are scoped to this machine/user, so the saved file can't be
/// decrypted anywhere else. Seeded from configuration on first run if a file exists.
/// </summary>
public abstract class EncryptedSecretStore
{
    private readonly IDataProtector _protector;
    private readonly ILogger _logger;
    private readonly string _file;
    private readonly object _lock = new();
    private string? _value;

    protected EncryptedSecretStore(
        IDataProtectionProvider protection, IHostEnvironment env, ILogger logger,
        string purpose, string fileName, string? seed)
    {
        _protector = protection.CreateProtector(purpose);
        _logger = logger;
        _file = Path.Combine(env.ContentRootPath, fileName);

        var saved = LoadFromDisk();
        _value = !string.IsNullOrWhiteSpace(saved) ? saved : seed;
    }

    public string? Value { get { lock (_lock) return _value; } }

    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    /// <summary>A masked preview for display, e.g. "ghp_…a1b2". Never reveals the middle of the secret.</summary>
    public string? Masked => Mask(Value);

    /// <summary>Pure masking used for any UI display of a secret — shows at most the first and
    /// last four characters so a full key can never be rendered. Unit-tested.</summary>
    public static string? Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= 8 ? "••••" : value[..4] + "…" + value[^4..];
    }

    public void Save(string value)
    {
        value = value.Trim();
        lock (_lock) _value = value;
        try { File.WriteAllText(_file, _protector.Protect(value)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not persist secret to {File}.", _file); }
    }

    public void Clear()
    {
        lock (_lock) _value = null;
        try { if (File.Exists(_file)) File.Delete(_file); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete secret file {File}.", _file); }
    }

    private string? LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_file)) return null;
            return _protector.Unprotect(File.ReadAllText(_file));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read secret file {File}; ignoring it.", _file);
            return null;
        }
    }
}

/// <summary>The GitHub personal-access token (raises the API rate limit).</summary>
public sealed class GitHubTokenStore : EncryptedSecretStore
{
    public GitHubTokenStore(
        IDataProtectionProvider protection, IHostEnvironment env,
        IConfiguration config, ILogger<GitHubTokenStore> logger)
        : base(protection, env, logger, "RepoSummary.GitHubToken", ".githubtoken", config["GitHub:Token"])
    {
    }
}
