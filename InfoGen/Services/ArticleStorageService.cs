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

    // Column is nvarchar(300); headroom for the ellipsis.
    private const int MaxSummaryLength = 280;

    private const int MaxStoredReferenceLinks = 25;

    // Matches the ReferenceLinksJson column, nvarchar(4000).
    private const int MaxReferenceLinksJsonLength = 4000;

    // List/search endpoints are anonymous, so the caller doesn't choose how much of the table to pull.
    private const int MaxPageSize = 50;

    public ArticleStorageService(
        InfoGenDbContext dbContext,
        IBlobStorageService blobStorageService,
        ILogger<ArticleStorageService> logger)
    {
        _dbContext = dbContext;
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    public async Task<SavedArticleSummary> SaveArticleAsync(GeneratedArticle article, List<WikipediaPage> sourcePages, string? createdByUserId = null, List<ReferenceLink>? referenceLinks = null)
    {
        string? imageUrl = null;
        if (!string.IsNullOrEmpty(article.ImageDataUrl))
        {
            _logger.LogInformation("Uploading image to Azure Blob Storage...");
            try
            {
                imageUrl = await _blobStorageService.UploadImageAsync(article.ImageDataUrl);
            }
            catch (ArgumentException ex)
            {
                // The session is already consumed, so failing the save would lose the article for good.
                _logger.LogError(ex, "Rejected the generated image; saving '{Title}' without one.", article.Title);
            }
        }

        var slug = await GenerateUniqueSlugAsync(article.Title);

        // Deliberately ignores the caller's list: the catalogue offered to the model may hold hundreds
        // of entries, and only links actually present in the body text can render.
        referenceLinks = await ResolveReferenceLinksAsync(article);

        var entity = new SavedArticleEntity
        {
            Id = Guid.NewGuid(),
            Title = article.Title,
            Slug = slug,
            Summary = DeriveSummary(article),
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
            ReferenceLinksJson = SerializeReferenceLinks(referenceLinks)
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
        // Clamped here rather than at the endpoint: this is an unindexed LIKE '%...%' scan reachable
        // anonymously, so every caller has to be bounded.
        take = Math.Clamp(take, 1, MaxPageSize);
        skip = Math.Max(skip, 0);

        return await _dbContext.SavedArticles
            .Where(a => a.Title.Contains(query) || (a.Summary != null && a.Summary.Contains(query)))
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
            // Never fall back to email/username for the display name - both are PII.
            var creator = await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.Id == entity.CreatedByUserId)
                .Select(u => new { u.DisplayName, u.IsSubscribed })
                .FirstOrDefaultAsync();
            creatorDisplayName = string.IsNullOrEmpty(creator?.DisplayName)
                ? GenerateDefaultDisplayName(entity.CreatedByUserId)
                : creator.DisplayName;
            creatorIsSubscribed = creator?.IsSubscribed ?? false;
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
        // Atomic UPDATE rather than read-then-write, so concurrent views can't lose counts.
        await _dbContext.SavedArticles
            .Where(a => a.Slug == slug)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.TotalViews, a => a.TotalViews + 1)
                .SetProperty(a => a.DailyViews, a => a.DailyViewsDate == today ? a.DailyViews + 1 : 1)
                .SetProperty(a => a.DailyViewsDate, a => today));
    }

    public async Task<List<SavedArticleSummary>> GetTopViewedTodayAsync(int count = 10)
    {
        count = Math.Clamp(count, 1, MaxPageSize);
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
        count = Math.Clamp(count, 1, MaxPageSize);
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

    public async Task<List<ArticleCatalogueEntry>> SearchCatalogueAsync(string query, int take = 10)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        query = query.Trim();
        take = Math.Clamp(take, 1, 25);

        return await _dbContext.SavedArticles
            .Where(a => a.Title.Contains(query) || (a.Summary != null && a.Summary.Contains(query)))
            .OrderByDescending(a => a.TotalViews)
            .ThenByDescending(a => a.CreatedAt)
            .Take(take)
            .Select(a => new ArticleCatalogueEntry
            {
                Title = a.Title,
                Slug = a.Slug,
                Summary = a.Summary
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

    /// <summary>Resolves the [[Title]] links the model wrote against real saved articles. Titles that
    /// don't match anything are dropped and render as plain text.</summary>
    public async Task<List<ReferenceLink>> ResolveReferenceLinksAsync(GeneratedArticle article)
    {
        var texts = article.Sections
            .SelectMany(s => s.Paragraphs)
            .Concat(article.InfoboxFacts.Select(f => f.Value))
            .Append(article.ImageDescription);

        var titles = texts
            .SelectMany(ReferenceLinkRenderer.ExtractTitles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxStoredReferenceLinks)
            .ToList();

        if (titles.Count == 0) return [];

        return await _dbContext.SavedArticles
            .Where(a => titles.Contains(a.Title))
            .Select(a => new ReferenceLink { Title = a.Title, Slug = a.Slug })
            .ToListAsync();
    }

    /// <summary>Adds links one at a time while they still fit ReferenceLinksJson's nvarchar(4000).
    /// MaxStoredReferenceLinks alone doesn't bound this - Title and Slug are nvarchar(500) each, so 25
    /// entries can overflow the column and throw on SaveChanges, losing the whole article.</summary>
    private static string? SerializeReferenceLinks(List<ReferenceLink>? referenceLinks)
    {
        if (referenceLinks is not { Count: > 0 }) return null;

        var kept = new List<object>();
        string? json = null;

        foreach (var link in referenceLinks)
        {
            kept.Add(new { link.Title, link.Slug });
            var candidate = JsonSerializer.Serialize(kept);
            if (candidate.Length > MaxReferenceLinksJsonLength)
            {
                kept.RemoveAt(kept.Count - 1);
                break;
            }
            json = candidate;
        }

        return json;
    }

    private static string? DeriveSummary(GeneratedArticle article)
    {
        var summary = article.Summary;
        if (string.IsNullOrWhiteSpace(summary))
            summary = FirstSentence(article.Sections.FirstOrDefault()?.Paragraphs.FirstOrDefault());

        if (string.IsNullOrWhiteSpace(summary)) return null;

        // Summaries are fed back to the model as catalogue entries and matched literally by search, so
        // the [[Title]] markers the model writes into lead sentences have to come out.
        summary = ReferenceLinkRenderer.StripMarkers(summary).Trim();
        if (summary.Length == 0) return null;
        return summary.Length <= MaxSummaryLength ? summary : summary[..MaxSummaryLength].TrimEnd() + "…";
    }

    private static string? FirstSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        return end > 0 ? text[..(end + 1)] : text;
    }

    /// <summary>Re-splits stored paragraphs on read. Articles saved before the paragraph-splitting fix
    /// have literal "\n\n" inside a single paragraph; this avoids rewriting the stored JSON.</summary>
    private static List<ArticleSection> NormalizeSections(List<ArticleSection> sections)
    {
        foreach (var section in sections)
            section.Paragraphs = section.Paragraphs.SelectMany(ParagraphSplitter.Split).ToList();
        return sections;
    }

    // Stable across restarts but non-identifying - never derived from email/username.
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
