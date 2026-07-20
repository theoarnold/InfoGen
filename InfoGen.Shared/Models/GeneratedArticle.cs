namespace InfoGen.Models;

public class GeneratedArticle
{
    public string Title { get; set; } = "";
    public string ImageDescription { get; set; } = "";

    /// <summary>One-line description used in the catalogue shown to the model when generating later
    /// articles, so it can decide what is worth linking to.</summary>
    public string? Summary { get; set; }

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
