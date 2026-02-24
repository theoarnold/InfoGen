using InfoGen.Data;
using InfoGen.Entities;

namespace InfoGen.Services;

public class UsageService : IUsageService
{
    public const int MonthlyLimit = 100;

    private readonly InfoGenDbContext _db;

    public UsageService(InfoGenDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CanGenerateAsync(ApplicationUser user)
    {
        var remaining = await GetRemainingGenerationsAsync(user);
        return remaining > 0;
    }

    public async Task<int> GetRemainingGenerationsAsync(ApplicationUser user)
    {
        var (year, month) = GetCurrentYearMonth();
        var usage = await _db.MonthlyGenerationUsage.FindAsync(user.Id, year, month);
        var count = usage?.Count ?? 0;
        return Math.Max(0, MonthlyLimit - count);
    }

    public async Task RecordGenerationAsync(ApplicationUser user)
    {
        var (year, month) = GetCurrentYearMonth();
        var usage = await _db.MonthlyGenerationUsage.FindAsync(user.Id, year, month);
        if (usage == null)
        {
            usage = new MonthlyGenerationUsage
            {
                UserId = user.Id,
                Year = year,
                Month = month,
                Count = 0
            };
            _db.MonthlyGenerationUsage.Add(usage);
        }
        usage.Count++;
        await _db.SaveChangesAsync();
    }

    private static (int Year, int Month) GetCurrentYearMonth()
    {
        var now = DateTime.UtcNow;
        return (now.Year, now.Month);
    }
}
