using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Services;

public interface IImageUploadClient
{
    Task<ImageUploadTarget?> GetUploadUrlAsync(string fileName, string contentType, long? expectedSizeBytes = null);

    Task<ImageUploadResult?> UploadViaBffProxyAsync(string uploadSessionId, FileUploadData fileData);
}

public sealed class ImageUploadClient(
    IHttpClientFactory httpClientFactory,
    ILogger<ImageUploadClient> logger,
    BffClient? bffClient = null,
    IApiClientExecutor? apiClientExecutor = null) : IImageUploadClient
{
    private const string GenerateUploadSessionPath = "/bff/storage/upload-session";
    private const string UploadProxyPath = "/bff/storage/upload-proxy";
    private readonly IApiClientExecutor _apiClientExecutor = apiClientExecutor ?? new ApiClientExecutor();

    public async Task<ImageUploadTarget?> GetUploadUrlAsync(string fileName, string contentType, long? expectedSizeBytes = null)
    {
        try
        {
            var safeFileName = ImageUploadClientPolicy.BuildSafeFileName(fileName, contentType);
            logger.LogInformation(
                "Getting upload URL for selected image. ContentTypeBucket={ContentTypeBucket}, SizeBucket={SizeBucket}",
                ImageUploadClientPolicy.GetContentTypeBucket(contentType),
                expectedSizeBytes.HasValue ? ImageUploadClientPolicy.GetSizeBucket(expectedSizeBytes.Value) : "unknown");

            if (!expectedSizeBytes.HasValue || expectedSizeBytes.Value <= 0)
            {
                logger.LogWarning("BFF upload session requires the selected image size.");
                return null;
            }

            var bffUploadSession = await GetUploadSessionViaBffAsync(
                safeFileName,
                contentType,
                expectedSizeBytes.Value);
            if (bffUploadSession != null)
            {
                return bffUploadSession;
            }

            logger.LogWarning("BFF upload session request returned no usable response.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Error getting upload URL. FailureType={FailureType}",
                ImageUploadClientPolicy.GetFailureType(ex));
            return null;
        }
    }

    public async Task<ImageUploadResult?> UploadViaBffProxyAsync(string uploadSessionId, FileUploadData fileData)
    {
        try
        {
            var safeFileName = ImageUploadClientPolicy.BuildSafeFileName(fileData.FileName, fileData.ContentType);
            using var form = new MultipartFormDataContent();
            using var uploadSessionContent = new StringContent(uploadSessionId);
            using var contentTypeContent = new StringContent(fileData.ContentType);
            form.Add(uploadSessionContent, "uploadSessionId");
            form.Add(contentTypeContent, "contentType");

            using var fileContent = new ByteArrayContent(fileData.Content.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(fileData.ContentType);
            form.Add(fileContent, "file", safeFileName);

            using var response = bffClient is not null
                ? await bffClient.PostMultipartAsync(UploadProxyPath, form)
                : await httpClientFactory.CreateClient("BffClient").PostAsync(UploadProxyPath, form);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "BFF upload proxy completed successfully. SizeBucket={SizeBucket}, ContentTypeBucket={ContentTypeBucket}",
                    ImageUploadClientPolicy.GetSizeBucket(fileData.Size),
                    ImageUploadClientPolicy.GetContentTypeBucket(fileData.ContentType));
                var uploadResult = await response.ReadJsonOrDefaultAsync<BffStorageUploadProxyResponse>();
                return MapBffUploadResult(uploadResult);
            }

            var hasBody = response.Content.Headers.ContentLength.GetValueOrDefault() > 0;
            logger.LogWarning(
                "BFF upload proxy failed. Status={StatusCode}, HasBody={HasBody}, SizeBucket={SizeBucket}",
                (int)response.StatusCode,
                hasBody,
                ImageUploadClientPolicy.GetSizeBucket(fileData.Size));
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "BFF upload proxy request failed. FailureType={FailureType}, SizeBucket={SizeBucket}",
                ImageUploadClientPolicy.GetFailureType(ex),
                ImageUploadClientPolicy.GetSizeBucket(fileData.Size));
            return null;
        }
    }

    private async Task<ImageUploadTarget?> GetUploadSessionViaBffAsync(
        string fileName,
        string contentType,
        long expectedSizeBytes)
    {
        var request = new BffStorageUploadSessionRequest
        {
            FileName = fileName,
            ContentType = contentType,
            ExpectedSizeBytes = expectedSizeBytes
        };

        try
        {
            var result = await _apiClientExecutor.ReadJsonAsync<BffStorageUploadSessionResponse>(
                ct => bffClient is not null
                    ? bffClient.PostAsync(GenerateUploadSessionPath, request, ct)
                    : httpClientFactory.CreateClient("BffClient").PostAsync(GenerateUploadSessionPath, JsonContent.Create(request), ct),
                "BFF upload session");

            if (!result.IsSuccess)
            {
                if (result.StatusCode == HttpStatusCode.Unauthorized)
                {
                    logger.LogWarning("Upload session request returned 401 Unauthorized");
                    return null;
                }

                logger.LogWarning("Upload session request failed via BFF. Status={StatusCode}, HasError={HasError}",
                    result.StatusCode is null ? null : (int)result.StatusCode,
                    !string.IsNullOrWhiteSpace(result.ErrorMessage));
                return null;
            }

            var session = result.Value;
            if (session == null || string.IsNullOrWhiteSpace(session.UploadSessionId))
            {
                return null;
            }

            return new ImageUploadTarget
            {
                UploadSessionId = session.UploadSessionId,
                ObjectKey = string.Empty,
                ViewUrl = string.Empty,
                ExpiresInMinutes = session.ExpiresInMinutes <= 0 ? 60 : session.ExpiresInMinutes
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Error calling upload session endpoint via BFF. FailureType={FailureType}",
                ImageUploadClientPolicy.GetFailureType(ex));
            return null;
        }
    }

    private static ImageUploadResult? MapBffUploadResult(BffStorageUploadProxyResponse? uploadResult)
    {
        if (uploadResult is null || uploadResult.StorageObjectId == Guid.Empty)
        {
            return new ImageUploadResult
            {
                Success = false,
                ErrorMessage = ImageUploadClientPolicy.StorageUploadCompletedWithoutMetadataMessage
            };
        }

        return new ImageUploadResult
        {
            Success = true,
            StorageObjectId = uploadResult.StorageObjectId,
            ViewUrl = uploadResult.ViewUrl,
            ObjectKey = string.Empty
        };
    }

}
