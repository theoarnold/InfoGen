using InfoGen.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InfoGen.Data;

public class InfoGenDbContext : IdentityDbContext<ApplicationUser>
{
    public InfoGenDbContext(DbContextOptions<InfoGenDbContext> options) : base(options) { }

    public DbSet<SavedArticleEntity> SavedArticles => Set<SavedArticleEntity>();

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
            entity.Property(e => e.ImageUrl).HasMaxLength(2000);
        });
    }
}
