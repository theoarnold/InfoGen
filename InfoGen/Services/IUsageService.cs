using InfoGen.Data;

namespace InfoGen.Services;

public interface IUsageService
{
    /// <summary>Returns true if the user has not reached the monthly generation limit.</summary>
    Task<bool> CanGenerateAsync(ApplicationUser user);

    /// <summary>Returns how many generations the user has left this month.</summary>
    Task<int> GetRemainingGenerationsAsync(ApplicationUser user);

    /// <summary>Records one generation for the current month. Call after a successful generation.</summary>
    Task RecordGenerationAsync(ApplicationUser user);
}
