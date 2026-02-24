using InfoGen.Data;

namespace InfoGen.Services;

public interface IStripeService
{
    Task<string> CreateCheckoutSessionAsync(ApplicationUser user, string successUrl, string cancelUrl);
    Task<string> GetSubscriptionStatusAsync(ApplicationUser user);
    Task<string> CreatePortalSessionAsync(ApplicationUser user, string returnUrl);
}
