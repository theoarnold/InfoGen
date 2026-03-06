using InfoGen.Models;

namespace InfoGen.Services;

public interface IArticleStorageService
{
    Task<SavedArticleSummary> SaveArticleAsync(GeneratedArticle article, List<WikipediaPage> sourcePages, string? createdByUserId = null, List<ReferenceLink>? referenceLinks = null);
    Task<List<SavedArticleSummary>> SearchArticlesAsync(string query, int skip = 0, int take = 15);
    Task<SavedArticleDetail?> GetArticleBySlugAsync(string slug);
    Task<List<SavedArticleSummary>> GetRecentArticlesAsync(int count = 10);
    Task<string?> GetRandomArticleSlugAsync();
}

public class SavedArticleSummary
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SavedArticleDetail
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string ImageDescription { get; set; } = "";
    public string? ImageUrl { get; set; }
    public List<ArticleSection> Sections { get; set; } = new();
    public List<InfoboxFact> InfoboxFacts { get; set; } = new();
    public List<SourcePageInfo> SourcePages { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public string? CreatorDisplayName { get; set; }
    public List<ReferenceLink> ReferenceLinks { get; set; } = new();
}

public class SourcePageInfo
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Extract { get; set; } = "";
}

/// <summary>Existing Ficipedia article to reference in generated body (becomes a blue link).</summary>
public class ReferenceLink
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
}
