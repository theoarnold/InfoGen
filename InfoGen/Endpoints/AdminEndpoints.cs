using System.Security.Cryptography;
using System.Text;
using InfoGen.Data;
using InfoGen.Services;
using Microsoft.EntityFrameworkCore;

namespace InfoGen.Endpoints;

/// <summary>
/// One-off maintenance operations. Every endpoint here is inert unless "Admin:SyncKey" is configured -
/// set the key in Azure App Settings, run the job, then clear it again.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Backfills ApplicationUser.IsSubscribed from Stripe. The column was added defaulting to false
        // for every existing account, so current subscribers show as non-subscribers until a webhook
        // happens to fire for them.
        endpoints.MapPost("/admin/sync-subscriptions", async (
            HttpContext context,
            IConfiguration configuration,
            InfoGenDbContext db,
            ISubscriptionStateService subscriptionState,
            ILogger<AdminLogCategory> logger) =>
        {
            var expected = configuration["Admin:SyncKey"];
            if (string.IsNullOrEmpty(expected))
                return Results.NotFound(); // disabled unless explicitly configured

            // Header, never the query string - query strings land verbatim in access logs.
            var key = context.Request.Headers["X-Admin-Sync-Key"].ToString();
            if (string.IsNullOrEmpty(key) || !FixedTimeEquals(key, expected))
                return Results.Unauthorized();

            var stripeService = context.RequestServices.GetService<IStripeService>();
            if (stripeService is null)
                return Results.BadRequest("Stripe is not configured on this instance.");

            // Every account, not just those with a Stripe customer id: complimentary-pro accounts are
            // recognised by email and may never have had one. GetSubscriptionStatusAsync returns without
            // a network call for users with neither, so the wider sweep costs no extra Stripe requests.
            var users = await db.Users.ToListAsync();

            int subscribed = 0, notSubscribed = 0, failed = 0;

            foreach (var user in users)
            {
                try
                {
                    var status = await stripeService.GetSubscriptionStatusAsync(user);
                    var isActive = status == "active";
                    await subscriptionState.SetAsync(user.Id, isActive);
                    if (isActive) subscribed++; else notSubscribed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogWarning(ex, "Subscription sync failed for user {UserId}.", user.Id);
                }
            }

            logger.LogInformation("Subscription sync complete: {Subscribed} subscribed, {NotSubscribed} not, {Failed} failed.",
                subscribed, notSubscribed, failed);

            return Results.Ok(new
            {
                checkedUsers = users.Count,
                subscribed,
                notSubscribed,
                failed
            });
        }).AllowAnonymous();

        return endpoints;
    }

    // Hashed first so the comparison is length- and content-independent, keeping response timing from
    // revealing the key a character at a time.
    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));

    private sealed class AdminLogCategory { }
}
