using Amazon.S3;
using Amazon.S3.Model;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class ObjectStorageServiceTests
{
    [Test]
    public async Task GeneratePresignedDownloadUrl_UsesAttachmentResponseOverride()
    {
        var config = CreateConfig();
        var configResolver = Substitute.For<IS3ConfigResolver>();
        var clientFactory = Substitute.For<IS3ClientFactory>();
        var s3Client = Substitute.For<IAmazonS3>();
        GetPreSignedUrlRequest? capturedRequest = null;
        configResolver.ResolveAsync().Returns(config);
        clientFactory.CreatePresignClient(config).Returns(s3Client);
        s3Client.GetPreSignedURL(Arg.Do<GetPreSignedUrlRequest>(request => capturedRequest = request))
            .Returns("https://storage.example.test/presigned");
        var service = new ObjectStorageService(
            configResolver,
            clientFactory,
            new TestListLogger<ObjectStorageService>());

        await service.GeneratePresignedDownloadUrl(
            "tenants/example/document.pdf",
            "Quarterly report.pdf",
            15);

        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.ResponseHeaderOverrides.ContentDisposition)
            .IsEqualTo("attachment; filename*=utf-8''Quarterly%20report.pdf");
    }

    [Test]
    public async Task TestConnectionAsync_WhenProbeFails_LogsFailureTypeWithoutEndpointOrProviderPayload()
    {
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
        var service = new ObjectStorageService(configResolver, clientFactory, logger);

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
