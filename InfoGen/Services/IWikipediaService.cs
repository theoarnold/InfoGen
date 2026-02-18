using InfoGen.Models;

namespace InfoGen.Services;

public interface IWikipediaService
{
    Task<List<WikipediaPage>> GetRandomPagesAsync(int count = 4);
}
