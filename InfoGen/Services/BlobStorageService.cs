using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace InfoGen.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(IConfiguration configuration, ILogger<BlobStorageService> logger)
    {
        _logger = logger;
        var connectionString = configuration["AzureBlobStorage:ConnectionString"]
            ?? throw new InvalidOperationException("AzureBlobStorage:ConnectionString is not configured.");
        var containerName = configuration["AzureBlobStorage:ContainerName"] ?? "article-images";

        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    private const int MaxImageBytes = 8 * 1024 * 1024;

    // The container is public, so an unrecognised type must be rejected rather than defaulted - the
    // Content-Type below is pinned from this table so a blob can't be served as HTML or script.
    private static readonly Dictionary<string, string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp"
    };

    // Input is the backend's own Gemini output, never a client upload. If that ever changes, this
    // needs real decode-based validation before the bytes reach a public container.
    public async Task<string> UploadImageAsync(string base64DataUrl)
    {
        var (mimeType, imageBytes) = ParseBase64DataUrl(base64DataUrl);

        if (!AllowedImageTypes.TryGetValue(mimeType, out var extension))
            throw new ArgumentException($"Unsupported image type '{mimeType}'.");

        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var blobName = $"{Guid.NewGuid()}{extension}";
        var blobClient = _containerClient.GetBlobClient(blobName);

        using var stream = new MemoryStream(imageBytes);
        await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = mimeType.ToLowerInvariant() });

        _logger.LogInformation("Uploaded image to blob: {BlobUri}", blobClient.Uri);
        return blobClient.Uri.ToString();
    }

    private static (string mimeType, byte[] data) ParseBase64DataUrl(string dataUrl)
    {
        var commaIndex = dataUrl.IndexOf(',');
        if (commaIndex < 0)
            throw new ArgumentException("Invalid base64 data URL format.");

        var header = dataUrl[..commaIndex];
        var base64 = dataUrl[(commaIndex + 1)..];

        if (!header.StartsWith("data:"))
            throw new ArgumentException("Invalid base64 data URL format.");

        var semicolonIndex = header.IndexOf(';');
        if (semicolonIndex < 0)
            throw new ArgumentException("Data URL is missing its media type.");

        var mimeType = header["data:".Length..semicolonIndex].Trim();
        if (mimeType.Length == 0)
            throw new ArgumentException("Data URL is missing its media type.");

        byte[] data;
        try
        {
            data = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Image data is not valid base64.", ex);
        }

        if (data.Length > MaxImageBytes)
            throw new ArgumentException("Image exceeds the maximum allowed size.");

        return (mimeType, data);
    }
}
