using InfoGen.Models;

namespace InfoGen.Services;

public class SavedArticleSummary
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalViews { get; set; }
    public int DailyViews { get; set; }
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
    public int TotalViews { get; set; }

    /// <summary>Identity user id of the creator (server-only; used to look up subscriber status). Never rendered to the client.</summary>
    public string? CreatorUserId { get; set; }

    /// <summary>True when the creator has an active subscription (gold author styling).</summary>
    public bool CreatorIsSubscribed { get; set; }
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

/// <summary>One entry in the catalogue of existing articles handed to the model at generation time.
/// Deliberately title + summary only - never article bodies, which would blow up the prompt.</summary>
public class ArticleCatalogueEntry
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Summary { get; set; }
}
