using InfoGen.Data;
using Microsoft.EntityFrameworkCore;

namespace InfoGen.Services;

/// <summary>Keeps the locally cached <see cref="ApplicationUser.IsSubscribed"/> flag in step with Stripe.
/// Stripe remains the source of truth - this exists purely so read paths (notably public article pages)
/// can style subscriber names without making a network call per request.</summary>
public interface ISubscriptionStateService
{
    /// <summary>Updates the flag for whoever owns this Stripe customer id. Returns false if no user matches.</summary>
    Task<bool> SetByStripeCustomerIdAsync(string stripeCustomerId, bool isSubscribed);

    /// <summary>Updates the flag for a known user id. Writes only when the value actually changes.</summary>
    Task SetAsync(string userId, bool isSubscribed);
}

public class SubscriptionStateService : ISubscriptionStateService
{
    private readonly InfoGenDbContext _dbContext;
    private readonly ILogger<SubscriptionStateService> _logger;

    public SubscriptionStateService(InfoGenDbContext dbContext, ILogger<SubscriptionStateService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> SetByStripeCustomerIdAsync(string stripeCustomerId, bool isSubscribed)
    {
        if (string.IsNullOrEmpty(stripeCustomerId)) return false;

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == stripeCustomerId);
        if (user is null)
        {
            _logger.LogWarning("No user found for Stripe customer {CustomerId}; subscription flag not updated.", stripeCustomerId);
            return false;
        }

        await ApplyAsync(user, isSubscribed);
        return true;
    }

    public async Task SetAsync(string userId, bool isSubscribed)
    {
        if (string.IsNullOrEmpty(userId)) return;
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return;
        await ApplyAsync(user, isSubscribed);
    }

    private async Task ApplyAsync(ApplicationUser user, bool isSubscribed)
    {
        // SubscriptionCheckedAt is refreshed even when the value is unchanged, so it records the last
        // time we genuinely heard from Stripe rather than the last time the answer flipped.
        var changed = user.IsSubscribed != isSubscribed;
        user.IsSubscribed = isSubscribed;
        user.SubscriptionCheckedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        if (changed)
            _logger.LogInformation("User {UserId} subscription flag set to {IsSubscribed}.", user.Id, isSubscribed);
    }
}
