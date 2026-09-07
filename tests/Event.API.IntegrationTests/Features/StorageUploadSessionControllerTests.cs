using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class StorageUploadSessionControllerTests
{
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();

    public StorageUploadSessionControllerTests()
    {
        _tenantContext.TenantId.Returns(_tenantId);
    }

    [Test]
    public async Task CreateUploadSession_DispatchesCommandWithTenantContext()
    {
        var dto = CreateDto();
        var response = Success(Guid.CreateVersion7());
        _mediator.Send(Arg.Any<CreateStorageUploadSessionCommand>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController();

        var actionResult = await controller.CreateUploadSession(dto, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(response);
        await _mediator.Received(1).Send(
            Arg.Is<CreateStorageUploadSessionCommand>(command =>
                command.UploadSessionDto == dto &&
                command.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UploadSessionContent_DispatchesStreamCommandWithoutBrowserDestination()
    {
        var sessionId = Guid.CreateVersion7();
        var body = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var response = Success(sessionId);
        _mediator.Send(Arg.Any<FinalizeStorageUploadSessionCommand>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController(body, "text/plain", body.Length);

        var actionResult = await controller.UploadSessionContent(sessionId, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await _mediator.Received(1).Send(
            Arg.Is<FinalizeStorageUploadSessionCommand>(command =>
                command.UploadSessionId == sessionId &&
                command.Content == body &&
                command.ContentType == "text/plain" &&
                command.ContentLength == body.Length &&
                command.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UploadSessionContent_WithGeneratedClientMediaType_UsesReservedSessionContentType()
    {
        var sessionId = Guid.CreateVersion7();
        var body = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        _mediator.Send(Arg.Any<FinalizeStorageUploadSessionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Success(sessionId));
        var controller = CreateController(body, "application/octet-stream", body.Length);

        _ = await controller.UploadSessionContent(sessionId, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<FinalizeStorageUploadSessionCommand>(command =>
                command.UploadSessionId == sessionId &&
                command.Content == body &&
                command.ContentType == null &&
                command.ContentLength == body.Length &&
                command.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CancelUploadSession_DispatchesCommandWithTenantContext()
    {
        var sessionId = Guid.CreateVersion7();
        var response = Success(sessionId);
        _mediator.Send(Arg.Any<CancelStorageUploadSessionCommand>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController();

        var actionResult = await controller.CancelUploadSession(sessionId, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await _mediator.Received(1).Send(
            Arg.Is<CancelStorageUploadSessionCommand>(command =>
                command.UploadSessionId == sessionId &&
                command.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetContent_DispatchesMetadataIdQueryAndEnablesRangeProcessing()
    {
        var storageObjectId = Guid.CreateVersion7();
        var response = new StorageObjectContentResult(
            new MemoryStream([1, 2, 3]),
            "image/png",
            3,
            DateTimeOffset.UtcNow,
            "abc123");
        _mediator.Send(Arg.Any<GetStorageObjectContentRequest>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController();

        var actionResult = await controller.GetContent(storageObjectId, CancellationToken.None);

        var file = actionResult as FileStreamResult;
        await Assert.That(file).IsNotNull();
        await Assert.That(file!.EnableRangeProcessing).IsTrue();
        await Assert.That(file.ContentType).IsEqualTo("image/png");
        await Assert.That(file.EntityTag?.Tag).IsEqualTo("\"abc123\"");
        await _mediator.Received(1).Send(
            Arg.Is<GetStorageObjectContentRequest>(query =>
                query.StorageObjectId == storageObjectId &&
                query.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetPresignedDownloadUrl_DispatchesQueryWithExpiration()
    {
        var storageObjectId = Guid.CreateVersion7();
        var response = new PresignedDownloadUrlResponseDto
        {
            PresignedUrl = "https://storage.example.test/presigned",
            ExpiresInMinutes = 15
        };
        _mediator.Send(Arg.Any<GetPresignedDownloadUrlRequest>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController();

        var actionResult = await controller.GetPresignedDownloadUrl(storageObjectId, 15, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(response);
        await _mediator.Received(1).Send(
            Arg.Is<GetPresignedDownloadUrlRequest>(query =>
                query.Id == storageObjectId &&
                query.TenantId == _tenantId &&
                query.ExpirationMinutes == 15),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetPresignedDownloadUrl_WhenHandlerReturnsNull_ReturnsNotFound()
    {
        _mediator.Send(Arg.Any<GetPresignedDownloadUrlRequest>(), Arg.Any<CancellationToken>())
            .Returns((PresignedDownloadUrlResponseDto?)null);
        var controller = CreateController();

        var actionResult = await controller.GetPresignedDownloadUrl(Guid.CreateVersion7(), 15, CancellationToken.None);

        var objectResult = actionResult.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task GetPublicImage_WhenReaderReturnsNull_ReturnsNotFound()
    {
        _mediator.Send(Arg.Any<GetPublicImageRequest>(), Arg.Any<CancellationToken>())
            .Returns((StorageObjectContentResult?)null);
        var controller = CreateController();

        var actionResult = await controller.GetPublicImage(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = actionResult as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task CreateUploadSession_WhenTooLarge_ReturnsPayloadTooLargeProblemDetails()
    {
        _mediator.Send(Arg.Any<CreateStorageUploadSessionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Failure("Upload exceeds the configured per-file limit.", FailureCodes.StorageUploadTooLarge));
        var controller = CreateController();

        var actionResult = await controller.CreateUploadSession(CreateDto(), CancellationToken.None);

        var objectResult = actionResult.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(413);

        var problemDetails = objectResult.Value as ProblemDetails;
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(problemDetails!.Extensions["code"]).IsEqualTo(FailureCodes.StorageUploadTooLarge);
        await Assert.That(problemDetails.Title).IsEqualTo("Storage upload is too large");
    }

    [Test]
    public async Task UploadSessionContent_WhenStorageFailure_ReturnsMappedProblemDetails()
    {
        var cases = new[]
        {
            new StorageFailureCase(FailureCodes.StorageUploadSessionNotFound, "Upload session was not found.", 404, "Storage upload session not found"),
            new StorageFailureCase(FailureCodes.StorageUploadSessionExpired, "Upload session has expired.", 409, "Storage upload session conflict"),
            new StorageFailureCase(FailureCodes.StorageUploadSizeMismatch, "Upload content length does not match the reserved byte count.", 400, "Storage upload validation failed"),
            new StorageFailureCase(FailureCodes.StorageUploadContentTypeMismatch, "Upload content type does not match the reserved content type.", 400, "Storage upload validation failed"),
            new StorageFailureCase(FailureCodes.StorageUploadContentSignatureMismatch, "Upload content did not match the reserved content policy.", 400, "Storage upload validation failed"),
            new StorageFailureCase(FailureCodes.StorageUploadWriteFailed, "Storage provider returned invalid upload metadata.", 503, "Storage provider unavailable")
        };

        foreach (var testCase in cases)
        {
            _mediator.Send(Arg.Any<FinalizeStorageUploadSessionCommand>(), Arg.Any<CancellationToken>())
                .Returns(Failure(testCase.Message, testCase.FailureCode));
            var controller = CreateController(new MemoryStream([1]), "text/plain", 1);

            var actionResult = await controller.UploadSessionContent(Guid.CreateVersion7(), CancellationToken.None);

            var objectResult = actionResult.Result as ObjectResult;
            await Assert.That(objectResult).IsNotNull();
            await Assert.That(objectResult!.StatusCode).IsEqualTo(testCase.StatusCode);

            var problemDetails = objectResult.Value as ProblemDetails;
            await Assert.That(problemDetails).IsNotNull();
            await Assert.That(problemDetails!.Title).IsEqualTo(testCase.Title);
            await Assert.That(problemDetails.Detail).IsEqualTo(testCase.Message);
            await Assert.That(problemDetails.Extensions["code"]).IsEqualTo(testCase.FailureCode);
        }
    }

    [Test]
    public async Task UploadSessionContent_WhenProviderFailureContainsInternalData_ReturnsCanonicalProblemDetails()
    {
        const string unsafeMessage = "S3 SignatureDoesNotMatch at https://storage.internal.example/bucket/tenants/tenant-1/raw-key?X-Amz-Signature=secret";
        _mediator.Send(Arg.Any<FinalizeStorageUploadSessionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Failure(unsafeMessage, FailureCodes.StorageUploadWriteFailed));
        var controller = CreateController(new MemoryStream([1]), "text/plain", 1);

        var actionResult = await controller.UploadSessionContent(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = actionResult.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(503);

        var problemDetails = objectResult.Value as ProblemDetails;
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(problemDetails!.Title).IsEqualTo("Storage provider unavailable");
        await Assert.That(problemDetails.Detail).IsEqualTo("Storage provider returned invalid upload metadata.");
        await Assert.That(problemDetails.Extensions["code"]).IsEqualTo(FailureCodes.StorageUploadWriteFailed);

        var serializedProblem = JsonSerializer.Serialize(problemDetails);
        await Assert.That(serializedProblem).DoesNotContain("SignatureDoesNotMatch");
        await Assert.That(serializedProblem).DoesNotContain("storage.internal.example");
        await Assert.That(serializedProblem).DoesNotContain("raw-key");
        await Assert.That(serializedProblem).DoesNotContain("secret");
    }

    [Test]
    public async Task UploadSessionContent_WhenValidationFailureContainsInternalData_ReturnsCanonicalValidationProblem()
    {
        const string unsafeMessage = "ContentType must equal application/private-tenant-secret; objectKey=tenants/tenant-1/raw-key";
        _mediator.Send(Arg.Any<FinalizeStorageUploadSessionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Failure(unsafeMessage, FailureCodes.StorageUploadContentTypeMismatch));
        var controller = CreateController(new MemoryStream([1]), "text/plain", 1);

        var actionResult = await controller.UploadSessionContent(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = actionResult.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(400);

        var problemDetails = objectResult.Value as ValidationProblemDetails;
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(problemDetails!.Title).IsEqualTo("Storage upload validation failed");
        await Assert.That(problemDetails.Detail).IsEqualTo("Upload content type does not match the reserved content type.");
        await Assert.That(problemDetails.Extensions["code"]).IsEqualTo(FailureCodes.StorageUploadContentTypeMismatch);
        await Assert.That(problemDetails.Errors.TryGetValue("storageUpload", out var errors)).IsTrue();
        await Assert.That(errors).IsNotNull();
        await Assert.That(errors!.Length).IsEqualTo(1);
        await Assert.That(errors[0]).IsEqualTo("Upload content type does not match the reserved content type.");

        var serializedProblem = JsonSerializer.Serialize(problemDetails);
        await Assert.That(serializedProblem).DoesNotContain("application/private-tenant-secret");
        await Assert.That(serializedProblem).DoesNotContain("objectKey");
        await Assert.That(serializedProblem).DoesNotContain("raw-key");
    }

    [Test]
    public async Task UploadSessionRoutes_UseStableRouteNamesAndWritePolicy()
    {
        var create = typeof(StorageObjectController).GetMethod(nameof(StorageObjectController.CreateUploadSession))!;
        var upload = typeof(StorageObjectController).GetMethod(nameof(StorageObjectController.UploadSessionContent))!;
        var cancel = typeof(StorageObjectController).GetMethod(nameof(StorageObjectController.CancelUploadSession))!;
        var content = typeof(StorageObjectController).GetMethod(nameof(StorageObjectController.GetContent))!;

        await AssertRoute(create, typeof(HttpPostAttribute), "upload-sessions", RouteNames.CreateStorageUploadSession);
        await AssertRoute(upload, typeof(HttpPutAttribute), "upload-sessions/{uploadSessionId:guid}/content", RouteNames.UploadStorageUploadSessionContent);
        await AssertRoute(cancel, typeof(HttpDeleteAttribute), "upload-sessions/{uploadSessionId:guid}", RouteNames.CancelStorageUploadSession);
        await AssertRoute(content, typeof(HttpGetAttribute), "{id:guid}/content", RouteNames.GetStorageObjectContent);

        await Assert.That(GetRateLimitPolicy(create)).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(GetRateLimitPolicy(upload)).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(GetRateLimitPolicy(cancel)).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(GetRequestTimeoutPolicy(upload)).IsEqualTo(RequestTimeoutExtensions.ComplexPolicy);
    }

    [Test]
    public async Task StorageDownloadRoutes_RequireAuthenticationAndDoNotCachePresignedUrls()
    {
        var content = typeof(StorageObjectController).GetMethod(nameof(StorageObjectController.GetContent))!;
        var presigned = typeof(StorageObjectController).GetMethod(nameof(StorageObjectController.GetPresignedDownloadUrl))!;

        await Assert.That(content.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(presigned.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(presigned.GetCustomAttribute<OutputCacheAttribute>()).IsNull();

        var responseCache = presigned.GetCustomAttribute<ResponseCacheAttribute>();
        await Assert.That(responseCache).IsNotNull();
        await Assert.That(responseCache!.NoStore).IsTrue();
        await Assert.That(responseCache.Location).IsEqualTo(ResponseCacheLocation.None);
    }

    [Test]
    public async Task ArbitraryKeyStorageRoutes_AreNotExposed()
    {
        var routeTemplates = typeof(StorageObjectController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .Select(attribute => attribute.Template)
            .Where(template => template is not null)
            .ToArray();
        var routeNames = typeof(RouteNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string)field.GetValue(null)!)
            .ToArray();

        await Assert.That(routeTemplates).DoesNotContain("file/{*fileKey}");
        await Assert.That(routeTemplates).DoesNotContain("presigned-url-by-key/{*objectKey}");
        await Assert.That(routeNames).DoesNotContain("GetStorageObjectFile");
        await Assert.That(routeNames).DoesNotContain("GetStorageObjectPresignedDownloadUrlByKey");
    }

    [Test]
    public async Task LegacyStorageWriteRoutes_AreNotExposed()
    {
        var routeTemplates = typeof(StorageObjectController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .Select(attribute => (attribute.HttpMethods.Single(), attribute.Template))
            .ToArray();

        await Assert.That(routeTemplates).DoesNotContain(("POST", "generate-upload-url"));
        await Assert.That(routeTemplates).DoesNotContain(("POST", null));
        await Assert.That(typeof(RouteNames).GetField("GenerateStorageObjectUploadUrl")).IsNull();
        await Assert.That(typeof(RouteNames).GetField("CreateStorageObject")).IsNull();
        await Assert.That(typeof(StorageObjectController).GetMethod(nameof(StorageObjectController.CreateUploadSession))).IsNotNull();
        await Assert.That(typeof(StorageObjectController).GetMethod(nameof(StorageObjectController.UploadSessionContent))).IsNotNull();
    }

    private StorageObjectController CreateController(Stream? requestBody = null, string? contentType = null, long? contentLength = null)
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-test"
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())], "test"));
        httpContext.Request.Body = requestBody ?? Stream.Null;
        httpContext.Request.ContentType = contentType;
        httpContext.Request.ContentLength = contentLength;

        return new StorageObjectController(
            _mediator,
            _tenantContext,
            Substitute.For<IResourceAssembler<StorageObjectDto, StorageObjectListDto>>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static async Task AssertRoute(MethodInfo method, Type attributeType, string template, string routeName)
    {
        var attribute = method.GetCustomAttributes().Single(value => value.GetType() == attributeType) as HttpMethodAttribute;
        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Template).IsEqualTo(template);
        await Assert.That(attribute.Name).IsEqualTo(routeName);
    }

    private static string? GetRateLimitPolicy(MethodInfo method)
        => method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;

    private static string? GetRequestTimeoutPolicy(MethodInfo method)
        => method.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName;

    private static CreateStorageUploadSessionDto CreateDto()
        => new()
        {
            ExpectedSizeBytes = 5,
            ContentType = "text/plain",
            OriginalFileName = "hello.txt",
            Purpose = StorageObjectPurposes.Attachment,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            IdempotencyKey = "upload-test"
        };

    private static BaseCommandResponse<StorageUploadSessionDto> Success(Guid sessionId)
        => BaseCommandResponse.Success(
            new StorageUploadSessionDto
            {
                Id = sessionId,
                TenantId = Guid.CreateVersion7(),
                Provider = StorageProviders.Local,
                ContentType = "text/plain",
                SafeDisplayName = "hello.txt",
                Purpose = StorageObjectPurposes.Attachment,
                Visibility = StorageObjectVisibilities.PrivateOwner,
                Status = StorageUploadSessionStates.Reserved
            },
            "ok");

    private static BaseCommandResponse<StorageUploadSessionDto> Failure(string message, string failureCode)
        => BaseCommandResponse.Failure<StorageUploadSessionDto>(
            failureCode,
            message,
            [message]);

    private sealed record StorageFailureCase(
        string FailureCode,
        string Message,
        int StatusCode,
        string Title);
}
