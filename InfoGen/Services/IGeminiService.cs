using InfoGen.Models;

namespace InfoGen.Services;

public interface IGeminiService
{
    /// <summary>Lets the model search existing articles (via the search_articles tool) for ones worth
    /// linking from what it is about to write. Separate from generation so it can be its own step.</summary>
    /// <param name="searchArticles">Backs the tool. Passed in rather than injected so this service
    /// stays free of database concerns.</param>
    Task<List<ArticleCatalogueEntry>> FindLinkCandidatesAsync(
        List<WikipediaPage> pages,
        Func<string, int, Task<List<ArticleCatalogueEntry>>> searchArticles);

    /// <param name="referenceLinks">Articles the user explicitly picked - the model is told to prefer these.</param>
    /// <param name="discovered">Articles found by <see cref="FindLinkCandidatesAsync"/>, offered to the
    /// model as link targets.</param>
    Task<GeneratedArticle> GenerateMashupArticleAsync(
        List<WikipediaPage> pages,
        ArticleTone tone = ArticleTone.Fun,
        string? additionalPrompt = null,
        List<ReferenceLink>? referenceLinks = null,
        List<ArticleCatalogueEntry>? discovered = null);
    Task<string?> GenerateImageAsync(string description);
}
