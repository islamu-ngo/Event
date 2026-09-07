using Explore.Blazor.Client.Services.Http;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Holds file data read from IBrowserFile for stable transfer across async operations.
/// Reading file bytes immediately prevents stream exhaustion issues in Blazor WASM.
/// </summary>
public sealed record FileUploadData
{
    public FileUploadData(ReadOnlyMemory<byte> Content, string FileName, string ContentType)
    {
        this.Content = Content.ToArray();
        this.FileName = FileName;
        this.ContentType = ContentType;
    }

    public ReadOnlyMemory<byte> Content { get; }
    public string FileName { get; }
    public string ContentType { get; }
    public long Size => Content.Length;
}

/// <summary>
/// Service for handling image storage operations.
/// </summary>
public interface IImageStorageService
{
    /// <summary>
    /// Reads a browser file into memory as FileUploadData.
    /// IMPORTANT: Call this immediately after file selection to prevent stream exhaustion.
    /// </summary>
    Task<FileUploadData?> ReadFileAsync(IBrowserFile file, long maxFileSize = 10 * 1024 * 1024);

    /// <summary>
    /// Get a server-issued upload session for uploading an image.
    /// </summary>
    Task<ImageUploadTarget?> GetUploadUrlAsync(string fileName, string contentType, long? expectedSizeBytes = null);

    /// <summary>
    /// Upload image bytes and create a StorageObject record.
    /// Preferred method for reliable WASM uploads.
    /// </summary>
    Task<ImageUploadResult?> UploadAndCreateRecordFromBytesAsync(FileUploadData fileData);

    /// <summary>
    /// Get a metadata-backed public image URL.
    /// </summary>
    Task<string?> GetImageUrlAsync(string imageKey);

    /// <summary>
    /// Get a metadata-backed public image URL by storage object ID.
    /// </summary>
    Task<string?> GetPresignedUrlByIdAsync(Guid storageObjectId);

    /// <summary>
    /// Delete an image from storage.
    /// </summary>
    Task<bool> DeleteImageAsync(string imageKey);

    /// <summary>
    /// Generate a preview URL from a browser file (local preview before upload).
    /// </summary>
    Task<string> GenerateLocalPreviewAsync(IBrowserFile file, long maxFileSize = 5 * 1024 * 1024);

    /// <summary>
    /// Generate a preview URL from file bytes.
    /// </summary>
    string GenerateLocalPreviewFromBytes(FileUploadData fileData);
}

public sealed class ImageUploadTarget
{
    public string UploadUrl { get; set; } = string.Empty;
    public string UploadSessionId { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string ViewUrl { get; set; } = string.Empty;
    public int ExpiresInMinutes { get; set; }
}

public sealed class BffStorageUploadSessionResponse
{
    public string UploadSessionId { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string ViewUrl { get; set; } = string.Empty;
    public int ExpiresInMinutes { get; set; }
}

public sealed class BffStorageUploadProxyResponse
{
    public Guid StorageObjectId { get; set; }
    public string ViewUrl { get; set; } = string.Empty;
    public string ContentUrl { get; set; } = string.Empty;
}

public sealed class BffStorageUploadSessionRequest
{
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long ExpectedSizeBytes { get; set; }
}

public class ImageUploadResult
{
    public Guid StorageObjectId { get; set; }
    public string ViewUrl { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Implementation of image storage service.
/// </summary>
public class ImageStorageService : IImageStorageService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ImageStorageService> _logger;
    private readonly BffClient? _bffClient;
    private readonly IApiClientExecutor _apiClientExecutor;
    private readonly IImageFileReaderService _fileReader;
    private readonly IImagePreviewService _previewService;
    private readonly IImageUploadClient _uploadClient;
    private readonly IStorageObjectUrlResolver _storageObjectUrlResolver;
    private const long DefaultMaxFileSize = 10 * 1024 * 1024; // 10MB

    public ImageStorageService(
        IHttpClientFactory httpClientFactory,
        ILogger<ImageStorageService> logger,
        BffClient? bffClient = null,
        IApiClientExecutor? apiClientExecutor = null,
        IImageFileReaderService? fileReader = null,
        IImagePreviewService? previewService = null,
        IImageUploadClient? uploadClient = null,
        IStorageObjectUrlResolver? storageObjectUrlResolver = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bffClient = bffClient;
        _apiClientExecutor = apiClientExecutor ?? new ApiClientExecutor();
        _fileReader = fileReader ?? new ImageFileReaderService(NullLogger<ImageFileReaderService>.Instance);
        _previewService = previewService ?? new ImagePreviewService(NullLogger<ImagePreviewService>.Instance);
        _uploadClient = uploadClient ?? new ImageUploadClient(
            _httpClientFactory,
            NullLogger<ImageUploadClient>.Instance,
            _bffClient,
            _apiClientExecutor);
        _storageObjectUrlResolver = storageObjectUrlResolver ?? new StorageObjectUrlResolver();
    }

    /// <inheritdoc />
    public async Task<FileUploadData?> ReadFileAsync(IBrowserFile file, long maxFileSize = DefaultMaxFileSize)
    {
        return await _fileReader.ReadFileAsync(file, maxFileSize);
    }

    /// <inheritdoc />
    public async Task<ImageUploadTarget?> GetUploadUrlAsync(string fileName, string contentType, long? expectedSizeBytes = null)
    {
        return await _uploadClient.GetUploadUrlAsync(fileName, contentType, expectedSizeBytes);
    }

    /// <inheritdoc />
    public async Task<ImageUploadResult?> UploadAndCreateRecordFromBytesAsync(FileUploadData fileData)
    {
        if (fileData == null || fileData.Content.Length == 0)
        {
            _logger.LogWarning("UploadAndCreateRecordFromBytesAsync called with null or empty file data");
            return new ImageUploadResult
            {
                Success = false,
                ErrorMessage = ImageUploadClientPolicy.NoImageDataMessage
            };
        }

        try
        {
            _logger.LogInformation(
                "Starting byte-based image upload. SizeBucket={SizeBucket}, ContentTypeBucket={ContentTypeBucket}",
                ImageUploadClientPolicy.GetSizeBucket(fileData.Size),
                ImageUploadClientPolicy.GetContentTypeBucket(fileData.ContentType));

            var uploadResponse = await GetUploadUrlAsync(fileData.FileName, fileData.ContentType, fileData.Size);
            if (uploadResponse == null)
            {
                _logger.LogWarning("Failed to get upload session for selected image.");
                return new ImageUploadResult
                {
                    Success = false,
                    ErrorMessage = ImageUploadClientPolicy.UploadSessionUnavailableMessage
                };
            }

            _logger.LogDebug("Got upload session response for selected image.");

            if (!string.IsNullOrWhiteSpace(uploadResponse.UploadSessionId))
            {
                var bffUploadResult = await _uploadClient.UploadViaBffProxyAsync(uploadResponse.UploadSessionId, fileData);
                if (bffUploadResult?.Success == true)
                {
                    return bffUploadResult;
                }

                return new ImageUploadResult
                {
                    Success = false,
                    ErrorMessage = ImageUploadClientPolicy.ToUserSafeUploadError(bffUploadResult?.ErrorMessage)
                };
            }

            _logger.LogWarning("Upload session response for selected image did not include a BFF upload session.");
            return new ImageUploadResult
            {
                Success = false,
                ErrorMessage = ImageUploadClientPolicy.UploadSessionUnavailableMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Unexpected error during selected image upload. FailureType={FailureType}",
                ImageUploadClientPolicy.GetFailureType(ex));
            return new ImageUploadResult
            {
                Success = false,
                ErrorMessage = ImageUploadClientPolicy.GenericUploadFailureMessage
            };
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetImageUrlAsync(string imageKey)
    {
        if (string.IsNullOrEmpty(imageKey))
        {
            return null;
        }

        try
        {
            var resolvedUrl = _storageObjectUrlResolver.ResolvePublicImageUrl(imageKey);
            if (!string.IsNullOrEmpty(resolvedUrl))
            {
                return resolvedUrl;
            }

            _logger.LogWarning("Image reference is not a metadata-backed storage URL or storage object ID.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting image URL");
            return null;
        }
    }

    /// <summary>
    /// Get a metadata-backed public image URL for a storage object by its ID.
    /// </summary>
    public async Task<string?> GetPresignedUrlByIdAsync(Guid storageObjectId)
    {
        try
        {
            return _storageObjectUrlResolver.ResolvePublicImageUrl(storageObjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting image URL by ID");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteImageAsync(string imageKey)
    {
        try
        {
            // TODO: Implement delete via API
            _logger.LogWarning("Delete not implemented for: {ImageKey}", imageKey);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting image");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateLocalPreviewAsync(IBrowserFile file, long maxFileSize = 5 * 1024 * 1024)
    {
        return await _previewService.GenerateLocalPreviewAsync(file, maxFileSize);
    }

    /// <inheritdoc />
    public string GenerateLocalPreviewFromBytes(FileUploadData fileData)
    {
        return _previewService.GenerateLocalPreviewFromBytes(fileData);
    }

}
