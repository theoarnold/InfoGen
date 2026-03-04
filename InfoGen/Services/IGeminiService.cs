using InfoGen.Models;

namespace InfoGen.Services;

public interface IGeminiService
{
    Task<GeneratedArticle> GenerateMashupArticleAsync(List<WikipediaPage> pages, ArticleTone tone = ArticleTone.Fun, string? additionalPrompt = null);
    Task<string?> GenerateImageAsync(string description);
}
