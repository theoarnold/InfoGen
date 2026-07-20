using InfoGen.Models;
using InfoGen.Services;

namespace InfoGen.Api;

public class SaveArticleRequest
{
    public List<ReferenceLink>? ReferenceLinks { get; set; }

    /// <summary>Identifies which generation to save. The article, its source pages and its image are
    /// all held server-side against this token - deliberately not sent by the client, so the content
    /// that gets published is provably the content that was generated.</summary>
    public string SessionToken { get; set; } = "";
}

public class RandomArticleResponse
{
    public string? Slug { get; set; }
}
