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
    private readonly ISubscriberStatusService _subscriberStatusService;
    private readonly ILogger<ArticleStorageService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ArticleStorageService(
        InfoGenDbContext dbContext,
        IBlobStorageService blobStorageService,
        ISubscriberStatusService subscriberStatusService,
        ILogger<ArticleStorageService> logger)
    {
        _dbContext = dbContext;
        _blobStorageService = blobStorageService;
        _subscriberStatusService = subscriberStatusService;
        _logger = logger;
    }

    public async Task<SavedArticleSummary> SaveArticleAsync(GeneratedArticle article, List<WikipediaPage> sourcePages, string? createdByUserId = null, List<ReferenceLink>? referenceLinks = null)
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
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId,
            ReferenceLinksJson = referenceLinks is { Count: > 0 }
                ? JsonSerializer.Serialize(referenceLinks.Select(r => new { r.Title, r.Slug }))
                : null
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

        string? creatorDisplayName = null;
        var creatorIsSubscribed = false;
        if (!string.IsNullOrEmpty(entity.CreatedByUserId))
        {
            // Never fall back to email/username here - both are PII. Users who haven't picked a
            // display name yet get a default that's stable (same value every time) but reveals
            // nothing about the account.
            var creatorDisplayNameSet = await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.Id == entity.CreatedByUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync();
            creatorDisplayName = string.IsNullOrEmpty(creatorDisplayNameSet)
                ? GenerateDefaultDisplayName(entity.CreatedByUserId)
                : creatorDisplayNameSet;
            creatorIsSubscribed = await _subscriberStatusService.IsSubscribedAsync(entity.CreatedByUserId);
        }

        return new SavedArticleDetail
        {
            Title = entity.Title,
            Slug = entity.Slug,
            ImageDescription = entity.ImageDescription,
            ImageUrl = entity.ImageUrl,
            Sections = NormalizeSections(JsonSerializer.Deserialize<List<ArticleSection>>(entity.SectionsJson, JsonOptions) ?? []),
            InfoboxFacts = JsonSerializer.Deserialize<List<InfoboxFact>>(entity.InfoboxFactsJson, JsonOptions) ?? [],
            SourcePages = JsonSerializer.Deserialize<List<SourcePageInfo>>(entity.SourcePagesJson, JsonOptions) ?? [],
            CreatedAt = entity.CreatedAt,
            CreatorDisplayName = creatorDisplayName,
            CreatorUserId = entity.CreatedByUserId,
            CreatorIsSubscribed = creatorIsSubscribed,
            TotalViews = entity.TotalViews,
            ReferenceLinks = string.IsNullOrEmpty(entity.ReferenceLinksJson)
                ? new List<ReferenceLink>()
                : JsonSerializer.Deserialize<List<ReferenceLink>>(entity.ReferenceLinksJson, JsonOptions) ?? new List<ReferenceLink>()
        };
    }

    public async Task RecordViewAsync(string slug)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Single atomic UPDATE (no read-then-write): the DB increments in place so concurrent views
        // can't lose counts. The daily counter resets to 1 the first time it's viewed on a new UTC day.
        // A non-existent slug simply matches zero rows.
        await _dbContext.SavedArticles
            .Where(a => a.Slug == slug)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.TotalViews, a => a.TotalViews + 1)
                .SetProperty(a => a.DailyViews, a => a.DailyViewsDate == today ? a.DailyViews + 1 : 1)
                .SetProperty(a => a.DailyViewsDate, a => today));
    }

    public async Task<List<SavedArticleSummary>> GetTopViewedTodayAsync(int count = 10)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await _dbContext.SavedArticles
            .Where(a => a.DailyViewsDate == today && a.DailyViews > 0)
            .OrderByDescending(a => a.DailyViews)
            .ThenByDescending(a => a.CreatedAt)
            .Take(count)
            .Select(a => new SavedArticleSummary
            {
                Title = a.Title,
                Slug = a.Slug,
                ImageUrl = a.ImageUrl,
                CreatedAt = a.CreatedAt,
                TotalViews = a.TotalViews,
                DailyViews = a.DailyViews
            })
            .ToListAsync();
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

    public async Task<List<SavedArticleSummary>> GetAllArticlesForSitemapAsync()
    {
        return await _dbContext.SavedArticles
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new SavedArticleSummary
            {
                Title = a.Title,
                Slug = a.Slug,
                ImageUrl = a.ImageUrl,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<string?> GetRandomArticleSlugAsync()
    {
        var count = await _dbContext.SavedArticles.CountAsync();
        if (count == 0) return null;
        var skip = Random.Shared.Next(0, count);
        return await _dbContext.SavedArticles
            .OrderBy(a => a.CreatedAt)
            .Skip(skip)
            .Take(1)
            .Select(a => a.Slug)
            .FirstOrDefaultAsync();
    }

    /// <summary>Re-splits stored paragraphs on read. Articles saved before the paragraph-splitting fix
    /// have literal "\n\n" sequences sitting inside a single paragraph; this makes them render as
    /// separate paragraphs without needing to rewrite the stored JSON.</summary>
    private static List<ArticleSection> NormalizeSections(List<ArticleSection> sections)
    {
        foreach (var section in sections)
            section.Paragraphs = section.Paragraphs.SelectMany(ParagraphSplitter.Split).ToList();
        return sections;
    }

    /// <summary>Deterministic (stable across app restarts) but non-identifying placeholder name for
    /// accounts that haven't picked a display name yet - never based on email/username.</summary>
    private static string GenerateDefaultDisplayName(string userId)
    {
        var hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(userId));
        var number = BitConverter.ToUInt32(hashBytes, 0) % 90000 + 10000;
        return $"User{number}";
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
