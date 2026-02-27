using InfoGen.Models;

namespace InfoGen.Services;

public interface IWikipediaService
{
    Task<List<WikipediaPage>> GetRandomPagesAsync(int count = 4);
    Task<WikipediaPage?> GetPageByTitleAsync(string title);
    /// <summary>Returns Wikipedia article titles matching the search query (for autocomplete).</summary>
    Task<List<string>> SearchTitlesAsync(string query, int limit = 10);
}
