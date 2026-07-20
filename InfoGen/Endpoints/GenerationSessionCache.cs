using InfoGen.Models;
using Microsoft.Extensions.Caching.Memory;

namespace InfoGen.Endpoints;

/// <summary>
/// Custody of a single generation, from /api/generation/text through /image to POST /api/articles.
///
/// The token isn't proof that a generation happened - it IS the article. Content is held server-side
/// and the save endpoint reads it from the token, so the client never gets to say what is published.
/// Taking the article from the request body instead would let a caller generate once and then submit
/// entirely different content to appear on a public page under their name.
/// </summary>
internal static class GenerationSessionCache
{
    private const string KeyPrefix = "gen-session:";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    internal sealed record Session(
        string UserId,
        GeneratedArticle Article,
        List<WikipediaPage> SourcePages,
        bool ImageGenerated,
        string? ImageDataUrl);

    public static string Issue(IMemoryCache cache, string userId, GeneratedArticle article, List<WikipediaPage> sourcePages)
    {
        var token = Guid.NewGuid().ToString("N");
        cache.Set(KeyPrefix + token, new Session(userId, article, sourcePages, ImageGenerated: false, ImageDataUrl: null), Lifetime);
        return token;
    }

    /// <summary>Marks the token used for the image step so it can't be replayed for a second free
    /// image, while leaving it valid for the save that follows.</summary>
    public static bool TryBeginImage(IMemoryCache cache, string? token, string userId, out string imageDescription)
    {
        imageDescription = "";
        if (string.IsNullOrEmpty(token)) return false;
        var key = KeyPrefix + token;
        if (!cache.TryGetValue(key, out Session? session) || session is null) return false;
        if (session.UserId != userId || session.ImageGenerated) return false;

        cache.Set(key, session with { ImageGenerated = true }, Lifetime);
        imageDescription = session.Article.ImageDescription;
        return true;
    }

    public static void AttachImage(IMemoryCache cache, string? token, string userId, string? imageDataUrl)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(imageDataUrl)) return;
        var key = KeyPrefix + token;
        if (!cache.TryGetValue(key, out Session? session) || session is null) return;
        if (session.UserId != userId) return;

        cache.Set(key, session with { ImageDataUrl = imageDataUrl }, Lifetime);
    }

    /// <summary>Consumes the token - single-use for the whole flow.</summary>
    public static Session? TryConsumeForSave(IMemoryCache cache, string? token, string userId)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var key = KeyPrefix + token;
        if (!cache.TryGetValue(key, out Session? session) || session is null) return null;
        if (session.UserId != userId) return null;

        cache.Remove(key);
        return session;
    }
}
