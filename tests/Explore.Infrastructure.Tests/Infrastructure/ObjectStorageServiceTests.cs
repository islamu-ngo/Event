using Amazon.S3;
using Amazon.S3.Model;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Storage;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[NotInParallel("StorageTargetSqlite")]
public sealed class ObjectStorageServiceTests
{
    [Test]
    [Arguments(null, false)]
    [Arguments("original-version", true)]
    [Arguments(null, true)]
    public async Task GeneratePresignedDownloadUrl_UsesCapturedTargetAndRequiresExactVersion(string? version, bool versioned)
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var config = CreateConfig();
        var configResolver = Substitute.For<IS3ConfigResolver>();
        var clientFactory = Substitute.For<IS3ClientFactory>();
        var s3Client = Substitute.For<IAmazonS3>();
        GetPreSignedUrlRequest? capturedRequest = null;
        configResolver.ResolveAsync().Returns(config);
        clientFactory.CreatePresignClient(Arg.Any<S3Configuration>()).Returns(s3Client);
        clientFactory.CreateDataClient(Arg.Any<S3Configuration>()).Returns(s3Client);
        s3Client.GetBucketVersioningAsync(Arg.Any<GetBucketVersioningRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetBucketVersioningResponse
            { VersioningConfig = new S3BucketVersioningConfig { Status = versioned ? VersionStatus.Enabled : VersionStatus.Off } });
        var access = RetainedSecretReference.Capture(SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Storage.AccessKeyId, SecretScope.Instance, null, "TEST_STORAGE_ACCESS"), "Environment");
        var secret = RetainedSecretReference.Capture(SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, SecretScope.Instance, null, "TEST_STORAGE_SECRET"), "Environment");
        var binding = StorageProviderBinding.S3("https://original.example.test", "original-bucket", "us-east-1", true, access, secret);
        environment.Context.Add(binding);
        await environment.Context.SaveChangesAsync();
        var secrets = Substitute.For<IRetainedSecretResolver>();
        secrets.ResolveAsync(Arg.Any<RetainedSecretReference>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var reference = call.Arg<RetainedSecretReference>();
            return SecretResolutionResult.Resolved(new ResolvedSecret(reference.SettingKey,
                Guid.CreateVersion7().ToString("N"), reference.SourceType, reference.Scope, reference.ScopeId, DateTimeOffset.UtcNow));
        });
        s3Client.GetPreSignedURL(Arg.Do<GetPreSignedUrlRequest>(request => capturedRequest = request))
            .Returns("https://storage.example.test/presigned");
        var service = new ObjectStorageService(
            configResolver,
            clientFactory,
            new TestListLogger<ObjectStorageService>(),
            new StorageProviderBindingRepository(environment.Context), secrets);

        if (versioned && version is null)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GeneratePresignedDownloadUrl(
                binding.Id, "tenants/example/document.pdf", "Quarterly report.pdf", 15));
            await Assert.That(capturedRequest).IsNull();
            return;
        }
        await service.GeneratePresignedDownloadUrl(
            binding.Id,
            "tenants/example/document.pdf",
            "Quarterly report.pdf",
            15,
            version);

        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.BucketName).IsEqualTo("original-bucket");
        await Assert.That(capturedRequest.VersionId).IsEqualTo(version);
        await Assert.That(capturedRequest!.ResponseHeaderOverrides.ContentDisposition)
            .IsEqualTo("attachment; filename*=utf-8''Quarterly%20report.pdf");
    }

    [Test]
    public async Task TestConnectionAsync_WhenProbeFails_LogsFailureTypeWithoutEndpointOrProviderPayload()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var config = CreateConfig();
        var configResolver = Substitute.For<IS3ConfigResolver>();
        var clientFactory = Substitute.For<IS3ClientFactory>();
        var s3Client = Substitute.For<IAmazonS3>();
        var logger = new TestListLogger<ObjectStorageService>();
        configResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(config);
        clientFactory.CreateDataClient(config).Returns(s3Client);
        s3Client.ListBucketsAsync(Arg.Any<CancellationToken>())
            .Returns<Task<ListBucketsResponse>>(_ => throw new InvalidOperationException(
                $"provider leaked endpoint {config.Endpoint} bucket {config.BucketName} secret {config.SecretAccessKey}"));
        var service = new ObjectStorageService(configResolver, clientFactory, logger,
            new StorageProviderBindingRepository(environment.Context), Substitute.For<IRetainedSecretResolver>());

        var result = await service.TestConnectionAsync(CancellationToken.None);

        await Assert.That(result).IsFalse();

        var log = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        await Assert.That(log.Exception).IsNull();
        await Assert.That(log.Message).Contains("FailureType=provider_unavailable");
        await Assert.That(log.Message).DoesNotContain("provider leaked endpoint");
        await Assert.That(log.Message).DoesNotContain(config.Endpoint);
        await Assert.That(log.Message).DoesNotContain(config.BucketName);
        await Assert.That(log.Message).DoesNotContain(config.SecretAccessKey);
    }

    private static S3Configuration CreateConfig()
        => new()
        {
            Endpoint = "https://s3.secret.example.test/private?token=storage-secret",
            BucketName = "tenant-private-bucket",
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            Region = "us-east-1",
            ForcePathStyle = true
        };
}
