namespace InfoGen.Services;

public interface IBlobStorageService
{
    Task<string> UploadImageAsync(string base64DataUrl);
}
