using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfoGen.Services;

public class GeminiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiService> _logger;
    private readonly string _apiKey;
    private readonly string _textModel;
    private readonly string _imageModel;

    public GeminiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"]
            ?? throw new InvalidOperationException("Gemini:ApiKey is not configured in appsettings.json");
        _textModel = configuration["Gemini:TextModel"] ?? "gemini-2.0-flash";
        _imageModel = configuration["Gemini:ImageModel"] ?? "gemini-2.0-flash-exp-image-generation";
    }

    /// <summary>
    /// Uses Gemini to generate a fake Wikipedia article from 4 source pages.
    /// </summary>
    public async Task<GeneratedArticle> GenerateMashupArticleAsync(List<WikipediaPage> pages)
    {
        var prompt = BuildMashupPrompt(pages);

        _logger.LogInformation("Sending mashup prompt to Gemini ({Model})...", _textModel);

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_textModel}:generateContent?key={_apiKey}";
        var response = await _httpClient.PostAsJsonAsync(url, requestBody);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gemini text API error: {Status} - {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Gemini API returned {response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var text = json.GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";

        _logger.LogInformation("Received article text ({Length} chars)", text.Length);

        return ParseGeneratedArticle(text);
    }

    /// <summary>
    /// Uses Nano Banana (Gemini image generation) to create an image for the article.
    /// </summary>
    public async Task<string?> GenerateImageAsync(string description)
    {
        var prompt = $"Generate a realistic, high-quality image that could serve as the main photograph for a Wikipedia article about: {description}. " +
                     "The image should look like a real encyclopedic photograph or illustration. No text overlays.";

        _logger.LogInformation("Sending image generation prompt to Nano Banana ({Model})...", _imageModel);

        var requestBody = new GeminiImageRequest
        {
            Contents = new[]
            {
                new GeminiContent
                {
                    Parts = new[]
                    {
                        new GeminiPart { Text = prompt }
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                ResponseModalities = new[] { "IMAGE", "TEXT" }
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_imageModel}:generateContent?key={_apiKey}";

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var response = await _httpClient.PostAsJsonAsync(url, requestBody, jsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gemini image API error: {Status} - {Body}", response.StatusCode, errorBody);
            return null; // Don't throw - article can still display without image
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        if (!json.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            _logger.LogWarning("No candidates in Gemini image response");
            return null;
        }

        var parts = candidates[0]
            .GetProperty("content")
            .GetProperty("parts");

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("inlineData", out var inlineData))
            {
                var mimeType = inlineData.GetProperty("mimeType").GetString();
                var data = inlineData.GetProperty("data").GetString();
                _logger.LogInformation("Received image ({MimeType}, {Length} bytes base64)", mimeType, data?.Length ?? 0);
                return $"data:{mimeType};base64,{data}";
            }
        }

        _logger.LogWarning("No image data found in Gemini response");
        return null;
    }

    private static string BuildMashupPrompt(List<WikipediaPage> pages)
    {
        var template = LoadMashupPromptTemplate();
        var sources = new StringBuilder();
        for (int i = 0; i < pages.Count; i++)
        {
            sources.AppendLine($"--- Source {i + 1}: \"{pages[i].Title}\" ---");
            sources.AppendLine(pages[i].Extract);
            sources.AppendLine();
        }
        return template.Replace("{{SOURCES}}", sources.ToString().TrimEnd());
    }

    private static string LoadMashupPromptTemplate()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Prompts", "MashupPrompt.txt");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Mashup prompt template not found. Expected: {path}");
        return File.ReadAllText(path);
    }

    private static GeneratedArticle ParseGeneratedArticle(string text)
    {
        var article = new GeneratedArticle();

        var lines = text.Split('\n');
        var articleStarted = false;
        var articleLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("TITLE:", StringComparison.OrdinalIgnoreCase))
            {
                article.Title = trimmed["TITLE:".Length..].Trim().Trim('*', '#');
                article.Title = StripTitleSubtitle(article.Title);
            }
            else if (trimmed.StartsWith("IMAGE_DESCRIPTION:", StringComparison.OrdinalIgnoreCase))
            {
                article.ImageDescription = trimmed["IMAGE_DESCRIPTION:".Length..].Trim();
            }
            else if (trimmed.StartsWith("ARTICLE:", StringComparison.OrdinalIgnoreCase))
            {
                articleStarted = true;
                var remainder = trimmed["ARTICLE:".Length..].Trim();
                if (!string.IsNullOrEmpty(remainder))
                    articleLines.Add(remainder);
            }
            else if (articleStarted)
            {
                articleLines.Add(line);
            }
        }

        article.Content = string.Join("\n", articleLines).Trim();
        article.Sections = ParseSections(articleLines);

        if (string.IsNullOrEmpty(article.Title))
            article.Title = "Untitled Article";
        if (string.IsNullOrEmpty(article.Content))
            article.Content = text; // Fallback: use full response
        if (string.IsNullOrEmpty(article.ImageDescription))
            article.ImageDescription = article.Title;

        return article;
    }

    /// <summary>Remove subtitle from person-style titles (e.g. "Name: The Lost Soundtrack" -> "Name").</summary>
    private static string StripTitleSubtitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;
        var lastColon = title.LastIndexOf(':');
        if (lastColon > 0 && lastColon < title.Length - 1)
        {
            var after = title[(lastColon + 1)..].Trim();
            if (after.Length > 0 && !after.StartsWith("(")) return title[..lastColon].Trim();
        }
        var dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex > 0)
            return title[..dashIndex].Trim();
        return title;
    }

    /// <summary>Parses article lines into intro + sections with == Heading == format.</summary>
    private static List<ArticleSection> ParseSections(List<string> articleLines)
    {
        var sections = new List<ArticleSection>();
        var currentParagraphs = new List<string>();
        string? currentHeading = null;
        var currentParagraph = new List<string>();

        void FlushParagraph()
        {
            if (currentParagraph.Count == 0) return;
            var text = string.Join(" ", currentParagraph).Trim();
            if (!string.IsNullOrEmpty(text))
                currentParagraphs.Add(text);
            currentParagraph.Clear();
        }

        void FlushSection()
        {
            FlushParagraph();
            if (currentParagraphs.Count > 0 || currentHeading != null)
            {
                sections.Add(new ArticleSection { Heading = currentHeading, Paragraphs = new List<string>(currentParagraphs) });
                currentParagraphs.Clear();
            }
        }

        foreach (var line in articleLines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("==") && trimmed.EndsWith("=="))
            {
                FlushSection();
                currentHeading = trimmed.Trim('=').Trim();
                continue;
            }
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushParagraph();
                continue;
            }
            currentParagraph.Add(trimmed);
        }

        FlushSection();

        if (sections.Count == 0)
        {
            var fallback = string.Join("\n", articleLines)
                .Split(new[] { "\n\n" }, StringSplitOptions.None)
                .Select(p => p.Trim().Replace("\n", " "))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (fallback.Count > 0)
                sections.Add(new ArticleSection { Heading = null, Paragraphs = fallback });
        }

        return sections;
    }
}

// Request DTOs for proper JSON serialization of the image generation request
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
}

// Result model
public class GeneratedArticle
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string ImageDescription { get; set; } = "";
    public string? ImageDataUrl { get; set; }
    /// <summary>Parsed sections: intro (null heading) plus subheadings and their paragraphs.</summary>
    public List<ArticleSection> Sections { get; set; } = new();
}

public class ArticleSection
{
    /// <summary>Section heading, or null for the intro/lead.</summary>
    public string? Heading { get; set; }
    public List<string> Paragraphs { get; set; } = new();
}

