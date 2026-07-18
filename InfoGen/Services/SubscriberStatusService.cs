using InfoGen.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace InfoGen.Services;

public interface ISubscriberStatusService
{
    /// <summary>True when the user currently has an active subscription. Cached briefly so that article
    /// page views don't hit Stripe on every request.</summary>
    Task<bool> IsSubscribedAsync(string userId);
}

public class SubscriberStatusService : ISubscriberStatusService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly IMemoryCache _cache;
    private readonly UserManager<ApplicationUser> _userManager;
    // Stripe is only registered when configured, so resolve it lazily rather than as a hard dependency.
    private readonly IStripeService? _stripeService;

    public SubscriberStatusService(
        IMemoryCache cache,
        UserManager<ApplicationUser> userManager,
        IServiceProvider serviceProvider)
    {
        _cache = cache;
        _userManager = userManager;
        _stripeService = serviceProvider.GetService<IStripeService>();
    }

    public async Task<bool> IsSubscribedAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId) || _stripeService is null)
            return false;

        return await _cache.GetOrCreateAsync($"subscriber-status:{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return false;
            var status = await _stripeService.GetSubscriptionStatusAsync(user);
            return status == "active";
        });
    }
}
