namespace InfoGen.Models;

public class GeneratedArticle
{
    public string Title { get; set; } = "";
    public string ImageDescription { get; set; } = "";
    public string? ImageDataUrl { get; set; }
    public List<ArticleSection> Sections { get; set; } = new();
    public List<InfoboxFact> InfoboxFacts { get; set; } = new();
}

public class ArticleSection
{
    /// <summary>Section heading, or null for the intro/lead.</summary>
    public string? Heading { get; set; }
    public List<string> Paragraphs { get; set; } = new();
}

public class InfoboxFact
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}
