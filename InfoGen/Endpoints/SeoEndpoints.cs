using System.Text;
using InfoGen.Services;

namespace InfoGen.Endpoints;

public static class SeoEndpoints
{
    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/sitemap.xml", async (HttpContext context, IArticleStorageService storage) =>
        {
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            var articles = await storage.GetAllArticlesForSitemapAsync();

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");

            sb.Append("  <url>\n");
            sb.Append($"    <loc>{baseUrl}/</loc>\n");
            sb.Append("  </url>\n");

            foreach (var article in articles)
            {
                sb.Append("  <url>\n");
                sb.Append($"    <loc>{baseUrl}/wiki/{System.Net.WebUtility.HtmlEncode(article.Slug)}</loc>\n");
                sb.Append($"    <lastmod>{article.CreatedAt:yyyy-MM-dd}</lastmod>\n");
                sb.Append("  </url>\n");
            }

            sb.Append("</urlset>\n");

            return Results.Content(sb.ToString(), "application/xml");
        }).AllowAnonymous();

        endpoints.MapGet("/robots.txt", (HttpContext context) =>
        {
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            var robots = $"""
                User-agent: *
                Disallow: /Account/
                Disallow: /api/
                Disallow: /generate
                Disallow: /random

                Sitemap: {baseUrl}/sitemap.xml
                """;

            return Results.Text(robots, "text/plain");
        }).AllowAnonymous();

        return endpoints;
    }
}
