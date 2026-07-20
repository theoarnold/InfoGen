using System.Security;
using System.Text;
using InfoGen.Services;

namespace InfoGen.Endpoints;

public static class SeoEndpoints
{
    public const string CachePolicy = "seo";

    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/sitemap.xml", async (HttpContext context, IConfiguration configuration, IArticleStorageService storage) =>
        {
            var baseUrl = ResolveBaseUrl(context, configuration);
            var articles = await storage.GetAllArticlesForSitemapAsync();

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");

            var escapedBaseUrl = SecurityElement.Escape(baseUrl);

            sb.Append("  <url>\n");
            sb.Append($"    <loc>{escapedBaseUrl}/</loc>\n");
            sb.Append("  </url>\n");

            // 50,000 URLs is the protocol cap; past it the whole document is rejected, not truncated.
            foreach (var article in articles.Take(50_000))
            {
                sb.Append("  <url>\n");
                sb.Append($"    <loc>{escapedBaseUrl}/wiki/{SecurityElement.Escape(article.Slug)}</loc>\n");
                sb.Append($"    <lastmod>{article.CreatedAt:yyyy-MM-dd}</lastmod>\n");
                sb.Append("  </url>\n");
            }

            sb.Append("</urlset>\n");

            return Results.Content(sb.ToString(), "application/xml", Encoding.UTF8);
        }).AllowAnonymous().CacheOutput(CachePolicy);

        endpoints.MapGet("/robots.txt", (HttpContext context, IConfiguration configuration) =>
        {
            var baseUrl = ResolveBaseUrl(context, configuration);
            var robots = $"""
                User-agent: *
                Disallow: /Account/
                Disallow: /api/
                Disallow: /generate
                Disallow: /random

                Sitemap: {baseUrl}/sitemap.xml
                """;

            return Results.Text(robots, "text/plain");
        }).AllowAnonymous().CacheOutput(CachePolicy);

        return endpoints;
    }

    /// <summary>Prefers configured "Site:BaseUrl" because Request.Host is attacker-supplied, and a
    /// spoofed Host header baked into sitemap.xml points search engines at someone else's domain.
    /// Host stays as the fallback for local dev; production should set Site:BaseUrl and AllowedHosts.</summary>
    private static string ResolveBaseUrl(HttpContext context, IConfiguration configuration)
    {
        var configured = configuration["Site:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        return $"{context.Request.Scheme}://{context.Request.Host}";
    }
}
