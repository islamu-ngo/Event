namespace Explore.Blazor.Client.Services;

public interface IImageContentClassifier
{
    string GetDefaultExtension(string contentType);

    int GetFileTypeId(string contentType);
}

public sealed class ImageContentClassifier : IImageContentClassifier
{
    public string GetDefaultExtension(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            _ => ".bin"
        };
    }

    public int GetFileTypeId(string contentType)
    {
        var normalized = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("image/", StringComparison.Ordinal))
        {
            return 1; // FileTypeEnum.Image
        }

        if (normalized.StartsWith("video/", StringComparison.Ordinal))
        {
            return 3; // FileTypeEnum.Video
        }

        if (normalized.StartsWith("audio/", StringComparison.Ordinal))
        {
            return 4; // FileTypeEnum.Audio
        }

        if (normalized.StartsWith("text/", StringComparison.Ordinal) ||
            normalized == "application/pdf")
        {
            return 2; // FileTypeEnum.Document
        }

        return 5; // FileTypeEnum.Other
    }
}
