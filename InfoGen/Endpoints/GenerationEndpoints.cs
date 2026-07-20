using InfoGen.Api;
using InfoGen.Data;
using InfoGen.Models;
using InfoGen.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace InfoGen.Endpoints;

public static class GenerationEndpoints
{
    // Backstop for missed webhooks, not the primary refresh path - hence hours, not seconds.
    private static readonly TimeSpan SubscriptionFlagTtl = TimeSpan.FromHours(6);

    public static IEndpointRouteBuilder MapGenerationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/generation").RequireAuthorization();

        // Deliberately reserves no credit - it only checks the user *could* generate, so researching
        // and then abandoning doesn't charge anyone. /text still does the real gate.
        group.MapPost("/research", async (
            ResearchRequest request,
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            IWikipediaService wikipedia,
            IGeminiService gemini,
            IUsageService usage,
            IArticleStorageService storage,
            ISubscriptionStateService subscriptionState,
            IMemoryCache cache) =>
        {
            var userId = userManager.GetUserId(context.User);
            if (userId is null) return Results.Unauthorized();

            if (!await IsEligibleToGenerateAsync(context, userManager, usage, subscriptionState, userId))
                return Results.Json(new GenerationErrorResponse { Reason = "generation_unavailable" }, statusCode: StatusCodes.Status403Forbidden);

            var sourcePages = await ResolveSourcePagesAsync(request.SourcePages, wikipedia);
            if (sourcePages is null)
                return Results.BadRequest("One or more Wikipedia articles could not be found.");

            // Best-effort: a research failure must not block generating.
            List<ArticleCatalogueEntry> discovered = [];
            try
            {
                discovered = await gemini.FindLinkCandidatesAsync(
                    sourcePages, (query, limit) => storage.SearchCatalogueAsync(query, limit));
            }
            catch (Exception)
            {
                // Logged inside the service; swallowed here so the flow continues without links.
            }

            var researchToken = ResearchCache.Store(cache, userId, sourcePages, discovered);
            return Results.Ok(new ResearchResponse { ResearchToken = researchToken, FoundCount = discovered.Count });
        }).RequireRateLimiting(RateLimitPolicies.Research);

        group.MapPost("/text", async (
            GenerateTextRequest request,
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            IWikipediaService wikipedia,
            IGeminiService gemini,
            IUsageService usage,
            IArticleStorageService storage,
            IMemoryCache cache) =>
        {
            var userId = userManager.GetUserId(context.User);
            if (userId is null) return Results.Unauthorized();

            // Reserves the credit atomically inside the gate, before any expensive Gemini call.
            var (denyResult, funding) = await CheckGenerationAllowedAsync(context, userManager, usage, userId);
            if (denyResult is not null) return denyResult;

            var success = false;
            try
            {
                // Prefer the work /research already did; falling back keeps this endpoint usable alone.
                var research = ResearchCache.TryTake(cache, request.ResearchToken, userId);

                List<WikipediaPage> sourcePages;
                List<ArticleCatalogueEntry> discovered;

                if (research is not null)
                {
                    (sourcePages, discovered) = research.Value;
                }
                else
                {
                    var resolved = await ResolveSourcePagesAsync(request.SourcePages, wikipedia);
                    if (resolved is null)
                        return Results.BadRequest("One or more Wikipedia articles could not be found.");
                    sourcePages = resolved;

                    discovered = [];
                    try
                    {
                        discovered = await gemini.FindLinkCandidatesAsync(
                            sourcePages, (query, limit) => storage.SearchCatalogueAsync(query, limit));
                    }
                    catch (Exception) { /* logged in the service; generate without links */ }
                }

                var article = await gemini.GenerateMashupArticleAsync(
                    sourcePages, request.Tone, request.AdditionalPrompt, request.ReferenceLinks, discovered);

                // Credit-funded generations must not touch the monthly quota, or credits bought before
                // subscribing would eat into that month's subscription allowance.
                if (funding != GenerationFunding.Credit)
                    await usage.RecordGenerationAsync(userId);

                // The token also proves this user's gate check passed, so /image needn't re-check -
                // that would wrongly block the image step of a free trial /text just consumed.
                var sessionToken = GenerationSessionCache.Issue(cache, userId, article, sourcePages);

                // Resolved here because the client only knows the references the user picked by hand,
                // not the ones the model found.
                var resolvedLinks = await storage.ResolveReferenceLinksAsync(article);

                success = true;
                return Results.Ok(new GenerateTextResponse
                {
                    Article = article,
                    SourcePages = sourcePages,
                    SessionToken = sessionToken,
                    ReferenceLinks = resolvedLinks
                });
            }
            finally
            {
                // Give back a reserved credit if the generation didn't complete.
                if (funding == GenerationFunding.Credit && !success)
                    await usage.RefundPurchasedCreditAsync(userId);
            }
        });

        group.MapPost("/image", async (
            GenerateImageRequest request,
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            IGeminiService gemini,
            IMemoryCache cache) =>
        {
            var userId = userManager.GetUserId(context.User);
            if (userId is null) return Results.Unauthorized();

            // The caption comes from the stored article: letting the client supply it would turn this
            // into a free general-purpose image generator.
            if (!GenerationSessionCache.TryBeginImage(cache, request.SessionToken, userId, out var imageDescription))
            {
                return Results.BadRequest("Missing or expired generation session. Please generate the article text again before requesting an image.");
            }

            var imageDataUrl = await gemini.GenerateImageAsync(imageDescription);

            GenerationSessionCache.AttachImage(cache, request.SessionToken, userId, imageDataUrl);

            return Results.Ok(new GenerateImageResponse { ImageDataUrl = imageDataUrl });
        });

        return endpoints;
    }

    /// <summary>Validates user-supplied pages in parallel, or fetches random ones. Null means a
    /// requested page doesn't exist.</summary>
    private static async Task<List<WikipediaPage>?> ResolveSourcePagesAsync(
        List<WikipediaPage>? requested, IWikipediaService wikipedia)
    {
        if (requested is not { Count: > 0 })
            return await wikipedia.GetRandomPagesAsync(4);

        var titles = requested.Select(p => p.Title).ToList();
        var fetched = await Task.WhenAll(titles.Select(t => wikipedia.GetPageByTitleAsync(t)));

        if (fetched.Any(p => p is null)) return null;
        return fetched.Select(p => p!).ToList();
    }

    /// <summary>Read-only eligibility check for /research. Unlike the real gate it reserves nothing.</summary>
    private static async Task<bool> IsEligibleToGenerateAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        IUsageService usage,
        ISubscriptionStateService subscriptionState,
        string userId)
    {
        var stripeService = context.RequestServices.GetService<IStripeService>();
        if (stripeService is null)
            return await usage.CanGenerateAsync(userId);

        var user = await userManager.GetUserAsync(context.User);
        if (user is null) return false;

        // Webhooks keep the flag current but do get missed, and a webhook-only flag would then stay
        // wrong forever - indefinitely granting access to someone who cancelled. Re-ask Stripe when the
        // answer is unknown (accounts predating this column) or stale; otherwise use the local copy.
        var isSubscribed = user.IsSubscribed;
        var checkedAt = user.SubscriptionCheckedAt;
        if (checkedAt is null || DateTime.UtcNow - checkedAt.Value > SubscriptionFlagTtl)
        {
            isSubscribed = await stripeService.GetSubscriptionStatusAsync(user) == "active";
            await subscriptionState.SetAsync(user.Id, isSubscribed);
        }

        if (isSubscribed && await usage.CanGenerateAsync(userId)) return true;
        if (!await usage.HasEverGeneratedAsync(userId)) return true;
        return await usage.GetPurchasedCreditsAsync(userId) > 0;
    }

    private enum GenerationFunding { Subscription, Trial, Credit }

    /// <summary>Priority: subscription monthly quota, then one-time free trial, then purchased credits.
    /// The credit bucket is reserved atomically here - callers must refund it if generation fails.</summary>
    /// <returns>DenyResult is non-null when generation is blocked.</returns>
    private static async Task<(IResult? DenyResult, GenerationFunding Funding)> CheckGenerationAllowedAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        IUsageService usage,
        string userId)
    {
        var stripeService = context.RequestServices.GetService<IStripeService>();

        // No payment provider configured (local/dev): plain monthly cap for everyone.
        if (stripeService is null)
        {
            if (await usage.CanGenerateAsync(userId))
                return (null, GenerationFunding.Subscription);
            return (Blocked(), default);
        }

        var user = await userManager.GetUserAsync(context.User);
        if (user is null) return (Results.Unauthorized(), default);

        var status = await stripeService.GetSubscriptionStatusAsync(user);
        if (status == "active" && await usage.CanGenerateAsync(userId))
            return (null, GenerationFunding.Subscription);

        if (!await usage.HasEverGeneratedAsync(userId))
            return (null, GenerationFunding.Trial);

        // Reserved atomically so two concurrent requests can't both pass on the same credit.
        if (await usage.TryReservePurchasedCreditAsync(userId))
            return (null, GenerationFunding.Credit);

        return (Blocked(), default);

        static IResult Blocked() =>
            Results.Json(new GenerationErrorResponse { Reason = "generation_unavailable" }, statusCode: StatusCodes.Status403Forbidden);
    }
}
