using InfoGen.Models;
using InfoGen.Services;

namespace InfoGen.Api;

public class GenerateTextRequest
{
    public List<WikipediaPage>? SourcePages { get; set; }
    public ArticleTone Tone { get; set; } = ArticleTone.Fun;
    public string? AdditionalPrompt { get; set; }
    public List<ReferenceLink>? ReferenceLinks { get; set; }
}

public class GenerateTextResponse
{
    public GeneratedArticle Article { get; set; } = new();
    public List<WikipediaPage> SourcePages { get; set; } = new();
    /// <summary>Proves to /api/generation/image that this user's subscription/usage gate was already
    /// checked for this generation - the image step trusts this token instead of re-checking, since
    /// re-checking independently would wrongly reject the image step of a just-granted free trial.</summary>
    public string SessionToken { get; set; } = "";
}

public class GenerateImageRequest
{
    public string ImageDescription { get; set; } = "";
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
