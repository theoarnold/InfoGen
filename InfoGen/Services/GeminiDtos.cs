namespace InfoGen.Services;

// --- Request DTOs for image generation ---

public class GeminiImageRequest
{
    public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();
    public GeminiGenerationConfig? GenerationConfig { get; set; }
}

public class GeminiContent
{
    public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
}

public class GeminiPart
{
    public string? Text { get; set; }
}

public class GeminiGenerationConfig
{
    public string[] ResponseModalities { get; set; } = Array.Empty<string>();
    public GeminiImageConfig? ImageConfig { get; set; }
}

public class GeminiImageConfig
{
    public string? AspectRatio { get; set; }
}

// --- JSON response DTOs from Gemini structured output ---

public class GeminiArticleResponse
{
    public string? Title { get; set; }
    public string? ImageDescription { get; set; }
    public string? Intro { get; set; }
    public List<GeminiArticleSection>? Sections { get; set; }
    public List<GeminiInfoboxFact>? InfoboxFacts { get; set; }
}

public class GeminiArticleSection
{
    public string? Heading { get; set; }
    public string? Content { get; set; }
}

public class GeminiInfoboxFact
{
    public string? Label { get; set; }
    public string? Value { get; set; }
}
