using AngleSharp.Dom;
using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Html;

namespace RepoSummary.Services;

/// <summary>
/// Renders a repository README (Markdown) to safe HTML so it displays the way
/// GitHub shows it — badges, images, headings, tables, and code blocks — instead
/// of as raw text. Untrusted content: Markdig produces the HTML, then
/// HtmlSanitizer strips anything dangerous (scripts, event handlers, javascript:
/// URLs). Relative image/link URLs are rewritten to absolute GitHub URLs so
/// repo-relative badges and logos resolve.
/// </summary>
public static class ReadmeRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()   // tables, task lists, autolinks — GitHub-flavored-ish
            .UseEmojiAndSmiley()       // :rocket: style shortcodes, like GitHub
            .Build();

    public static HtmlString ToHtml(string markdown, string owner, string repo, string? defaultBranch)
    {
        // Note: raw HTML is intentionally NOT disabled — many READMEs write badges as
        // <img> tags. HtmlSanitizer below is the security boundary that makes this safe.
        var rawHtml = Markdown.ToHtml(markdown, Pipeline);

        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? "HEAD" : defaultBranch;
        var rawBase = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/";
        var blobBase = $"https://github.com/{owner}/{repo}/blob/{branch}/";

        var sanitizer = new HtmlSanitizer();
        // Preserve layout/sizing attributes badges and logos commonly use.
        sanitizer.AllowedAttributes.Add("align");
        sanitizer.AllowedAttributes.Add("width");
        sanitizer.AllowedAttributes.Add("height");
        sanitizer.AllowedAttributes.Add("target");
        sanitizer.AllowedAttributes.Add("rel");

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not IElement el) return;

            if (el.TagName == "IMG")
            {
                var src = el.GetAttribute("src");
                if (src is not null) el.SetAttribute("src", ToAbsolute(src, rawBase));
            }
            else if (el.TagName == "A")
            {
                var href = el.GetAttribute("href");
                if (href is not null) el.SetAttribute("href", ToAbsolute(href, blobBase));
                el.SetAttribute("target", "_blank");
                el.SetAttribute("rel", "noopener noreferrer");
            }
        };

        return new HtmlString(sanitizer.Sanitize(rawHtml));
    }

    private static string ToAbsolute(string url, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        var u = url.Trim();

        if (u.StartsWith('#')) return u;                         // in-page anchor
        if (u.StartsWith("http://") || u.StartsWith("https://")) return u;
        if (u.StartsWith("//")) return "https:" + u;             // protocol-relative
        if (u.StartsWith("mailto:") || u.StartsWith("tel:") || u.StartsWith("data:")) return u;

        // Relative → resolve against the repo's raw/blob base.
        u = u.TrimStart('/');
        while (u.StartsWith("./")) u = u[2..];
        while (u.StartsWith("../")) u = u[3..];
        return baseUrl + u;
    }
}
