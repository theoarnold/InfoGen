using InfoGen.Models;

namespace InfoGen.Services;

public interface IArticleStorageService
{
    Task<SavedArticleSummary> SaveArticleAsync(GeneratedArticle article, List<WikipediaPage> sourcePages, string? createdByUserId = null, List<ReferenceLink>? referenceLinks = null);
    Task<List<SavedArticleSummary>> SearchArticlesAsync(string query, int skip = 0, int take = 15);
    Task<SavedArticleDetail?> GetArticleBySlugAsync(string slug);
    Task<List<SavedArticleSummary>> GetRecentArticlesAsync(int count = 10);
    /// <summary>Articles ordered by today's (UTC) view count, most-viewed first. Only includes articles viewed today.</summary>
    Task<List<SavedArticleSummary>> GetTopViewedTodayAsync(int count = 10);
    /// <summary>Records one page view for the article: bumps the all-time total and the per-day counter
    /// (resetting the daily counter when the UTC day rolls over). No-op if the slug doesn't exist.</summary>
    Task RecordViewAsync(string slug);
    Task<string?> GetRandomArticleSlugAsync();
    /// <summary>All saved article slugs + timestamps, for sitemap generation. No paging - callers should stream/limit as needed.</summary>
    Task<List<SavedArticleSummary>> GetAllArticlesForSitemapAsync();
}
