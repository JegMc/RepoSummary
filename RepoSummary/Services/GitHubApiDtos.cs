using System.Text.Json.Serialization;

namespace RepoSummary.Services;

// Minimal DTOs mapping only the GitHub API fields this app uses.
// Kept internal to the service layer; pages never see these.

internal sealed class RepoDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("default_branch")] public string? DefaultBranch { get; set; }
    [JsonPropertyName("stargazers_count")] public int Stars { get; set; }
    [JsonPropertyName("forks_count")] public int Forks { get; set; }
    [JsonPropertyName("open_issues_count")] public int OpenIssues { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("owner")] public OwnerDto? Owner { get; set; }
    [JsonPropertyName("license")] public LicenseDto? License { get; set; }
    [JsonPropertyName("fork")] public bool Fork { get; set; }
}

internal sealed class OwnerDto
{
    [JsonPropertyName("login")] public string? Login { get; set; }
}

internal sealed class LicenseDto
{
    [JsonPropertyName("spdx_id")] public string? SpdxId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class ReadmeDto
{
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("encoding")] public string? Encoding { get; set; }
}

internal sealed class CommitDto
{
    [JsonPropertyName("sha")] public string? Sha { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("commit")] public CommitDetailDto? Commit { get; set; }
}

internal sealed class CommitDetailDto
{
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("author")] public CommitAuthorDto? Author { get; set; }
}

internal sealed class CommitAuthorDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("date")] public DateTimeOffset? Date { get; set; }
}

internal sealed class ContentItemDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    // Present on the "get file contents" response (base64), used to read manifests.
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("encoding")] public string? Encoding { get; set; }
}

internal sealed class TreeResponseDto
{
    [JsonPropertyName("tree")] public List<TreeEntryDto>? Tree { get; set; }
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
}

internal sealed class TreeEntryDto
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    /// <summary>"blob" (file) or "tree" (directory).</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }
    /// <summary>Blob size in bytes (present for files). Used to pick the largest/most-central files.</summary>
    [JsonPropertyName("size")] public long Size { get; set; }
}

internal sealed class ReleaseDto
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
}
