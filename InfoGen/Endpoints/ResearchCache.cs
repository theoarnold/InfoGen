using InfoGen.Models;
using InfoGen.Services;
using Microsoft.Extensions.Caching.Memory;

namespace InfoGen.Endpoints;

/// <summary>
/// Holds the output of /api/generation/research until /api/generation/text runs. Kept server-side
/// because a browser round-trip would let a caller inject fabricated "existing articles" into the
/// generation prompt. Also carries the resolved source pages so Wikipedia isn't fetched twice.
/// </summary>
internal static class ResearchCache
{
    private const string KeyPrefix = "gen-research:";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private sealed record Entry(string UserId, List<WikipediaPage> SourcePages, List<ArticleCatalogueEntry> Discovered);

    public static string Store(IMemoryCache cache, string userId, List<WikipediaPage> sourcePages, List<ArticleCatalogueEntry> discovered)
    {
        var token = Guid.NewGuid().ToString("N");
        cache.Set(KeyPrefix + token, new Entry(userId, sourcePages, discovered), Lifetime);
        return token;
    }

    /// <summary>Null when the token is missing, expired, or belongs to someone else - callers then
    /// research inline instead.</summary>
    public static (List<WikipediaPage> SourcePages, List<ArticleCatalogueEntry> Discovered)? TryTake(
        IMemoryCache cache, string? token, string userId)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var key = KeyPrefix + token;
        if (!cache.TryGetValue(key, out Entry? entry) || entry is null) return null;
        if (entry.UserId != userId) return null;

        cache.Remove(key);
        return (entry.SourcePages, entry.Discovered);
    }
}
