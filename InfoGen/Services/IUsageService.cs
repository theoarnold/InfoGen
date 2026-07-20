namespace InfoGen.Services;

public interface IUsageService
{
    Task<bool> CanGenerateAsync(string userId);

    Task<int> GetRemainingGenerationsAsync(string userId);

    /// <summary>Call after a successful generation.</summary>
    Task RecordGenerationAsync(string userId);

    /// <summary>Any month. Gates the one-time free trial.</summary>
    Task<bool> HasEverGeneratedAsync(string userId);

    Task<int> GetPurchasedCreditsAsync(string userId);

    /// <summary>Idempotent: returns false without changing the balance if this checkout session was
    /// already processed.</summary>
    Task<bool> AddPurchasedCreditsAsync(string userId, int credits, string stripeCheckoutSessionId);

    /// <summary>Atomic - the single point preventing concurrent requests double-spending one credit.
    /// True only if a credit was actually decremented.</summary>
    Task<bool> TryReservePurchasedCreditAsync(string userId);

    /// <summary>Returns a reserved credit when the generation it was reserved for failed.</summary>
    Task RefundPurchasedCreditAsync(string userId);
}
