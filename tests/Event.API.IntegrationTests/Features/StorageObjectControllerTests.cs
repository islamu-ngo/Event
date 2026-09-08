using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerClass)]
public class StorageObjectControllerTests
{
    private readonly ApiTestFixture _fixture;
    private const string BaseUrl = "/api/storageobject";

    public StorageObjectControllerTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region GET Endpoints

    [Test]
    public async Task GetAll_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.GetAsync(BaseUrl);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Arguments(1, 10)]
    [Arguments(1, 20)]
    [Arguments(2, 5)]
    public async Task GetAll_WithPaginationParamsWithoutAuth_ShouldReturnUnauthorized(int pageNumber, int pageSize)
    {
        var response = await _fixture.Client.GetAsync($"{BaseUrl}?pageNumber={pageNumber}&pageSize={pageSize}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetById_WithoutAuth_ShouldReturnUnauthorized()
    {
        var id = Guid.NewGuid();

        var response = await _fixture.Client.GetAsync($"{BaseUrl}/{id}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetContent_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}/content");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetPresignedDownloadUrl_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}/presigned-url");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetPublicImage_WithoutAuth_ShouldRemainPublicAndReturnNotFoundForMissingImage()
    {
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}/public");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetPublicImage_WithSafeRaster_ReturnsInlineHeadersAndSupportsRanges()
    {
        var resolver = CreateProviderResolver([1, 2, 3, 4, 5, 6, 7, 8]);
        await using var factory = CreateDeliveryFactory(resolver);
        using var client = factory.CreateClient();
        var lastModified = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
        var storageObjectId = await SeedDeliveryStorageObjectAsync(
            factory,
            "image/png",
            "png",
            "public-image.png",
            StorageObjectVisibilities.PublicImage,
            StorageObjectPurposes.EventImage);
        ConfigureProviderRead(resolver, [1, 2, 3, 4, 5, 6, 7, 8], lastModified);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{BaseUrl}/{storageObjectId}/public");
        request.Headers.Range = new RangeHeaderValue(2, 4);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.PartialContent);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("image/png");
        await Assert.That(response.Content.Headers.ContentDisposition).IsNull();
        await Assert.That(response.Content.Headers.ContentRange!.From).IsEqualTo(2);
        await Assert.That(response.Content.Headers.ContentRange.To).IsEqualTo(4);
        await Assert.That(response.Content.Headers.LastModified).IsEqualTo(lastModified);
        await Assert.That(response.Headers.ETag).IsNotNull();
        await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
        await Assert.That(response.Headers.GetValues("Content-Security-Policy").Single())
            .IsEqualTo("default-src 'none'; frame-ancestors 'none'");
    }

    [Test]
    public async Task GetContent_WithAuthenticatedDocument_ReturnsSanitizedAttachmentDisposition()
    {
        var resolver = CreateProviderResolver([1, 2, 3, 4]);
        await using var factory = CreateDeliveryFactory(resolver);
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var storageObjectId = await SeedDeliveryStorageObjectAsync(
            factory,
            "application/pdf",
            "pdf",
            "../unsafe\r\nX-Injected: yes.pdf",
            StorageObjectVisibilities.AuthenticatedTenant,
            StorageObjectPurposes.Document,
            userId);
        ConfigureProviderRead(resolver, [1, 2, 3, 4], DateTimeOffset.UtcNow);
        using var request = CreateAuthenticatedGetRequest(
            $"{BaseUrl}/{storageObjectId}/content",
            userId);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/pdf");
        await Assert.That(response.Content.Headers.ContentDisposition!.DispositionType).IsEqualTo("attachment");
        await Assert.That(response.Content.Headers.ContentDisposition.FileNameStar).IsEqualTo("download");
        await Assert.That(response.Headers.Contains("X-Injected")).IsFalse();
        await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
        await Assert.That(response.Headers.GetValues("Content-Security-Policy").Single())
            .IsEqualTo("default-src 'none'; frame-ancestors 'none'");
    }

    [Test]
    public async Task GetPublicImage_WithUnsafeMetadata_ReturnsNotFoundBeforeProviderResolution()
    {
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        await using var factory = CreateDeliveryFactory(resolver);
        using var client = factory.CreateClient();
        var storageObjectId = await SeedDeliveryStorageObjectAsync(
            factory,
            "text/html",
            "html",
            "unsafe.html",
            StorageObjectVisibilities.PublicImage,
            StorageObjectPurposes.EventImage);

        using var response = await client.GetAsync($"{BaseUrl}/{storageObjectId}/public");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        resolver.DidNotReceive().GetRequired(Arg.Any<string>());
    }

    [Test]
    public async Task GetPresignedDownloadUrl_WithAuthenticatedDocument_ReturnsNoStoreSecretSafeResponse()
    {
        var resolver = CreateProviderResolver([1]);
        var objectStorageService = Substitute.For<IObjectStorageService>();
        objectStorageService
            .GeneratePresignedDownloadUrl(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns("https://storage.example.test/presigned?signature=secret");
        await using var factory = CreateDeliveryFactory(resolver, objectStorageService);
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var storageObjectId = await SeedDeliveryStorageObjectAsync(
            factory,
            "application/pdf",
            "pdf",
            "Report.pdf",
            StorageObjectVisibilities.AuthenticatedTenant,
            StorageObjectPurposes.Document,
            userId);
        using var request = CreateAuthenticatedGetRequest(
            $"{BaseUrl}/{storageObjectId}/presigned-url?expirationMinutes=15",
            userId);

        using var response = await client.SendAsync(request);
        var result = await response.Content.ReadFromJsonAsync<PresignedDownloadUrlResponseDto>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PresignedUrl).IsEqualTo(
            "https://storage.example.test/presigned?signature=secret");
        await Assert.That(result.ObjectKey).IsEqualTo(string.Empty);
        await Assert.That(result.SafeDisplayName).IsEqualTo("Report.pdf");
        await Assert.That(result.ShouldDownloadAsAttachment).IsTrue();
        await objectStorageService.Received(1).GeneratePresignedDownloadUrl(
            Arg.Is<string>(value => value.Contains(storageObjectId.ToString("N"), StringComparison.Ordinal)),
            "Report.pdf",
            15);
    }

    [Test]
    public async Task RegistrationFileQuarantine_BlocksEveryReadSurfaceUntilExplicitRelease()
    {
        Guid storageObjectId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        var storageObject = new StorageObject
        {
            Id = storageObjectId,
            TenantId = PlatformDefaults.DefaultTenantId,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Tenant = null!,
            Uri = $"{BaseUrl}/{storageObjectId}/content",
            ObjectKey = $"tenants/{PlatformDefaults.DefaultTenantId:N}/{storageObjectId:N}.png",
            Provider = StorageProviders.Local,
            FullName = "quarantined.png",
            SafeDisplayName = "quarantined.png",
            Extension = "png",
            ContentType = "image/png",
            Sha256Checksum = new string('a', 64),
            Size = 8,
            Visibility = StorageObjectVisibilities.PublicImage,
            Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = StorageObjectLifecycleStates.Active,
            CreatedBy = userId
        };
        var repository = Substitute.For<IStorageObjectRepository>();
        repository.GetById(storageObjectId).Returns(storageObject);
        repository.IsRegistrationAnswerFileQuarantinedAsync(storageObjectId, Arg.Any<CancellationToken>())
            .Returns(true);
        var resolver = CreateProviderResolver([1, 2, 3, 4, 5, 6, 7, 8]);
        var provider = resolver.GetRequired(StorageProviders.Local);
        provider.ClearReceivedCalls();
        var objectStorageService = Substitute.For<IObjectStorageService>();
        objectStorageService.GeneratePresignedDownloadUrl(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns("https://storage.example.test/released");
        await using var factory = new QuarantinedRegistrationFileWebApplicationFactory(
            repository,
            resolver,
            objectStorageService)
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        using var client = factory.CreateClient();

        using var content = await client.SendAsync(CreateAuthenticatedGetRequest(
            $"{BaseUrl}/{storageObjectId}/content", userId));
        using var publicImage = await client.GetAsync($"{BaseUrl}/{storageObjectId}/public");
        using var presigned = await client.SendAsync(CreateAuthenticatedGetRequest(
            $"{BaseUrl}/{storageObjectId}/presigned-url", userId));

        await Assert.That(content.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(publicImage.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(presigned.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await provider.DidNotReceive().OpenReadAsync(
            Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>());
        await objectStorageService.DidNotReceive().GeneratePresignedDownloadUrl(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());

        repository.IsRegistrationAnswerFileQuarantinedAsync(storageObjectId, Arg.Any<CancellationToken>())
            .Returns(false);
        using var releasedContent = await client.SendAsync(CreateAuthenticatedGetRequest(
            $"{BaseUrl}/{storageObjectId}/content", userId));
        using var releasedPublicImage = await client.GetAsync($"{BaseUrl}/{storageObjectId}/public");
        using var releasedPresigned = await client.SendAsync(CreateAuthenticatedGetRequest(
            $"{BaseUrl}/{storageObjectId}/presigned-url", userId));

        await Assert.That(releasedContent.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(releasedPublicImage.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(releasedPresigned.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task StorageMetadataResponses_DoNotExposeProviderObjectKeys()
    {
        var resolver = CreateProviderResolver([1]);
        await using var factory = CreateDeliveryFactory(resolver);
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var storageObjectId = await SeedDeliveryStorageObjectAsync(
            factory,
            "image/png",
            "png",
            "provider-key-proof.png",
            StorageObjectVisibilities.AuthenticatedTenant,
            StorageObjectPurposes.EventImage,
            userId);
        var providerObjectKey = $"tenants/{PlatformDefaults.DefaultTenantId:N}/{storageObjectId:N}.png";

        using var detailRequest = CreateAuthenticatedGetRequest($"{BaseUrl}/{storageObjectId}", userId);
        using var detailResponse = await client.SendAsync(detailRequest);
        var detailBody = await detailResponse.Content.ReadAsStringAsync();
        using var listRequest = CreateAuthenticatedGetRequest(BaseUrl, userId);
        using var listResponse = await client.SendAsync(listRequest);
        var listBody = await listResponse.Content.ReadAsStringAsync();

        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detailBody).DoesNotContain("objectKey");
        await Assert.That(detailBody).DoesNotContain(providerObjectKey);
        await Assert.That(listBody).DoesNotContain("objectKey");
        await Assert.That(listBody).DoesNotContain(providerObjectKey);
    }

    [Test]
    public async Task GetById_WithInvalidGuidFormat_ShouldReturnNotFound()
    {
        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/not-a-guid");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ProtectedGetEndpoints_WhenAuthenticatedButDenied_ShouldReturnForbidden()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using var client = factory.CreateClient();
        var storageObjectId = Guid.NewGuid();
        var paths = new[]
        {
            BaseUrl,
            $"{BaseUrl}/{storageObjectId}",
            $"{BaseUrl}/{storageObjectId}/content",
            $"{BaseUrl}/{storageObjectId}/presigned-url"
        };

        foreach (var path in paths)
        {
            using var request = CreateAuthenticatedGetRequest(path);
            var response = await client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
    }

    [Test]
    public async Task StorageReadEndpoints_WithPrivateOwnerObjectForDifferentUser_ShouldReturnSafeNotFound()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        using var client = factory.CreateClient();
        var ownerUserId = Guid.CreateVersion7();
        var requesterUserId = Guid.CreateVersion7();
        var storageObjectId = await SeedPrivateStorageObjectAsync(factory, ownerUserId);
        var paths = new[]
        {
            $"{BaseUrl}/{storageObjectId}/content",
            $"{BaseUrl}/{storageObjectId}/presigned-url"
        };

        foreach (var path in paths)
        {
            using var request = CreateAuthenticatedGetRequest(path, requesterUserId);
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(body).Contains("Storage object not found");
            await Assert.That(body).DoesNotContain(ownerUserId.ToString());
            await Assert.That(body).DoesNotContain("tenants/");
            await Assert.That(body).DoesNotContain("private-owner-proof");
            await Assert.That(body).DoesNotContain(StorageProviders.S3Compatible);
            await Assert.That(body).DoesNotContain(StorageObjectVisibilities.PrivateOwner);
        }
    }

    [Test]
    public async Task StorageReadEndpoints_WithCrossTenantObject_ShouldReturnSafeNotFound()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        using var client = factory.CreateClient();
        var storageObject = await SeedCrossTenantStorageObjectAsync(factory);
        var requesterUserId = Guid.CreateVersion7();
        var paths = new[]
        {
            $"{BaseUrl}/{storageObject.Id}",
            $"{BaseUrl}/{storageObject.Id}/content",
            $"{BaseUrl}/{storageObject.Id}/presigned-url"
        };

        foreach (var path in paths)
        {
            using var request = CreateAuthenticatedGetRequest(path, requesterUserId);
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(body).Contains("Storage object not found");
            await Assert.That(body).DoesNotContain(storageObject.TenantId.ToString());
            await Assert.That(body).DoesNotContain("tenants/");
            await Assert.That(body).DoesNotContain("cross-tenant-proof");
            await Assert.That(body).DoesNotContain(StorageProviders.S3Compatible);
            await Assert.That(body).DoesNotContain(StorageObjectVisibilities.AuthenticatedTenant);
        }
    }

    #endregion

    #region POST Endpoints

    [Test]
    public async Task CreateUploadSession_WithoutAuth_ShouldReturnUnauthorized()
    {
        var createDto = new CreateStorageUploadSessionDto
        {
            ExpectedSizeBytes = 4,
            ContentType = "image/png",
            OriginalFileName = "probe.png",
            Purpose = StorageObjectPurposes.LegacyImage,
            Visibility = StorageObjectVisibilities.PublicImage,
            IdempotencyKey = Guid.CreateVersion7().ToString("N")
        };

        var response = await _fixture.Client.PostAsJsonAsync($"{BaseUrl}/upload-sessions", createDto);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task LegacyStorageWriteRoutes_AreNotCallable()
    {
        var mediator = Substitute.For<IMediator>();
        var storageService = Substitute.For<IObjectStorageService>();
        var repository = Substitute.For<IStorageObjectRepository>();
        await using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
                services.RemoveAll<IObjectStorageService>();
                services.AddSingleton(storageService);
                services.RemoveAll<IStorageObjectRepository>();
                services.AddSingleton(repository);
            }));
        using var client = factory.CreateClient();

        using var directUpload = await client.PostAsJsonAsync(
            $"{BaseUrl}/generate-upload-url",
            new { fileName = "unsafe.svg", contentType = "image/svg+xml" });
        using var callerMetadata = await client.PostAsJsonAsync(
            BaseUrl,
            new { uri = "https://attacker.invalid/file", lifecycleState = StorageObjectLifecycleStates.Active });

        await Assert.That(directUpload.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed).IsTrue();
        await Assert.That(callerMetadata.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed).IsTrue();
        await Assert.That(mediator.ReceivedCalls()).IsEmpty();
        await Assert.That(storageService.ReceivedCalls()).IsEmpty();
        await Assert.That(repository.ReceivedCalls()).IsEmpty();
    }

    #endregion

    #region Upload Session Endpoints

    [Test]
    public async Task UploadSessionContent_WithoutAuth_ShouldReturnUnauthorized()
    {
        using var content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var response = await _fixture.Client.PutAsync(
            $"{BaseUrl}/upload-sessions/{Guid.CreateVersion7()}/content",
            content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CancelUploadSession_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.DeleteAsync($"{BaseUrl}/upload-sessions/{Guid.CreateVersion7()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UploadSessionRoutes_WithInvalidGuidFormat_ShouldReturnNotFound()
    {
        using var content = new ByteArrayContent([1]);

        var uploadResponse = await _fixture.Client.PutAsync(
            $"{BaseUrl}/upload-sessions/not-a-guid/content",
            content);
        var cancelResponse = await _fixture.Client.DeleteAsync($"{BaseUrl}/upload-sessions/not-a-guid");

        await Assert.That(uploadResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(cancelResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UploadSessionEndpoints_WithMissingSession_WhenAuthenticated_ShouldReturnSafeNotFound()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var uploadSessionId = Guid.CreateVersion7();

        using var uploadRequest = CreateAuthenticatedUploadRequest(uploadSessionId, userId);
        using var uploadResponse = await client.SendAsync(uploadRequest);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();

        using var cancelRequest = CreateAuthenticatedDeleteRequest($"{BaseUrl}/upload-sessions/{uploadSessionId}", userId);
        using var cancelResponse = await client.SendAsync(cancelRequest);
        var cancelBody = await cancelResponse.Content.ReadAsStringAsync();

        await Assert.That(uploadResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(cancelResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await AssertSafeUploadSessionNotFoundProblem(uploadBody);
        await AssertSafeUploadSessionNotFoundProblem(cancelBody);
    }

    [Test]
    public async Task UploadSessionContent_WithCanceledSession_WhenAuthenticated_ShouldReturnSafeConflict()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var uploadSessionId = await SeedUploadSessionAsync(
            factory,
            userId,
            StorageUploadSessionStates.Canceled);

        using var request = CreateAuthenticatedUploadRequest(uploadSessionId, userId);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(body).Contains("Storage upload session conflict");
        await Assert.That(body).Contains("storage_upload_session_invalid_state");
        await Assert.That(body).Contains("Upload session cannot accept bytes in its current state.");
        await AssertDoesNotEchoStorageSessionMetadata(body);
    }

    [Test]
    public async Task CancelUploadSession_WithFinalizedSession_WhenAuthenticated_ShouldReturnSafeConflict()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var uploadSessionId = await SeedUploadSessionAsync(
            factory,
            userId,
            StorageUploadSessionStates.Finalized);

        using var request = CreateAuthenticatedDeleteRequest($"{BaseUrl}/upload-sessions/{uploadSessionId}", userId);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(body).Contains("Storage upload session conflict");
        await Assert.That(body).Contains("storage_upload_session_finalized");
        await Assert.That(body).Contains("Finalized upload sessions cannot be canceled.");
        await AssertDoesNotEchoStorageSessionMetadata(body);
    }

    #endregion

    #region PATCH Endpoints

    [Test]
    public async Task Update_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var id = Guid.NewGuid();
        var updateDto = new UpdateStorageObjectDto
        {
            Metadata = new StorageObjectMetadataUpdateDto
            {
                FullName = "updated-file.png",
                SafeDisplayName = "Updated file.png"
            }
        };

        // Act
        var response = await _fixture.Client.PatchAsJsonAsync($"{BaseUrl}/{id}", updateDto);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateContract_DoesNotSerializeServerOwnedStorageFields()
    {
        var updateDto = new UpdateStorageObjectDto
        {
            Metadata = new StorageObjectMetadataUpdateDto
            {
                FullName = "updated-file.png",
                SafeDisplayName = "Updated file.png"
            }
        };
        using var content = JsonContent.Create(updateDto);
        var body = await content.ReadAsStringAsync();

        await Assert.That(body).DoesNotContain("\"id\"");
        await Assert.That(body).DoesNotContain("\"uri\"");
        await Assert.That(body).DoesNotContain("\"objectKey\"");
        await Assert.That(body).DoesNotContain("\"provider\"");
        await Assert.That(body).DoesNotContain("\"sha256Checksum\"");
        await Assert.That(body).DoesNotContain("\"size\"");
        await Assert.That(body).DoesNotContain("\"lifecycleState\"");
        await Assert.That(body).DoesNotContain("\"tenantId\"");
    }

    #endregion

    #region DELETE Endpoints

    [Test]
    public async Task Delete_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _fixture.Client.DeleteAsync($"{BaseUrl}/{id}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    private static async Task<Guid> SeedPrivateStorageObjectAsync(
        AuthenticatedWebApplicationFactory factory,
        Guid ownerUserId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var storageObjectId = Guid.CreateVersion7();

        context.StorageObjects.Add(new StorageObject
        {
            Id = storageObjectId,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Uri = $"s3://test-bucket/tenants/{PlatformDefaults.DefaultTenantId:N}/private-owner-proof.png",
            ObjectKey = $"tenants/{PlatformDefaults.DefaultTenantId:N}/private-owner-proof.png",
            Provider = StorageProviders.S3Compatible,
            FullName = "private-owner-proof.png",
            SafeDisplayName = "private-owner-proof.png",
            Extension = ".png",
            ContentType = "image/png",
            Sha256Checksum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            Size = 4,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Purpose = StorageObjectPurposes.Attachment,
            LifecycleState = StorageObjectLifecycleStates.Active,
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = ownerUserId,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        await context.SaveChangesAsync();

        return storageObjectId;
    }

    private static DeliveryWebApplicationFactory CreateDeliveryFactory(
        IFileStorageProviderResolver resolver,
        IObjectStorageService? objectStorageService = null)
        => new(resolver, objectStorageService)
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };

    private static IFileStorageProviderResolver CreateProviderResolver(byte[] content)
    {
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        resolver.GetRequired(StorageProviders.Local).Returns(provider);
        ConfigureProviderRead(resolver, content, DateTimeOffset.UtcNow);
        return resolver;
    }

    private static void ConfigureProviderRead(
        IFileStorageProviderResolver resolver,
        byte[] content,
        DateTimeOffset lastModified)
    {
        var provider = resolver.GetRequired(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => new FileStorageReadResult(
                new MemoryStream(content),
                "application/octet-stream",
                content.Length,
                lastModified));
    }

    private static async Task<Guid> SeedDeliveryStorageObjectAsync(
        AuthenticatedWebApplicationFactory factory,
        string contentType,
        string extension,
        string safeDisplayName,
        string visibility,
        string purpose,
        Guid? createdBy = null)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var storageObjectId = Guid.CreateVersion7();

        context.StorageObjects.Add(new StorageObject
        {
            Id = storageObjectId,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Uri = $"/api/storageobject/{storageObjectId}/content",
            ObjectKey = $"tenants/{PlatformDefaults.DefaultTenantId:N}/{storageObjectId:N}.{extension}",
            Provider = StorageProviders.Local,
            FullName = safeDisplayName,
            SafeDisplayName = safeDisplayName,
            Extension = extension,
            ContentType = contentType,
            Sha256Checksum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            Size = 8,
            Visibility = visibility,
            Purpose = purpose,
            LifecycleState = StorageObjectLifecycleStates.Active,
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        await context.SaveChangesAsync();

        return storageObjectId;
    }

    private sealed class DeliveryWebApplicationFactory(
        IFileStorageProviderResolver resolver,
        IObjectStorageService? objectStorageService) : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileStorageProviderResolver>();
                services.AddSingleton(resolver);
                if (objectStorageService is not null)
                {
                    services.RemoveAll<IObjectStorageService>();
                    services.AddSingleton(objectStorageService);
                }
            });
        }
    }

    private sealed class QuarantinedRegistrationFileWebApplicationFactory(
        IStorageObjectRepository repository,
        IFileStorageProviderResolver resolver,
        IObjectStorageService objectStorageService) : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStorageObjectRepository>();
                services.AddSingleton(repository);
                services.RemoveAll<IFileStorageProviderResolver>();
                services.AddSingleton(resolver);
                services.RemoveAll<IObjectStorageService>();
                services.AddSingleton(objectStorageService);
            });
        }
    }

    private static async Task<(Guid Id, Guid TenantId)> SeedCrossTenantStorageObjectAsync(
        AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var secondaryTenant = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(
            context,
            "Storage Isolation Tenant");
        var storageObjectId = Guid.CreateVersion7();

        context.StorageObjects.Add(new StorageObject
        {
            Id = storageObjectId,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Uri = $"s3://test-bucket/tenants/{secondaryTenant.TenantId:N}/cross-tenant-proof.png",
            ObjectKey = $"tenants/{secondaryTenant.TenantId:N}/cross-tenant-proof.png",
            Provider = StorageProviders.S3Compatible,
            FullName = "cross-tenant-proof.png",
            SafeDisplayName = "cross-tenant-proof.png",
            Extension = ".png",
            ContentType = "image/png",
            Sha256Checksum = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
            Size = 4,
            Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            Purpose = StorageObjectPurposes.Attachment,
            LifecycleState = StorageObjectLifecycleStates.Active,
            TenantId = secondaryTenant.TenantId,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = secondaryTenant.UserId,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        await context.SaveChangesAsync();

        return (storageObjectId, secondaryTenant.TenantId);
    }

    private static async Task<Guid> SeedUploadSessionAsync(
        AuthenticatedWebApplicationFactory factory,
        Guid userId,
        string status)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var session = new StorageUploadSession
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            UserId = userId,
            Provider = StorageProviders.Local,
            RouteKey = StorageRouteKeys.General,
            PolicyMaxUploadBytes = 11,
            PolicyVersion = "1",
            ExpectedSizeBytes = 11,
            ReservedBytes = 11,
            ContentType = "text/plain",
            OriginalFileName = "semantic-proof.txt",
            SafeDisplayName = "semantic-proof.txt",
            Extension = "txt",
            Purpose = StorageObjectPurposes.Attachment,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Status = StorageUploadSessionStates.Reserved,
            IdempotencyKey = $"semantic-{Guid.CreateVersion7():N}",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        if (status == StorageUploadSessionStates.Canceled)
        {
            session.Cancel(DateTime.UtcNow.AddMinutes(-1));
        }
        else if (status == StorageUploadSessionStates.Finalized)
        {
            session.Finalize(
                Guid.CreateVersion7(),
                $"tenants/{PlatformDefaults.DefaultTenantId:N}/semantic-proof.txt",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                DateTime.UtcNow.AddMinutes(-1));
        }
        else
        {
            session.Status = status;
        }

        context.StorageUploadSessions.Add(session);
        await context.SaveChangesAsync();

        return session.Id;
    }

    private static HttpRequestMessage CreateAuthenticatedGetRequest(string path, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId ?? Guid.NewGuid()));
        return request;
    }

    private static HttpRequestMessage CreateAuthenticatedUploadRequest(Guid uploadSessionId, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/upload-sessions/{uploadSessionId}/content")
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        return request;
    }

    private static HttpRequestMessage CreateAuthenticatedDeleteRequest(string path, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        return request;
    }

    private static async Task AssertSafeUploadSessionNotFoundProblem(string body)
    {
        await Assert.That(body).Contains("Storage upload session not found");
        await Assert.That(body).Contains("storage_upload_session_not_found");
        await AssertDoesNotEchoStorageSessionMetadata(body);
    }

    private static async Task AssertDoesNotEchoStorageSessionMetadata(string body)
    {
        await Assert.That(body).DoesNotContain(PlatformDefaults.DefaultTenantId.ToString());
        await Assert.That(body).DoesNotContain("tenants/");
        await Assert.That(body).DoesNotContain("semantic-proof");
        await Assert.That(body).DoesNotContain(StorageProviders.Local);
        await Assert.That(body).DoesNotContain(StorageObjectVisibilities.PrivateOwner);
        await Assert.That(body).DoesNotContain("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    }
}
