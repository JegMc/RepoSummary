using RepoSummary.Models;
using RepoSummary.Services;

namespace RepoSummary.Tests;

public class ExportBuilderTests
{
    [Fact]
    public void ToMarkdown_includes_name_grade_and_sections()
    {
        var r = new GitHubRepoAnalysisResult
        {
            Owner = "acme", Name = "widget",
            Description = "A widget",
            Stars = 42,
            PrimaryLanguage = "C#",
            Maturity = new RepoMaturity { Grade = "B", Score = 72 },
            Languages = new() { new GitHubLanguageSummary { Name = "C#", Percentage = 90 } }
        };

        var md = ExportBuilder.ToMarkdown(r);

        Assert.Contains("# acme/widget", md);
        Assert.Contains("B (72/100)", md);
        Assert.Contains("## Languages", md);
    }

    [Fact]
    public void BadgeSvg_renders_grade_and_color()
    {
        var svg = ExportBuilder.BadgeSvg("A", 95);
        Assert.StartsWith("<svg", svg);
        Assert.Contains("RepoSummary", svg);
        Assert.Contains("A · 95", svg);
        Assert.Contains("#1a7f37", svg);   // grade A colour
    }

    [Fact]
    public void BadgeSvg_handles_missing_grade()
    {
        var svg = ExportBuilder.BadgeSvg(null, null);
        Assert.Contains("#8b949e", svg);   // unknown colour
    }
}
