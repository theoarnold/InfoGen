using System.Text.Json;
using System.Text.RegularExpressions;
using InfoGen.Data;
using InfoGen.Entities;
using InfoGen.Models;
using Microsoft.EntityFrameworkCore;

namespace InfoGen.Services;

public partial class ArticleStorageService : IArticleStorageService
{
    private readonly InfoGenDbContext _dbContext;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<ArticleStorageService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ArticleStorageService(
        InfoGenDbContext dbContext,
        IBlobStorageService blobStorageService,
        ILogger<ArticleStorageService> logger)
    {
        _dbContext = dbContext;
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    public async Task<SavedArticleSummary> SaveArticleAsync(GeneratedArticle article, List<WikipediaPage> sourcePages)
    {
        string? imageUrl = null;
        if (!string.IsNullOrEmpty(article.ImageDataUrl))
        {
            _logger.LogInformation("Uploading image to Azure Blob Storage...");
            imageUrl = await _blobStorageService.UploadImageAsync(article.ImageDataUrl);
        }

        var slug = await GenerateUniqueSlugAsync(article.Title);

        var entity = new SavedArticleEntity
        {
            Id = Guid.NewGuid(),
            Title = article.Title,
            Slug = slug,
            ImageDescription = article.ImageDescription,
            ImageUrl = imageUrl,
            SectionsJson = JsonSerializer.Serialize(article.Sections),
            InfoboxFactsJson = JsonSerializer.Serialize(article.InfoboxFacts),
            SourcePagesJson = JsonSerializer.Serialize(sourcePages.Select(p => new
            {
                p.Title,
                p.Url,
                p.Extract
            })),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.SavedArticles.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Saved article '{Title}' with slug '{Slug}'", entity.Title, entity.Slug);
        return new SavedArticleSummary
        {
            Title = entity.Title,
            Slug = entity.Slug,
            ImageUrl = entity.ImageUrl,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<SavedArticleSummary>> SearchArticlesAsync(string query, int skip = 0, int take = 15)
    {
        return await _dbContext.SavedArticles
            .Where(a => a.Title.Contains(query))
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(a => new SavedArticleSummary
            {
                Title = a.Title,
                Slug = a.Slug,
                ImageUrl = a.ImageUrl,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<SavedArticleDetail?> GetArticleBySlugAsync(string slug)
    {
        var entity = await _dbContext.SavedArticles
            .FirstOrDefaultAsync(a => a.Slug == slug);

        if (entity == null) return null;

        return new SavedArticleDetail
        {
            Title = entity.Title,
            Slug = entity.Slug,
            ImageDescription = entity.ImageDescription,
            ImageUrl = entity.ImageUrl,
            Sections = JsonSerializer.Deserialize<List<ArticleSection>>(entity.SectionsJson, JsonOptions) ?? [],
            InfoboxFacts = JsonSerializer.Deserialize<List<InfoboxFact>>(entity.InfoboxFactsJson, JsonOptions) ?? [],
            SourcePages = JsonSerializer.Deserialize<List<SourcePageInfo>>(entity.SourcePagesJson, JsonOptions) ?? [],
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<SavedArticleSummary>> GetRecentArticlesAsync(int count = 10)
    {
        return await _dbContext.SavedArticles
            .OrderByDescending(a => a.CreatedAt)
            .Take(count)
            .Select(a => new SavedArticleSummary
            {
                Title = a.Title,
                Slug = a.Slug,
                ImageUrl = a.ImageUrl,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    private async Task<string> GenerateUniqueSlugAsync(string title)
    {
        var slug = Slugify(title);
        var baseSlug = slug;
        var counter = 1;

        while (await _dbContext.SavedArticles.AnyAsync(a => a.Slug == slug))
        {
            slug = $"{baseSlug}-{counter++}";
        }

        return slug;
    }

    private static string Slugify(string text)
    {
        var slug = text.ToLowerInvariant();
        slug = SlugNonAlphanumeric().Replace(slug, "-");
        slug = SlugMultipleDashes().Replace(slug, "-");
        return slug.Trim('-');
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugNonAlphanumeric();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex SlugMultipleDashes();
}
