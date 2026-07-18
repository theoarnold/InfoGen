namespace InfoGen.Services;

public interface IUsageService
{
    /// <summary>Returns true if the user has not reached the monthly generation limit.</summary>
    Task<bool> CanGenerateAsync(string userId);

    /// <summary>Returns how many generations the user has left this month.</summary>
    Task<int> GetRemainingGenerationsAsync(string userId);

    /// <summary>Records one generation for the current month. Call after a successful generation.</summary>
    Task RecordGenerationAsync(string userId);

    /// <summary>Returns true if this account has ever completed a generation (any month). Used to gate the one-time free trial.</summary>
    Task<bool> HasEverGeneratedAsync(string userId);

    /// <summary>Current balance of one-time purchased generation credits.</summary>
    Task<int> GetPurchasedCreditsAsync(string userId);

    /// <summary>Idempotently credits a completed Stripe purchase. Returns false (no balance change) if this
    /// checkout session was already processed; otherwise records the purchase and increments the balance.</summary>
    Task<bool> AddPurchasedCreditsAsync(string userId, int credits, string stripeCheckoutSessionId);

    /// <summary>Atomically reserves one purchased credit. Returns true only if a credit was actually
    /// decremented - this is the single point that prevents concurrent requests double-spending one credit.</summary>
    Task<bool> TryReservePurchasedCreditAsync(string userId);

    /// <summary>Returns one previously-reserved credit to the balance (used when a generation fails after reservation).</summary>
    Task RefundPurchasedCreditAsync(string userId);
}
