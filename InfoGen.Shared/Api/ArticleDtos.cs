using InfoGen.Models;
using InfoGen.Services;

namespace InfoGen.Api;

public class SaveArticleRequest
{
    public GeneratedArticle Article { get; set; } = new();
    public List<WikipediaPage> SourcePages { get; set; } = new();
    public List<ReferenceLink>? ReferenceLinks { get; set; }
    /// <summary>Proves this content came from a real /api/generation/text call for this user.</summary>
    public string SessionToken { get; set; } = "";
}

public class RandomArticleResponse
{
    public string? Slug { get; set; }
}
