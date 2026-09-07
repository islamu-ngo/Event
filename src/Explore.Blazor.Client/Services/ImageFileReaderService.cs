using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public interface IImageFileReaderService
{
    Task<FileUploadData?> ReadFileAsync(IBrowserFile file, long maxFileSize);
}

public sealed class ImageFileReaderService(ILogger<ImageFileReaderService> logger) : IImageFileReaderService
{
    public async Task<FileUploadData?> ReadFileAsync(IBrowserFile file, long maxFileSize)
    {
        if (file == null)
        {
            logger.LogWarning("ReadFileAsync called with null file");
            return null;
        }

        try
        {
            logger.LogInformation(
                "Reading selected image into memory. SizeBucket={SizeBucket}, ContentTypeBucket={ContentTypeBucket}",
                ImageUploadClientPolicy.GetSizeBucket(file.Size),
                ImageUploadClientPolicy.GetContentTypeBucket(file.ContentType));

            if (file.Size > maxFileSize)
            {
                logger.LogWarning(
                    "Selected image exceeds client read limit. SizeBucket={SizeBucket}, MaxSizeBucket={MaxSizeBucket}",
                    ImageUploadClientPolicy.GetSizeBucket(file.Size),
                    ImageUploadClientPolicy.GetSizeBucket(maxFileSize));
                return null;
            }

            await using var stream = file.OpenReadStream(maxAllowedSize: maxFileSize);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var bytes = memoryStream.ToArray();
            if (!ImageUploadClientPolicy.TryValidateImageFile(
                    file.Name,
                    file.ContentType,
                    bytes,
                    out var contentType))
            {
                logger.LogWarning(
                    "Selected image declaration did not match its extension and detected bytes. ContentTypeBucket={ContentTypeBucket}",
                    ImageUploadClientPolicy.GetContentTypeBucket(file.ContentType));
                return null;
            }

            var safeFileName = ImageUploadClientPolicy.BuildSafeFileName(file.Name, contentType);

            logger.LogDebug(
                "Successfully read selected image into memory. SizeBucket={SizeBucket}",
                ImageUploadClientPolicy.GetSizeBucket(bytes.Length));

            return new FileUploadData(bytes, safeFileName, contentType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Selected image could not be read into memory. FailureType={FailureType}, SizeBucket={SizeBucket}",
                ImageUploadClientPolicy.GetFailureType(ex),
                ImageUploadClientPolicy.GetSizeBucket(file.Size));
            return null;
        }
    }
}
