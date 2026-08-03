using Microsoft.EntityFrameworkCore;

namespace RepoSummary.Data;

/// <summary>A saved repository analysis. The full result is stored as JSON so a
/// past analysis can be re-opened instantly without hitting GitHub again; the
/// other columns are kept for cheap listing/sorting.</summary>
public class SavedAnalysis
{
    public int Id { get; set; }
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    // Stored as UTC DateTime: SQLite can't ORDER BY DateTimeOffset.
    public DateTime AnalyzedAt { get; set; }
    public string? PrimaryLanguage { get; set; }
    public int? MaturityScore { get; set; }
    public string? MaturityGrade { get; set; }
    public string ResultJson { get; set; } = "";
}

/// <summary>A point-in-time maturity reading for a repo, so the grade can be tracked over time.</summary>
public class MaturitySnapshot
{
    public int Id { get; set; }
    public string RepoFullName { get; set; } = "";
    public DateTime RecordedAt { get; set; }   // UTC
    public int Score { get; set; }
    public string Grade { get; set; } = "";
}

/// <summary>A generated STAR interview story the user chose to keep.</summary>
public class SavedStory
{
    public int Id { get; set; }
    public string RepoFullName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? EvidenceCsv { get; set; }
    public DateTime SavedAt { get; set; }
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SavedAnalysis> Analyses => Set<SavedAnalysis>();
    public DbSet<SavedStory> Stories => Set<SavedStory>();
    public DbSet<MaturitySnapshot> MaturityHistory => Set<MaturitySnapshot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SavedAnalysis>().HasIndex(a => a.FullName).IsUnique();
        b.Entity<SavedStory>().HasIndex(s => s.RepoFullName);
        b.Entity<MaturitySnapshot>().HasIndex(s => s.RepoFullName);
    }
}
