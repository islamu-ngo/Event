using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Preferences;

namespace Explore.Blazor.Extensions;

public static class BffStorageEndpoints
{
    private const int MaxUploadFileNameLength = 500;
    private const int MaxUploadContentTypeLength = 100;
    private const int UploadSessionIdLength = 32;
    private const string LegacyImagePurpose = "legacy_image";
    private const string PublicImageVisibility = "public_image";
    private const string PdfContentType = "application/pdf";
    private static readonly HashSet<string> RawDestinationFieldNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "uploadUrl",
            "objectKey",
            "destination",
            "path"
        };
    private static readonly IReadOnlyDictionary<string, string[]> AllowedImageExtensions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = [".jpg", ".jpeg"],
            ["image/png"] = [".png"],
            ["image/gif"] = [".gif"],
            ["image/webp"] = [".webp"]
        };
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    /// <summary>
    /// Maps the storage BFF endpoints: POST /bff/storage/upload-session and POST /bff/storage/upload-proxy.
    /// </summary>
    public static WebApplication MapStorageEndpoints(this WebApplication app)
    {
        app.MapPost("/bff/storage/upload-session", HandleStorageUploadSessionAsync)
            .RequireAuthorization()
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPost(
                "/bff/organizations/{organizationId:guid}/legitimacy-evidence/upload-session",
                HandleOrganizationEvidenceUploadSessionAsync)
            .RequireAuthorization()
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPost("/bff/storage/upload-proxy", HandleStorageUploadProxyAsync)
            .RequireAuthorization()
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> HandleStorageUploadSessionAsync(
        StorageUploadSessionRequest? request,
        HttpContext ctx,
        IStorageObjectClient apiClient,
        IStorageUploadSessionStore sessionStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("StorageUploadSession");

        var validationProblem = ValidateUploadSessionRequest(request, out var normalizedRequest);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        BaseCommandResponseOfStorageUploadSessionDto uploadResponse;
        try
        {
            uploadResponse = await apiClient.CreateStorageUploadSessionAsync(
                normalizedRequest,
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                "Storage upload session generation failed. Status={StatusCode}",
                ex.StatusCode);
            return BffForwardingResults.Problem(
                ex,
                "Failed to create a storage upload session.",
                "Storage upload session failed");
        }

        if (uploadResponse.Success != true || uploadResponse.Id is null)
        {
            return Results.Problem(
                detail: "Storage service returned an invalid upload session response.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var issueResult = await sessionStore.IssueAsync(ctx.User, uploadResponse.Id, normalizedRequest.ContentType, cancellationToken);
        if (!issueResult.Success || string.IsNullOrWhiteSpace(issueResult.SessionId))
        {
            logger.LogWarning(
                "Rejected storage upload session response. FailureCode={FailureCode}",
                issueResult.FailureCode);
            return Results.Problem(
                detail: "Storage service returned an invalid upload session.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new StorageUploadSessionResponse(
            issueResult.SessionId,
            string.Empty,
            string.Empty,
            issueResult.ExpiresInMinutes));
    }

    private static async Task<IResult> HandleOrganizationEvidenceUploadSessionAsync(
        Guid organizationId,
        StorageUploadSessionRequest? request,
        HttpContext ctx,
        IOrganizationTenantEvidenceClient apiClient,
        IStorageUploadSessionStore sessionStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("OrganizationEvidenceUploadSession");
        var validationProblem = ValidateEvidenceUploadSessionRequest(request, out var normalizedRequest);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        BaseCommandResponseOfStorageUploadSessionDto uploadResponse;
        try
        {
            uploadResponse = await apiClient.CreateOrganizationTenantEvidenceUploadSessionAsync(
                organizationId,
                normalizedRequest,
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                "Organization evidence upload session generation failed. Status={StatusCode}",
                ex.StatusCode);
            return BffForwardingResults.Problem(
                ex,
                "Failed to create an Organization evidence upload session.",
                "Organization evidence upload session failed");
        }

        if (uploadResponse.Success != true || uploadResponse.Id is null)
        {
            return Results.Problem(
                detail: "Storage service returned an invalid upload session response.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var issueResult = await sessionStore.IssueAsync(
            ctx.User,
            uploadResponse.Id,
            PdfContentType,
            cancellationToken);
        if (!issueResult.Success || string.IsNullOrWhiteSpace(issueResult.SessionId))
        {
            logger.LogWarning(
                "Rejected Organization evidence upload session response. FailureCode={FailureCode}",
                issueResult.FailureCode);
            return Results.Problem(
                detail: "Storage service returned an invalid upload session.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new StorageUploadSessionResponse(
            issueResult.SessionId,
            string.Empty,
            string.Empty,
            issueResult.ExpiresInMinutes));
    }

    private static async Task<IResult> HandleStorageUploadProxyAsync(
        HttpContext ctx,
        IStorageObjectClient apiClient,
        IStorageUploadSessionStore sessionStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("StorageUploadProxy");

        if (!ctx.Request.HasFormContentType)
        {
            return Results.Problem(
                detail: "Request must be multipart/form-data.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        IFormCollection form;
        try
        {
            form = await ctx.Request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException)
        {
            return InvalidStorageUploadRequest("Multipart upload request is invalid.");
        }
        catch (InvalidDataException)
        {
            return InvalidStorageUploadRequest("Multipart upload request is invalid.");
        }

        if (form.Keys.Any(RawDestinationFieldNames.Contains))
        {
            return InvalidStorageUploadRequest("Raw upload destinations are not accepted.");
        }

        var uploadSessionId = form["uploadSessionId"].ToString().Trim();
        var file = form.Files.GetFile("file");

        if (!IsValidUploadSessionId(uploadSessionId))
        {
            return InvalidStorageUploadRequest("A valid server-issued upload session is required.");
        }

        if (file is null || file.Length == 0)
        {
            return Results.Problem(
                detail: "File is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!IsSafeBrowserFileName(file.FileName, out var fileNameProblem))
        {
            return InvalidStorageUploadRequest(fileNameProblem);
        }

        var declaredContentType = form["contentType"].ToString();
        if (!TryValidateSupportedDeclaration(file.FileName, declaredContentType, out var contentType, out var contentTypeProblem))
        {
            return InvalidStorageUploadRequest(contentTypeProblem);
        }

        if (!TryValidateSupportedDeclaration(file.FileName, file.ContentType, out var fileContentType, out contentTypeProblem) ||
            !string.Equals(contentType, fileContentType, StringComparison.OrdinalIgnoreCase))
        {
            return InvalidStorageUploadRequest("File content type must match declared content type.");
        }

        var resolution = await sessionStore.ResolveAsync(ctx.User, uploadSessionId, contentType, cancellationToken);
        if (!resolution.Success || resolution.Session is null)
        {
            logger.LogWarning("Rejected upload proxy request without a valid server-issued upload session. FailureCode={FailureCode}",
                resolution.FailureCode);
            return Results.Problem(
                detail: "A valid server-issued upload session is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length != resolution.Session.ExpectedSizeBytes)
        {
            return InvalidStorageUploadRequest("File size must match the reserved upload session size.");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var uploadResponse = await apiClient.UploadStorageUploadSessionContentAsync(
                resolution.Session.ApiUploadSessionId,
                stream,
                cancellationToken: cancellationToken);
            if (uploadResponse.Success != true || uploadResponse.Id?.StorageObjectId is null)
            {
                logger.LogWarning(
                    "Upload proxy received invalid finalization response for a resolved API upload session.");
                return Results.Problem(
                    detail: "Storage upload finalized without storage object metadata.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            await sessionStore.ConsumeAsync(resolution.Session.SessionId, cancellationToken);
            var storageObjectId = uploadResponse.Id.StorageObjectId.Value;
            var contentUrl = $"/api/storageobject/{storageObjectId}/content";
            var publicUrl = string.Equals(uploadResponse.Id.Visibility, PublicImageVisibility, StringComparison.Ordinal)
                ? $"/api/storageobject/{storageObjectId}/public"
                : contentUrl;

            return Results.Ok(new StorageUploadProxyResponse(
                storageObjectId,
                publicUrl,
                contentUrl));
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Upload proxy failed for a resolved API upload session. FailureType={FailureType}",
                CategorizeUploadProxyFailure(ex));
            return Results.Problem(
                detail: "Storage upload failed due to an internal proxy error.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private sealed record StorageUploadSessionRequest(
        string FileName,
        string ContentType,
        long ExpectedSizeBytes);

    private sealed record StorageUploadSessionResponse(
        string UploadSessionId,
        string ObjectKey,
        string ViewUrl,
        int ExpiresInMinutes);

    private sealed record StorageUploadProxyResponse(
        Guid StorageObjectId,
        string ViewUrl,
        string ContentUrl);

    private static IResult? ValidateUploadSessionRequest(StorageUploadSessionRequest? request, out CreateStorageUploadSessionDto normalizedRequest)
    {
        normalizedRequest = new CreateStorageUploadSessionDto
        {
            ExpectedSizeBytes = request?.ExpectedSizeBytes ?? 0,
            ContentType = request?.ContentType?.Trim() ?? string.Empty,
            OriginalFileName = request?.FileName?.Trim(),
            SafeDisplayName = request?.FileName?.Trim(),
            Extension = Path.GetExtension(request?.FileName ?? string.Empty).TrimStart('.'),
            Purpose = LegacyImagePurpose,
            Visibility = PublicImageVisibility,
            IdempotencyKey = $"bff:{Guid.CreateVersion7():N}"
        };

        if (request is null)
        {
            return InvalidStorageUploadRequest("Upload session request body is required.");
        }

        if (normalizedRequest.ExpectedSizeBytes <= 0)
        {
            return InvalidStorageUploadRequest("Expected file size must be greater than zero.");
        }

        if (!IsSafeBrowserFileName(normalizedRequest.OriginalFileName, out var fileNameProblem))
        {
            return InvalidStorageUploadRequest(fileNameProblem);
        }

        if (!TryValidateImageDeclaration(
                normalizedRequest.OriginalFileName,
                normalizedRequest.ContentType,
                out var contentType,
                out var contentTypeProblem))
        {
            return InvalidStorageUploadRequest(contentTypeProblem);
        }

        normalizedRequest.ContentType = contentType;
        return null;
    }

    private static IResult? ValidateEvidenceUploadSessionRequest(
        StorageUploadSessionRequest? request,
        out CreateOrganizationTenantEvidenceUploadSessionDto normalizedRequest)
    {
        normalizedRequest = new CreateOrganizationTenantEvidenceUploadSessionDto
        {
            ExpectedSizeBytes = request?.ExpectedSizeBytes ?? 0,
            ContentType = request?.ContentType?.Trim() ?? string.Empty,
            FileName = request?.FileName?.Trim() ?? string.Empty
        };

        if (request is null)
        {
            return InvalidStorageUploadRequest("Upload session request body is required.");
        }

        if (normalizedRequest.ExpectedSizeBytes <= 0)
        {
            return InvalidStorageUploadRequest("Expected file size must be greater than zero.");
        }

        if (!TryValidateEvidenceDeclaration(
                normalizedRequest.FileName,
                normalizedRequest.ContentType,
                out var contentType,
                out var problem))
        {
            return InvalidStorageUploadRequest(problem);
        }

        normalizedRequest.ContentType = contentType;
        return null;
    }

    private static bool TryValidateSupportedDeclaration(
        string? fileName,
        string? contentTypeValue,
        out string contentType,
        out string problem)
    {
        return string.Equals(contentTypeValue?.Trim(), PdfContentType, StringComparison.OrdinalIgnoreCase)
            ? TryValidateEvidenceDeclaration(fileName, contentTypeValue, out contentType, out problem)
            : TryValidateImageDeclaration(fileName, contentTypeValue, out contentType, out problem);
    }

    private static bool TryValidateEvidenceDeclaration(
        string? fileName,
        string? contentTypeValue,
        out string contentType,
        out string problem)
    {
        contentType = contentTypeValue?.Trim() ?? string.Empty;
        problem = string.Empty;

        if (!IsSafeBrowserFileName(fileName, out problem))
        {
            return false;
        }

        if (!string.Equals(contentType, PdfContentType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            problem = "Evidence documents must be PDF files.";
            return false;
        }

        contentType = PdfContentType;
        return true;
    }

    private static bool TryValidateImageDeclaration(
        string? fileName,
        string? contentTypeValue,
        out string contentType,
        out string problem)
    {
        contentType = string.Empty;
        problem = string.Empty;

        var candidate = contentTypeValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            problem = "Content type is required.";
            return false;
        }

        if (candidate.Length > MaxUploadContentTypeLength)
        {
            problem = $"Content type must be {MaxUploadContentTypeLength} characters or fewer.";
            return false;
        }

        if (ContainsControlCharacters(candidate) ||
            !AllowedImageExtensions.TryGetValue(candidate, out var allowedExtensions))
        {
            problem = "Content type must be JPEG, PNG, GIF, or WebP without parameters.";
            return false;
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) ||
            !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            problem = "File extension must match the declared image content type.";
            return false;
        }

        contentType = AllowedImageExtensions.Keys.First(key =>
            string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private static bool IsSafeBrowserFileName(string? value, out string problem)
    {
        var fileName = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            problem = "File name is required.";
            return false;
        }

        if (fileName.Length > MaxUploadFileNameLength)
        {
            problem = $"File name must be {MaxUploadFileNameLength} characters or fewer.";
            return false;
        }

        if (ContainsControlCharacters(fileName) ||
            fileName.Contains('/', StringComparison.Ordinal) ||
            fileName.Contains('\\', StringComparison.Ordinal) ||
            string.Equals(fileName, ".", StringComparison.Ordinal) ||
            string.Equals(fileName, "..", StringComparison.Ordinal))
        {
            problem = "File name must be a simple file name without path segments or control characters.";
            return false;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (ReservedFileNames.Contains(baseName))
        {
            problem = "File name must not use a reserved device name.";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static bool IsValidUploadSessionId(string sessionId)
    {
        if (sessionId.Length != UploadSessionIdLength)
        {
            return false;
        }

        return sessionId.All(static c =>
            (c >= '0' && c <= '9') ||
            (c >= 'a' && c <= 'f') ||
            (c >= 'A' && c <= 'F'));
    }

    private static bool ContainsControlCharacters(string value) =>
        value.Any(char.IsControl);

    private static string CategorizeUploadProxyFailure(Exception exception) =>
        exception switch
        {
            OperationCanceledException => "canceled",
            ApiException => "api_rejected",
            HttpRequestException => "api_unavailable",
            IOException => "stream_io",
            InvalidOperationException => "proxy_invalid_operation",
            _ => "unknown"
        };

    private static IResult InvalidStorageUploadRequest(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid storage upload request");
}
