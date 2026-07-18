using Microsoft.Extensions.Caching.Memory;

namespace InfoGen.Endpoints;

/// <summary>
/// Proves that /api/generation/image and POST /api/articles are being called as part of a
/// generation that actually passed the subscription/usage gate in /api/generation/text - without
/// this, a client could skip straight to /image (unlimited free paid-image calls) or straight to
/// /articles (publishing arbitrary hand-crafted content with no generation involved at all).
/// One token covers the whole text -> image -> save flow; image generation is single-use per
/// token (can't be replayed for extra free images), save consumes the token entirely.
/// </summary>
internal static class GenerationSessionCache
{
    private const string KeyPrefix = "gen-session:";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private sealed record Session(string UserId, bool ImageGenerated);

    public static string Issue(IMemoryCache cache, string userId)
    {
        var token = Guid.NewGuid().ToString("N");
        cache.Set(KeyPrefix + token, new Session(userId, ImageGenerated: false), Lifetime);
        return token;
    }

    /// <summary>Validates the token for the image step and marks it used, so it can't be replayed
    /// for a second free image - but leaves it valid for the subsequent save step.</summary>
    public static bool TryConsumeForImage(IMemoryCache cache, string? token, string userId)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var key = KeyPrefix + token;
        if (!cache.TryGetValue(key, out Session? session) || session is null) return false;
        if (session.UserId != userId || session.ImageGenerated) return false;

        cache.Set(key, session with { ImageGenerated = true }, Lifetime);
        return true;
    }

    /// <summary>Validates the token for the save step and removes it - single-use for the whole flow.</summary>
    public static bool TryConsumeForSave(IMemoryCache cache, string? token, string userId)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var key = KeyPrefix + token;
        if (!cache.TryGetValue(key, out Session? session) || session is null) return false;
        if (session.UserId != userId) return false;

        cache.Remove(key);
        return true;
    }
}
