using InfoGen.Data;
using Microsoft.AspNetCore.Identity;
using Stripe;
using Stripe.Checkout;

namespace InfoGen.Services;

public class StripeService : IStripeService
{
    private readonly string _priceId;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<StripeService> _logger;
    private readonly HashSet<string> _complimentaryProEmails;

    public StripeService(IConfiguration configuration, UserManager<ApplicationUser> userManager, ILogger<StripeService> logger)
    {
        var secretKey = configuration["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey is not configured.");
        _priceId = configuration["Stripe:PriceId"]
            ?? throw new InvalidOperationException("Stripe:PriceId is not configured.");
        _userManager = userManager;
        _logger = logger;

        var list = configuration["Pro:ComplimentaryEmails"] ?? "";
        _complimentaryProEmails = new HashSet<string>(
            list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

        StripeConfiguration.ApiKey = secretKey;
    }

    public async Task<string> CreateCheckoutSessionAsync(ApplicationUser user, string successUrl, string cancelUrl)
    {
        if (string.IsNullOrEmpty(user.StripeCustomerId))
        {
            var customerService = new CustomerService();
            var customer = await customerService.CreateAsync(new CustomerCreateOptions
            {
                Email = user.Email,
                Metadata = new Dictionary<string, string> { ["UserId"] = user.Id }
            });
            user.StripeCustomerId = customer.Id;
            await _userManager.UpdateAsync(user);
            _logger.LogInformation("Created Stripe customer {CustomerId} for user {UserId}", customer.Id, user.Id);
        }

        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Customer = user.StripeCustomerId,
            Mode = "subscription",
            LineItems = [new SessionLineItemOptions { Price = _priceId, Quantity = 1 }],
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
        });

        return session.Url;
    }

    public async Task<string> GetSubscriptionStatusAsync(ApplicationUser user)
    {
        if (!string.IsNullOrEmpty(user.Email) && _complimentaryProEmails.Contains(user.Email))
            return "active";

        if (string.IsNullOrEmpty(user.StripeCustomerId))
            return "none";

        var subscriptionService = new SubscriptionService();
        var subscriptions = await subscriptionService.ListAsync(new SubscriptionListOptions
        {
            Customer = user.StripeCustomerId,
            Limit = 1,
        });

        if (subscriptions.Data.Count == 0)
            return "none";

        var sub = subscriptions.Data[0];
        return sub.Status switch
        {
            "active" or "trialing" => "active",
            "canceled" or "incomplete_expired" => "canceled",
            _ => sub.Status
        };
    }

    public async Task<string> CreatePortalSessionAsync(ApplicationUser user, string returnUrl)
    {
        if (string.IsNullOrEmpty(user.StripeCustomerId))
            throw new InvalidOperationException("User does not have a Stripe customer ID.");

        var portalService = new Stripe.BillingPortal.SessionService();
        var session = await portalService.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = user.StripeCustomerId,
            ReturnUrl = returnUrl,
        });

        return session.Url;
    }
}
