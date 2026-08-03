using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Html;

namespace RepoSummary.Services;

/// <summary>
/// Renders short AI-generated text (which often contains markdown — bullets,
/// bold, paragraphs) into safe, readable HTML. Simpler than <see cref="ReadmeRenderer"/>:
/// no URL rewriting, just format + sanitize.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static HtmlString ToHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return HtmlString.Empty;
        var html = Markdown.ToHtml(text, Pipeline);
        return new HtmlString(new HtmlSanitizer().Sanitize(html));
    }
}
