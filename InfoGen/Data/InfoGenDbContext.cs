using InfoGen.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InfoGen.Data;

public class InfoGenDbContext : IdentityDbContext<ApplicationUser>
{
    public InfoGenDbContext(DbContextOptions<InfoGenDbContext> options) : base(options) { }

    public DbSet<SavedArticleEntity> SavedArticles => Set<SavedArticleEntity>();
    public DbSet<MonthlyGenerationUsage> MonthlyGenerationUsage => Set<MonthlyGenerationUsage>();
    public DbSet<GenerationCreditPurchase> GenerationCreditPurchases => Set<GenerationCreditPurchase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<SavedArticleEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.Slug).HasMaxLength(500);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.Property(e => e.ImageDescription).HasMaxLength(1000);
            entity.Property(e => e.Summary).HasMaxLength(300);
            entity.Property(e => e.ImageUrl).HasMaxLength(2000);
            entity.Property(e => e.CreatedByUserId).HasMaxLength(450);
            entity.Property(e => e.ReferenceLinksJson).HasMaxLength(4000);
        });
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.DisplayName).HasMaxLength(256);
        });
        modelBuilder.Entity<MonthlyGenerationUsage>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.Year, e.Month });
            entity.Property(e => e.UserId).HasMaxLength(450);
        });
        modelBuilder.Entity<GenerationCreditPurchase>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.Property(e => e.StripeCheckoutSessionId).HasMaxLength(255);
            entity.HasIndex(e => e.StripeCheckoutSessionId).IsUnique();
        });
    }
}
