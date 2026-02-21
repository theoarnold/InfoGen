namespace InfoGen.Entities;

public class SavedArticleEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string ImageDescription { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string SectionsJson { get; set; } = "[]";
    public string InfoboxFactsJson { get; set; } = "[]";
    public string SourcePagesJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
}
