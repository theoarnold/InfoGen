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

            // Requires a token from a real /api/generation/text call - otherwise this endpoint would
            // accept fully client-fabricated content with no generation, subscription, or usage check
            // involved at all.
            if (!GenerationSessionCache.TryConsumeForSave(cache, request.SessionToken, userId))
            {
                return Results.BadRequest("Missing or expired generation session. Please generate the article again before saving.");
            }

            var result = await storage.SaveArticleAsync(request.Article, request.SourcePages, userId, request.ReferenceLinks);
            return Results.Ok(result);
        }).RequireAuthorization();

        return endpoints;
    }
}
