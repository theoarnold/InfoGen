namespace InfoGen.Api;

public class UsageStatusResponse
{
    public bool IsSubscribed { get; set; }
    /// <summary>Remaining generations in the monthly subscription quota (only meaningful when subscribed).</summary>
    public int RemainingThisMonth { get; set; }
    public int PurchasedCredits { get; set; }
    /// <summary>True if the one-time free trial generation is still available.</summary>
    public bool TrialAvailable { get; set; }

    /// <summary>Total generations the user can do right now, across all sources.</summary>
    public int TotalAvailable =>
        (IsSubscribed ? RemainingThisMonth : 0) + PurchasedCredits + (TrialAvailable ? 1 : 0);
}
