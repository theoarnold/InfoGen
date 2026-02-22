using InfoGen.Models;

namespace InfoGen.Services;

public interface IWikipediaService
{
    Task<List<WikipediaPage>> GetRandomPagesAsync(int count = 4);
    Task<WikipediaPage?> GetPageByTitleAsync(string title);
}
