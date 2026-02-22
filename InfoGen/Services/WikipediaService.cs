using System.Text.Json;
using InfoGen.Models;

namespace InfoGen.Services;

public class WikipediaService : IWikipediaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WikipediaService> _logger;

    public WikipediaService(HttpClient httpClient, ILogger<WikipediaService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<WikipediaPage>> GetRandomPagesAsync(int count = 4)
    {
        // Fetch extra random pages to increase variety (e.g. more chance of including people)
        var fetchCount = Math.Max(count + 2, 6);
        var randomUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=random&rnlimit={fetchCount}&rnnamespace=0&format=json&origin=*";
        var randomResponse = await _httpClient.GetFromJsonAsync<JsonElement>(randomUrl);

        var randomPages = randomResponse.GetProperty("query").GetProperty("random");
        var pageIds = new List<int>();
        foreach (var page in randomPages.EnumerateArray())
        {
            pageIds.Add(page.GetProperty("id").GetInt32());
        }

        _logger.LogInformation("Fetched {Count} random page IDs: {Ids}", pageIds.Count, string.Join(", ", pageIds));

        // Step 2: Get extracts for those pages
        var idsString = string.Join("|", pageIds);
        var extractUrl = $"https://en.wikipedia.org/w/api.php?action=query&pageids={idsString}&prop=extracts&exintro=true&explaintext=true&format=json&origin=*";
        var extractResponse = await _httpClient.GetFromJsonAsync<JsonElement>(extractUrl);

        var pages = extractResponse.GetProperty("query").GetProperty("pages");
        var result = new List<WikipediaPage>();

        foreach (var page in pages.EnumerateObject())
        {
            var title = page.Value.GetProperty("title").GetString() ?? "";
            var extract = page.Value.TryGetProperty("extract", out var ext)
                ? ext.GetString() ?? ""
                : "";
            var pageId = page.Value.GetProperty("pageid").GetInt32();

            // Skip pages with very short or empty extracts
            if (!string.IsNullOrWhiteSpace(extract) && extract.Length > 50)
            {
                result.Add(new WikipediaPage
                {
                    Title = title,
                    Extract = extract,
                    PageId = pageId
                });
            }
            else
            {
                _logger.LogWarning("Skipping page '{Title}' - extract too short ({Length} chars)", title, extract.Length);
            }
        }

        // If we didn't get enough good pages, fetch more
        if (result.Count < count)
        {
            _logger.LogInformation("Only got {Count} good pages, fetching more...", result.Count);
            var additional = await GetRandomPagesAsync(count - result.Count);
            result.AddRange(additional);
        }

        return result.Take(count).ToList();
    }

    public async Task<WikipediaPage?> GetPageByTitleAsync(string title)
    {
        var encodedTitle = Uri.EscapeDataString(title.Trim());
        var url = $"https://en.wikipedia.org/w/api.php?action=query&titles={encodedTitle}&prop=extracts&exintro=true&explaintext=true&format=json&origin=*&redirects=1";
        var response = await _httpClient.GetFromJsonAsync<JsonElement>(url);

        var pages = response.GetProperty("query").GetProperty("pages");
        foreach (var page in pages.EnumerateObject())
        {
            if (page.Name == "-1") return null;

            var pageTitle = page.Value.GetProperty("title").GetString() ?? "";
            var extract = page.Value.TryGetProperty("extract", out var ext) ? ext.GetString() ?? "" : "";
            var pageId = page.Value.GetProperty("pageid").GetInt32();

            if (string.IsNullOrWhiteSpace(extract) || extract.Length <= 50)
                return null;

            return new WikipediaPage
            {
                Title = pageTitle,
                Extract = extract,
                PageId = pageId
            };
        }

        return null;
    }
}

