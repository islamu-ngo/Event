using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public interface IImagePreviewService
{
    Task<string> GenerateLocalPreviewAsync(IBrowserFile file, long maxFileSize);

    string GenerateLocalPreviewFromBytes(FileUploadData fileData);
}

public sealed class ImagePreviewService(ILogger<ImagePreviewService> logger) : IImagePreviewService
{
    public async Task<string> GenerateLocalPreviewAsync(IBrowserFile file, long maxFileSize)
    {
        try
        {
            var resizedImage = await file.RequestImageFileAsync(file.ContentType, 400, 400);
            using var stream = resizedImage.OpenReadStream(maxFileSize);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var bytes = memoryStream.ToArray();
            return $"data:{file.ContentType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating preview");
            return string.Empty;
        }
    }

    public string GenerateLocalPreviewFromBytes(FileUploadData fileData)
    {
        if (fileData == null || fileData.Content.Length == 0)
        {
            logger.LogWarning("GenerateLocalPreviewFromBytes called with null or empty file data");
            return string.Empty;
        }

        try
        {
            return $"data:{fileData.ContentType};base64,{Convert.ToBase64String(fileData.Content.Span)}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating preview from bytes");
            return string.Empty;
        }
    }
}
