using Microsoft.AspNetCore.DataProtection;

namespace RepoSummary.Services;

/// <summary>
/// The Anthropic (Claude) API key used for AI career-material generation. Entered
/// on the Settings page, stored encrypted on this machine only, and applied per
/// request. Seeded from Anthropic:ApiKey / ANTHROPIC_API_KEY if either is present.
/// </summary>
public sealed class AnthropicKeyStore : EncryptedSecretStore
{
    public AnthropicKeyStore(
        IDataProtectionProvider protection, IHostEnvironment env,
        IConfiguration config, ILogger<AnthropicKeyStore> logger)
        : base(protection, env, logger, "RepoSummary.AnthropicKey", ".anthropickey",
            config["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
    {
    }
}
