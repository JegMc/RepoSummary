using Microsoft.AspNetCore.DataProtection;

namespace RepoSummary.Services;

/// <summary>
/// Remembers which AI provider to prefer ("openai" or "anthropic") when the user
/// has both keys set. Persisted like the other settings so the choice survives
/// restarts. When only one key is set, the generator uses that provider regardless.
/// </summary>
public sealed class AiProviderStore : EncryptedSecretStore
{
    public const string OpenAi = "openai";
    public const string Anthropic = "anthropic";

    public AiProviderStore(
        IDataProtectionProvider protection, IHostEnvironment env,
        IConfiguration config, ILogger<AiProviderStore> logger)
        : base(protection, env, logger, "RepoSummary.AiProvider", ".aiprovider", config["Ai:Provider"])
    {
    }

    /// <summary>The saved preference, or null if the user hasn't chosen one.</summary>
    public string? Preference =>
        string.Equals(Value, Anthropic, StringComparison.OrdinalIgnoreCase) ? Anthropic
        : string.Equals(Value, OpenAi, StringComparison.OrdinalIgnoreCase) ? OpenAi
        : null;
}
