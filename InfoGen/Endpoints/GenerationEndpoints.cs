using InfoGen.Api;
using InfoGen.Data;
using InfoGen.Models;
using InfoGen.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace InfoGen.Endpoints;

public static class GenerationEndpoints
{
    public static IEndpointRouteBuilder MapGenerationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/generation").RequireAuthorization();

        group.MapPost("/text", async (
            GenerateTextRequest request,
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            IWikipediaService wikipedia,
            IGeminiService gemini,
            IUsageService usage,
            IMemoryCache cache) =>
        {
            var userId = userManager.GetUserId(context.User);
            if (userId is null) return Results.Unauthorized();

            // A purchased credit (if that's the funding source) is reserved atomically inside the gate,
            // BEFORE any expensive Gemini call, so concurrent requests can't share one credit.
            var (denyResult, funding) = await CheckGenerationAllowedAsync(context, userManager, usage, userId);
            if (denyResult is not null) return denyResult;

            var success = false;
            try
            {
                List<WikipediaPage> sourcePages;
                if (request.SourcePages is { Count: > 0 })
                {
                    var validated = new List<WikipediaPage>();
                    foreach (var page in request.SourcePages)
                    {
                        var fresh = await wikipedia.GetPageByTitleAsync(page.Title);
                        if (fresh is null)
                            return Results.BadRequest($"Could not find a valid Wikipedia article for '{page.Title}'.");
                        validated.Add(fresh);
                    }
                    sourcePages = validated;
                }
                else
                {
                    sourcePages = await wikipedia.GetRandomPagesAsync(4);
                }

                var article = await gemini.GenerateMashupArticleAsync(sourcePages, request.Tone, request.AdditionalPrompt, request.ReferenceLinks);

                // Only subscription/free-trial generations count against the monthly quota. Credit-funded
                // generations must NOT touch it - otherwise credits bought before subscribing would eat
                // into that month's subscription allowance.
                if (funding != GenerationFunding.Credit)
                    await usage.RecordGenerationAsync(userId);

                // Issue a session token proving this user's gate check already passed, so /image and
                // the save endpoint can trust it instead of re-checking (re-checking subscription/usage
                // in /image would wrongly block the image step of a free trial that /text just consumed;
                // and without requiring this token, POST /api/articles could be called directly with
                // fully fabricated content, bypassing generation entirely).
                var sessionToken = GenerationSessionCache.Issue(cache, userId);

                success = true;
                return Results.Ok(new GenerateTextResponse { Article = article, SourcePages = sourcePages, SessionToken = sessionToken });
            }
            finally
            {
                // If a credit was reserved but the generation didn't complete (invalid pages, Gemini
                // failure, etc.), give it back so the user isn't charged for nothing.
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

            if (!GenerationSessionCache.TryConsumeForImage(cache, request.SessionToken, userId))
            {
                return Results.BadRequest("Missing or expired generation session. Please generate the article text again before requesting an image.");
            }

            var imageDataUrl = await gemini.GenerateImageAsync(request.ImageDescription);
            return Results.Ok(new GenerateImageResponse { ImageDataUrl = imageDataUrl });
        });

        return endpoints;
    }

    private enum GenerationFunding { Subscription, Trial, Credit }

    /// <summary>Decides whether the user may generate and which source funds it. Priority: subscription
    /// monthly quota, then one-time free trial, then purchased credits. For the credit bucket this
    /// ATOMICALLY reserves the credit before returning - callers must refund it if the generation then
    /// fails. Subscription/Trial fundings only reserve monthly-quota bookkeeping (done by the caller).</summary>
    /// <returns>DenyResult is non-null when generation is blocked.</returns>
    private static async Task<(IResult? DenyResult, GenerationFunding Funding)> CheckGenerationAllowedAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        IUsageService usage,
        string userId)
    {
        var stripeService = context.RequestServices.GetService<IStripeService>();

        // No payment provider configured (e.g. local/dev): fall back to a plain monthly cap for all.
        if (stripeService is null)
        {
            if (await usage.CanGenerateAsync(userId))
                return (null, GenerationFunding.Subscription);
            return (Blocked(), default);
        }

        var user = await userManager.GetUserAsync(context.User);
        if (user is null) return (Results.Unauthorized(), default);

        // Bucket 1: active subscription with monthly quota (MonthlyLimit) still remaining.
        var status = await stripeService.GetSubscriptionStatusAsync(user);
        if (status == "active" && await usage.CanGenerateAsync(userId))
            return (null, GenerationFunding.Subscription);

        // Bucket 2: one-time free trial - never generated before.
        if (!await usage.HasEverGeneratedAsync(userId))
            return (null, GenerationFunding.Trial);

        // Bucket 3: purchased credits - reserve one atomically here so two concurrent requests can't
        // both pass on the same credit and each run a (billed) generation.
        if (await usage.TryReservePurchasedCreditAsync(userId))
            return (null, GenerationFunding.Credit);

        return (Blocked(), default);

        static IResult Blocked() =>
            Results.Json(new GenerationErrorResponse { Reason = "generation_unavailable" }, statusCode: StatusCodes.Status403Forbidden);
    }
}
