using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RepoSummary.Services;

namespace RepoSummary.Pages;

public class SettingsModel : PageModel
{
    private readonly GitHubTokenStore _tokens;
    private readonly OpenAiKeyStore _openAi;
    private readonly AnthropicKeyStore _anthropic;
    private readonly AiProviderStore _preference;
    private readonly ICareerArtifactGenerator _generator;

    public SettingsModel(
        GitHubTokenStore tokens,
        OpenAiKeyStore openAi,
        AnthropicKeyStore anthropic,
        AiProviderStore preference,
        ICareerArtifactGenerator generator,
        GitHubRateLimitStore rateLimit)
    {
        _tokens = tokens;
        _openAi = openAi;
        _anthropic = anthropic;
        _preference = preference;
        _generator = generator;
        RateLimit = rateLimit;
    }

    public GitHubRateLimitStore RateLimit { get; }

    [BindProperty] public string? GitHubToken { get; set; }
    [BindProperty] public string? OpenAiKey { get; set; }
    [BindProperty] public string? AnthropicKey { get; set; }
    [BindProperty] public string? Provider { get; set; }

    /// <summary>Where to send the user back to after they finish here (set when they arrived
    /// from an "Add a key" CTA mid-task). Only honored when it's a local URL.</summary>
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public bool HasReturn => !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl);

    public bool HasToken => _tokens.HasValue;
    public string? MaskedToken => _tokens.Masked;
    public bool HasOpenAi => _openAi.HasValue;
    public string? MaskedOpenAi => _openAi.Masked;
    public bool HasAnthropic => _anthropic.HasValue;
    public string? MaskedAnthropic => _anthropic.Masked;
    public bool BothKeysSet => HasOpenAi && HasAnthropic;
    public string? PreferredProvider => _preference.Preference ?? AiProviderStore.OpenAi;

    public bool AiConfigured => _generator.IsConfigured;
    public string ActiveProviderName => _generator.ActiveProviderName;

    [TempData] public string? StatusMessage { get; set; }

    public void OnGet() { }

    public IActionResult OnPostSaveOpenAi()
    {
        if (string.IsNullOrWhiteSpace(OpenAiKey))
            StatusMessage = "Please paste an OpenAI API key before saving.";
        else
        {
            _openAi.Save(OpenAiKey);
            StatusMessage = "OpenAI key saved. AI generation is enabled.";
        }
        return RedirectToPage(new { ReturnUrl });
    }

    public IActionResult OnPostClearOpenAi()
    {
        _openAi.Clear();
        StatusMessage = "OpenAI key removed.";
        return RedirectToPage(new { ReturnUrl });
    }

    public IActionResult OnPostSaveAnthropic()
    {
        if (string.IsNullOrWhiteSpace(AnthropicKey))
            StatusMessage = "Please paste an Anthropic API key before saving.";
        else
        {
            _anthropic.Save(AnthropicKey);
            StatusMessage = "Anthropic key saved. AI generation is enabled.";
        }
        return RedirectToPage(new { ReturnUrl });
    }

    public IActionResult OnPostClearAnthropic()
    {
        _anthropic.Clear();
        StatusMessage = "Anthropic key removed.";
        return RedirectToPage(new { ReturnUrl });
    }

    public IActionResult OnPostSavePreference()
    {
        if (Provider is AiProviderStore.OpenAi or AiProviderStore.Anthropic)
        {
            _preference.Save(Provider);
            StatusMessage = $"Preferred provider set to {(Provider == AiProviderStore.Anthropic ? "Anthropic (Claude)" : "OpenAI (ChatGPT)")}.";
        }
        return RedirectToPage(new { ReturnUrl });
    }

    public IActionResult OnPostSaveGitHub()
    {
        if (string.IsNullOrWhiteSpace(GitHubToken))
            StatusMessage = "Please paste a GitHub token before saving.";
        else
        {
            _tokens.Save(GitHubToken);
            StatusMessage = "GitHub token saved. Your limit is now 5,000 requests/hour.";
        }
        return RedirectToPage(new { ReturnUrl });
    }

    public IActionResult OnPostClearGitHub()
    {
        _tokens.Clear();
        StatusMessage = "GitHub token removed. Back to the anonymous limit (60 requests/hour).";
        return RedirectToPage(new { ReturnUrl });
    }
}
