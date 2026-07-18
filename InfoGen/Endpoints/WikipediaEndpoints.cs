using InfoGen.Services;

namespace InfoGen.Endpoints;

public static class WikipediaEndpoints
{
    public static IEndpointRouteBuilder MapWikipediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/wikipedia").AllowAnonymous();

        group.MapGet("/search-titles", async (IWikipediaService wikipedia, string q, int limit = 10) =>
        {
            var results = await wikipedia.SearchTitlesAsync(q, limit);
            return Results.Ok(results);
        });

        group.MapGet("/page", async (IWikipediaService wikipedia, string title) =>
        {
            var page = await wikipedia.GetPageByTitleAsync(title);
            return page is not null ? Results.Ok(page) : Results.NotFound();
        });

        return endpoints;
    }
}
