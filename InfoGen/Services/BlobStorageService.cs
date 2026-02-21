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

    public async Task<string> UploadImageAsync(string base64DataUrl)
    {
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var (mimeType, imageBytes) = ParseBase64DataUrl(base64DataUrl);
        var extension = mimeType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png"
        };

        var blobName = $"{Guid.NewGuid()}{extension}";
        var blobClient = _containerClient.GetBlobClient(blobName);

        using var stream = new MemoryStream(imageBytes);
        await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = mimeType });

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

        var mimeType = "image/png";
        if (header.StartsWith("data:") && header.Contains(';'))
        {
            mimeType = header["data:".Length..header.IndexOf(';')];
        }

        return (mimeType, Convert.FromBase64String(base64));
    }
}
