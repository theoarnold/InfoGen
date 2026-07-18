namespace InfoGen.Entities;

/// <summary>One row per successfully-credited Stripe checkout session. Exists for idempotency
/// (Stripe can redeliver the same webhook event) and a basic purchase audit trail.</summary>
public class GenerationCreditPurchase
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string StripeCheckoutSessionId { get; set; } = "";
    public int Credits { get; set; }
    public DateTime PurchasedAt { get; set; }
}
