using InfoGen.Data;
using InfoGen.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoGen.Services;

public class UsageService : IUsageService
{
    public const int MonthlyLimit = 100;

    private readonly InfoGenDbContext _db;

    public UsageService(InfoGenDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CanGenerateAsync(string userId)
    {
        var remaining = await GetRemainingGenerationsAsync(userId);
        return remaining > 0;
    }

    public async Task<int> GetRemainingGenerationsAsync(string userId)
    {
        var (year, month) = GetCurrentYearMonth();
        var usage = await _db.MonthlyGenerationUsage.FindAsync(userId, year, month);
        var count = usage?.Count ?? 0;
        return Math.Max(0, MonthlyLimit - count);
    }

    public async Task RecordGenerationAsync(string userId)
    {
        var (year, month) = GetCurrentYearMonth();
        var usage = await _db.MonthlyGenerationUsage.FindAsync(userId, year, month);
        if (usage == null)
        {
            usage = new MonthlyGenerationUsage
            {
                UserId = userId,
                Year = year,
                Month = month,
                Count = 0
            };
            _db.MonthlyGenerationUsage.Add(usage);
        }
        usage.Count++;
        await _db.SaveChangesAsync();
    }

    public async Task<bool> HasEverGeneratedAsync(string userId)
    {
        return await _db.MonthlyGenerationUsage.AnyAsync(u => u.UserId == userId && u.Count > 0);
    }

    public async Task<int> GetPurchasedCreditsAsync(string userId)
    {
        var user = await _db.Users.FindAsync(userId);
        return user?.PurchasedGenerationCredits ?? 0;
    }

    public async Task<bool> AddPurchasedCreditsAsync(string userId, int credits, string stripeCheckoutSessionId)
    {
        // Idempotency: a redelivered webhook for the same session must not credit twice.
        var alreadyProcessed = await _db.GenerationCreditPurchases
            .AnyAsync(p => p.StripeCheckoutSessionId == stripeCheckoutSessionId);
        if (alreadyProcessed)
            return false;

        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return false;

        _db.GenerationCreditPurchases.Add(new GenerationCreditPurchase
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StripeCheckoutSessionId = stripeCheckoutSessionId,
            Credits = credits,
            PurchasedAt = DateTime.UtcNow
        });
        user.PurchasedGenerationCredits += credits;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> TryReservePurchasedCreditAsync(string userId)
    {
        // Single atomic UPDATE ... WHERE credits > 0: the database serializes concurrent callers,
        // so exactly one request can claim the last credit - closing the double-spend window that a
        // read-then-write would leave open across the slow generation call.
        var rows = await _db.Users
            .Where(u => u.Id == userId && u.PurchasedGenerationCredits > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PurchasedGenerationCredits, u => u.PurchasedGenerationCredits - 1));
        return rows == 1;
    }

    public async Task RefundPurchasedCreditAsync(string userId)
    {
        await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PurchasedGenerationCredits, u => u.PurchasedGenerationCredits + 1));
    }

    private static (int Year, int Month) GetCurrentYearMonth()
    {
        var now = DateTime.UtcNow;
        return (now.Year, now.Month);
    }
}
