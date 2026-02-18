using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using InfoGen.Models;

namespace InfoGen.Services;

public class GeminiService : IGeminiService
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
    /// Returns structured JSON via Gemini's responseSchema feature.
    /// </summary>
    public async Task<GeneratedArticle> GenerateMashupArticleAsync(List<WikipediaPage> pages)
    {
        var prompt = BuildMashupPrompt(pages);

        _logger.LogInformation("Sending mashup prompt to Gemini ({Model}) with JSON schema...", _textModel);

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
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new Dictionary<string, object>
                    {
                        ["title"] = new { type = "STRING", description = "Article title. If biographical, use only the person's name." },
                        ["imageDescription"] = new { type = "STRING", description = "Short Wikipedia-style caption: subject and setting in one phrase." },
                        ["intro"] = new { type = "STRING", description = "Lead section: 1-2 paragraphs with no heading. Use \\n\\n between paragraphs." },
                        ["sections"] = new
                        {
                            type = "ARRAY",
                            description = "2-4 article sections after the intro.",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new Dictionary<string, object>
                                {
                                    ["heading"] = new { type = "STRING", description = "Section heading (e.g. Early life, Career, History)." },
                                    ["content"] = new { type = "STRING", description = "Section body text. Use \\n\\n between paragraphs." }
                                },
                                required = new[] { "heading", "content" }
                            }
                        },
                        ["infoboxFacts"] = new
                        {
                            type = "ARRAY",
                            description = "4-6 key-value pairs for the Wikipedia infobox sidebar (e.g. Born, Nationality, Occupation).",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new Dictionary<string, object>
                                {
                                    ["label"] = new { type = "STRING", description = "Fact label (e.g. Born, Location, Genre)." },
                                    ["value"] = new { type = "STRING", description = "Fact value (e.g. 12 March 1985, Scotland, Jazz fusion)." }
                                },
                                required = new[] { "label", "value" }
                            }
                        }
                    },
                    required = new[] { "title", "imageDescription", "intro", "sections", "infoboxFacts" }
                }
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_textModel}:generateContent?key={_apiKey}";

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var response = await _httpClient.PostAsJsonAsync(url, requestBody, jsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gemini text API error: {Status} - {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Gemini API returned {response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>();
        var text = responseJson.GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        _logger.LogInformation("Received article JSON ({Length} chars)", text.Length);

        var articleJson = JsonSerializer.Deserialize<GeminiArticleResponse>(text, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new GeminiArticleResponse();

        return MapToGeneratedArticle(articleJson);
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

    /// <summary>Maps the deserialized JSON response to the GeneratedArticle model.</summary>
    private static GeneratedArticle MapToGeneratedArticle(GeminiArticleResponse response)
    {
        var article = new GeneratedArticle
        {
            Title = string.IsNullOrWhiteSpace(response.Title) ? "Untitled Article" : response.Title.Trim(),
            ImageDescription = string.IsNullOrWhiteSpace(response.ImageDescription) ? response.Title ?? "" : response.ImageDescription.Trim()
        };

        // Build sections: intro first (null heading), then named sections
        if (!string.IsNullOrWhiteSpace(response.Intro))
        {
            article.Sections.Add(new ArticleSection
            {
                Heading = null,
                Paragraphs = SplitParagraphs(response.Intro)
            });
        }

        if (response.Sections != null)
        {
            foreach (var s in response.Sections)
            {
                article.Sections.Add(new ArticleSection
                {
                    Heading = s.Heading?.Trim(),
                    Paragraphs = SplitParagraphs(s.Content)
                });
            }
        }

        if (response.InfoboxFacts != null)
        {
            foreach (var fact in response.InfoboxFacts)
            {
                if (!string.IsNullOrWhiteSpace(fact.Label) && !string.IsNullOrWhiteSpace(fact.Value))
                {
                    article.InfoboxFacts.Add(new InfoboxFact
                    {
                        Label = fact.Label.Trim(),
                        Value = fact.Value.Trim()
                    });
                }
            }
        }

        return article;
    }

    /// <summary>Splits a block of text into paragraphs by double newlines.</summary>
    private static List<string> SplitParagraphs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace("\n", " ").Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }
}

