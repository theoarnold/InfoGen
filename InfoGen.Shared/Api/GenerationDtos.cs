using InfoGen.Models;
using InfoGen.Services;

namespace InfoGen.Api;

public class GenerateTextRequest
{
    public List<WikipediaPage>? SourcePages { get; set; }
    public ArticleTone Tone { get; set; } = ArticleTone.Fun;
    public string? AdditionalPrompt { get; set; }
    public List<ReferenceLink>? ReferenceLinks { get; set; }

    /// <summary>From /api/generation/research. Optional - without it, research runs inline.</summary>
    public string? ResearchToken { get; set; }
}

public class ResearchRequest
{
    /// <summary>Null or empty means a randomised generation.</summary>
    public List<WikipediaPage>? SourcePages { get; set; }
}

public class ResearchResponse
{
    public string ResearchToken { get; set; } = "";

    /// <summary>Display only.</summary>
    public int FoundCount { get; set; }
}

public class GenerateTextResponse
{
    public GeneratedArticle Article { get; set; } = new();
    public List<WikipediaPage> SourcePages { get; set; } = new();

    /// <summary>Links the model wrote, resolved to slugs, so the preview can render them.</summary>
    public List<ReferenceLink> ReferenceLinks { get; set; } = new();

    public string SessionToken { get; set; } = "";
}

public class GenerateImageRequest
{
    public string SessionToken { get; set; } = "";
}

public class GenerateImageResponse
{
    public string? ImageDataUrl { get; set; }
}

/// <summary>Returned with 403 from /api/generation/text: "subscription_required" or "usage_limit_reached".</summary>
public class GenerationErrorResponse
{
    public string Reason { get; set; } = "";
}
