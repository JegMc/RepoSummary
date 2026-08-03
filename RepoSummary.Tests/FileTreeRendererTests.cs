using RepoSummary.Services;

namespace RepoSummary.Tests;

public class FileTreeRendererTests
{
    [Fact]
    public void Build_lists_directories_before_files_and_marks_types()
    {
        var paths = new[] { "README.md", "src/Program.cs", "src/Services/Foo.cs", ".gitignore" };

        var lines = FileTreeRenderer.Build(paths);

        // Directories sort before files at each level, so "src" leads.
        Assert.Equal("src", lines[0].Name);
        Assert.True(lines[0].IsDirectory);
        Assert.Contains(lines, l => l.Name == "Program.cs" && !l.IsDirectory);
        Assert.Contains(lines, l => l.Name == "Services" && l.IsDirectory);
    }

    [Fact]
    public void Build_respects_maxLines()
    {
        var paths = Enumerable.Range(0, 50).Select(i => $"file{i}.txt").ToArray();

        var lines = FileTreeRenderer.Build(paths, maxLines: 10);

        Assert.Equal(10, lines.Count);
    }

    [Fact]
    public void BuildTree_nests_children_and_builds_blob_urls_for_files()
    {
        var paths = new[] { "src/Program.cs", "README.md" };

        var (root, truncated) = FileTreeRenderer.BuildTree(paths, "https://github.com/o/r", "main");

        Assert.False(truncated);

        var src = root.Children.First(c => c.Name == "src");
        Assert.True(src.IsDirectory);

        var program = src.Children.Single();
        Assert.Equal("Program.cs", program.Name);
        Assert.False(program.IsDirectory);
        Assert.Equal("https://github.com/o/r/blob/main/src/Program.cs", program.HtmlUrl);

        var readme = root.Children.First(c => c.Name == "README.md");
        Assert.Equal("https://github.com/o/r/blob/main/README.md", readme.HtmlUrl);
    }

    [Fact]
    public void BuildTree_flags_truncation_when_over_budget()
    {
        var paths = Enumerable.Range(0, 50).Select(i => $"f{i}.txt").ToArray();

        var (_, truncated) = FileTreeRenderer.BuildTree(paths, null, null, maxNodes: 10);

        Assert.True(truncated);
    }

    [Fact]
    public void BuildTree_without_repo_url_leaves_urls_null()
    {
        var (root, _) = FileTreeRenderer.BuildTree(new[] { "a.txt" }, null, null);

        Assert.Null(root.Children.Single().HtmlUrl);
    }
}
