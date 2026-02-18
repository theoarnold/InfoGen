using InfoGen.Models;

namespace InfoGen.Services;

public interface IGeminiService
{
    Task<GeneratedArticle> GenerateMashupArticleAsync(List<WikipediaPage> pages);
    Task<string?> GenerateImageAsync(string description);
}
