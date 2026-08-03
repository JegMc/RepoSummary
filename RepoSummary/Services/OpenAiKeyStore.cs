using Microsoft.AspNetCore.DataProtection;

namespace RepoSummary.Services;

/// <summary>
/// The OpenAI API key used for AI career-material generation. Entered on the
/// Settings page, stored encrypted on this machine only, and applied per request —
/// so cloning the repo needs no environment variables or config edits. Seeded from
/// OpenAI:ApiKey / OPENAI_API_KEY if either is present.
/// </summary>
public sealed class OpenAiKeyStore : EncryptedSecretStore
{
    public OpenAiKeyStore(
        IDataProtectionProvider protection, IHostEnvironment env,
        IConfiguration config, ILogger<OpenAiKeyStore> logger)
        : base(protection, env, logger, "RepoSummary.OpenAiKey", ".openaikey",
            config["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
    {
    }
}
