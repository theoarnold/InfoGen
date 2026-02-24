namespace InfoGen.Entities;

public class MonthlyGenerationUsage
{
    public string UserId { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public int Count { get; set; }
}
