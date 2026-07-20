using InfoGen.Api;
using InfoGen.Data;
using InfoGen.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace InfoGen.Endpoints;

public static class ArticleEndpoints
{
    public static IEndpointRouteBuilder MapArticleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/articles");

        group.MapGet("/recent", async (IArticleStorageService storage, int count = 10) =>
        {
            var articles = await storage.GetRecentArticlesAsync(count);
            return Results.Ok(articles);
        }).AllowAnonymous();

        group.MapGet("/top-today", async (IArticleStorageService storage, int count = 10) =>
        {
            var articles = await storage.GetTopViewedTodayAsync(count);
            return Results.Ok(articles);
        }).AllowAnonymous();

        group.MapGet("/search", async (IArticleStorageService storage, string q, int skip = 0, int take = 15) =>
        {
            var results = await storage.SearchArticlesAsync(q, skip, take);
            return Results.Ok(results);
        }).AllowAnonymous();

        group.MapGet("/random", async (IArticleStorageService storage) =>
        {
            var slug = await storage.GetRandomArticleSlugAsync();
            return Results.Ok(new RandomArticleResponse { Slug = slug });
        }).AllowAnonymous();

        group.MapPost("/", async (
            SaveArticleRequest request,
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            IArticleStorageService storage,
            IMemoryCache cache) =>
        {
            var userId = userManager.GetUserId(context.User);
            if (userId is null) return Results.Unauthorized();

            // The article is read from the session, not the request body. The token is what identifies
            // which generation is being saved; the content itself never makes a round-trip through the
            // client, so there is nothing here for a caller to substitute.
            var session = GenerationSessionCache.TryConsumeForSave(cache, request.SessionToken, userId);
            if (session is null)
            {
                return Results.BadRequest("Missing or expired generation session. Please generate the article again before saving.");
            }

            // The image likewise comes from the session - it was produced by /api/generation/image on
            // this server, so the blob upload below can never be fed an arbitrary client payload.
            var article = session.Article;
            article.ImageDataUrl = session.ImageDataUrl;

            var result = await storage.SaveArticleAsync(article, session.SourcePages, userId, request.ReferenceLinks);
            return Results.Ok(result);
        }).RequireAuthorization();

        return endpoints;
    }
}
