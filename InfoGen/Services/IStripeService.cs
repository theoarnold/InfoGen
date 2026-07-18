using InfoGen.Data;

namespace InfoGen.Services;

public interface IStripeService
{
    Task<string> CreateCheckoutSessionAsync(ApplicationUser user, string successUrl, string cancelUrl);
    Task<string> GetSubscriptionStatusAsync(ApplicationUser user);
    Task<string> CreatePortalSessionAsync(ApplicationUser user, string returnUrl);

    /// <summary>Creates a one-time (payment mode) checkout session for a generation credit pack.
    /// Throws if packSize is not a configured pack.</summary>
    Task<string> CreateCreditPackCheckoutSessionAsync(ApplicationUser user, int packSize, string successUrl, string cancelUrl);
}
