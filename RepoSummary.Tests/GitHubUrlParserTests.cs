using RepoSummary.Services;

namespace RepoSummary.Tests;

public class GitHubUrlParserTests
{
    private readonly GitHubUrlParser _parser = new();

    [Theory]
    [InlineData("owner/repo", "owner", "repo")]
    [InlineData("github.com/owner/repo", "owner", "repo")]
    [InlineData("https://github.com/owner/repo", "owner", "repo")]
    [InlineData("http://www.github.com/owner/repo/", "owner", "repo")]
    [InlineData("https://github.com/owner/repo.git", "owner", "repo")]
    [InlineData("https://github.com/owner/repo/tree/main/src", "owner", "repo")]
    [InlineData("  dotnet/aspnetcore  ", "dotnet", "aspnetcore")]
    [InlineData("some-org/my.repo-name_2", "some-org", "my.repo-name_2")]
    public void Parses_valid_input(string input, string expectedOwner, string expectedRepo)
    {
        var ok = _parser.TryParse(input, out var reference, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(reference);
        Assert.Equal(expectedOwner, reference!.Owner);
        Assert.Equal(expectedRepo, reference.Repo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("noslasheshere")]
    public void Rejects_invalid_input(string? input)
    {
        var ok = _parser.TryParse(input, out var reference, out var error);

        Assert.False(ok);
        Assert.Null(reference);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Rejects_when_repo_is_only_dot_git()
    {
        var ok = _parser.TryParse("owner/.git", out var reference, out var error);

        Assert.False(ok);
        Assert.Null(reference);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
