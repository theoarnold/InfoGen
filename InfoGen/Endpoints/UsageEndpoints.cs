using InfoGen.Api;
using InfoGen.Data;
using InfoGen.Services;
using Microsoft.AspNetCore.Identity;

namespace InfoGen.Endpoints;

public static class UsageEndpoints
{
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/usage/status", async (
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            IUsageService usage) =>
        {
            var userId = userManager.GetUserId(context.User);
            if (userId is null) return Results.Unauthorized();

            var isSubscribed = false;
            var stripeService = context.RequestServices.GetService<IStripeService>();
            if (stripeService is not null)
            {
                var user = await userManager.GetUserAsync(context.User);
                if (user is not null)
                    isSubscribed = await stripeService.GetSubscriptionStatusAsync(user) == "active";
            }

            return Results.Ok(new UsageStatusResponse
            {
                IsSubscribed = isSubscribed,
                RemainingThisMonth = await usage.GetRemainingGenerationsAsync(userId),
                PurchasedCredits = await usage.GetPurchasedCreditsAsync(userId),
                TrialAvailable = !await usage.HasEverGeneratedAsync(userId)
            });
        }).RequireAuthorization();

        return endpoints;
    }
}
