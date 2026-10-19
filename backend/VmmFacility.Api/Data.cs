using Microsoft.EntityFrameworkCore;

namespace VmmFacility.Api;

public class Expense
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string PaymentState { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class AppUser
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Expense>(e =>
        {
            // SQLite stores decimal as TEXT, which keeps amounts exact.
            e.Property(x => x.Amount).HasColumnType("TEXT").IsRequired();
            e.Property(x => x.Category).HasMaxLength(50).IsRequired();
            e.Property(x => x.Description).HasMaxLength(ExpenseRules.DescriptionMaxLength).IsRequired();
            e.Property(x => x.PaymentState).HasMaxLength(10).IsRequired();
            e.HasIndex(x => x.Date);
        });

        modelBuilder.Entity<AppUser>(u =>
        {
            u.Property(x => x.UserName).HasMaxLength(50).IsRequired();
            u.Property(x => x.PasswordHash).IsRequired();
        });
    }
}

public static class AppPaths
{
    // ~/Library/Application Support/VMM Facility Management, unless VMM_DATA_DIR is set.
    public static string DataDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("VMM_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return Path.GetFullPath(overrideDir);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Application Support", "VMM Facility Management");
    }
}
