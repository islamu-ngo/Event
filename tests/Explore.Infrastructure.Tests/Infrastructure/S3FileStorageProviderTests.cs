using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class S3FileStorageProviderTests
{
    [Test]
    public async Task WriteAsync_StreamsToS3WithGeneratedTenantKeyAndChecksum()
    {
        var config = CreateConfig();
        var s3Client = Substitute.For<IAmazonS3>();
        var provider = CreateProvider(config, s3Client);
        var tenantId = Guid.CreateVersion7();
        PutObjectRequest? capturedRequest = null;
        byte[]? capturedBytes = null;

        s3Client
            .PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedRequest = call.Arg<PutObjectRequest>();
                using var buffer = new MemoryStream();
                capturedRequest.InputStream.CopyTo(buffer);
                capturedBytes = buffer.ToArray();
                return Task.FromResult(new PutObjectResponse());
            });

        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("storage payload"));
        var result = await provider.WriteAsync(
            new FileStorageWriteInput(
                tenantId,
                content,
                "text/plain",
                "ignored.txt",
                ".TXT",
                ExpectedSizeBytes: 15,
                MaxSizeBytes: 1024),
            CancellationToken.None);

        await Assert.That(result.Provider).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(result.ObjectKey).StartsWith($"tenants/{tenantId:N}/");
        await Assert.That(result.ObjectKey).EndsWith(".txt");
        await Assert.That(result.SizeBytes).IsEqualTo(15);
        await Assert.That(result.Sha256Checksum).IsEqualTo("5e1c7766758f09dc15399c4c444a9c5734cf49bda9797f31c2f42af3be2fbbaa");
        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.BucketName).IsEqualTo(config.BucketName);
        await Assert.That(capturedRequest.ContentType).IsEqualTo("text/plain");
        await Assert.That(capturedRequest.Headers.ContentLength).IsEqualTo(15);
        await Assert.That(capturedRequest.InputStream.CanSeek).IsFalse();
        await Assert.That(capturedBytes).IsEquivalentTo(Encoding.UTF8.GetBytes("storage payload"));
    }

    [Test]
    public async Task WriteAsync_WithReservedTenantObjectKey_UsesExactKey()
    {
        var config = CreateConfig();
        var s3Client = Substitute.For<IAmazonS3>();
        var provider = CreateProvider(config, s3Client);
        var tenantId = Guid.CreateVersion7();
        var objectKey = $"tenants/{tenantId:N}/uploads/{Guid.CreateVersion7():N}.txt";
        PutObjectRequest? capturedRequest = null;
        s3Client
            .PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedRequest = call.Arg<PutObjectRequest>();
                capturedRequest.InputStream.CopyTo(Stream.Null);
                return Task.FromResult(new PutObjectResponse());
            });
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("reserved"));

        FileStorageWriteResult result = await provider.WriteAsync(
            new FileStorageWriteInput(
                tenantId,
                content,
                "text/plain",
                "reserved.txt",
                ".txt",
                8,
                8,
                objectKey),
            CancellationToken.None);

        await Assert.That(result.ObjectKey).IsEqualTo(objectKey);
        await Assert.That(capturedRequest!.Key).IsEqualTo(objectKey);
    }

    [Test]
    public async Task WriteAsync_WithAnotherTenantObjectKey_RejectsBeforeS3Call()
    {
        var s3Client = Substitute.For<IAmazonS3>();
        var provider = CreateProvider(CreateConfig(), s3Client);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("rejected"));

        await Assert.ThrowsAsync<ArgumentException>(() => provider.WriteAsync(
            new FileStorageWriteInput(
                Guid.CreateVersion7(),
                content,
                "text/plain",
                "rejected.txt",
                ".txt",
                8,
                8,
                $"tenants/{Guid.CreateVersion7():N}/uploads/rejected.txt"),
            CancellationToken.None));

        await s3Client.DidNotReceive().PutObjectAsync(
            Arg.Any<PutObjectRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OpenReadAsync_ReturnsS3ResponseStreamAndMetadata()
    {
        var s3Client = Substitute.For<IAmazonS3>();
        var provider = CreateProvider(CreateConfig(), s3Client);
        var body = new MemoryStream(Encoding.UTF8.GetBytes("read me"));
        var response = new GetObjectResponse
        {
            ResponseStream = body,
            LastModified = DateTime.UtcNow
        };
        response.Headers.ContentType = "text/plain";
        response.Headers.ContentLength = 7;

        s3Client
            .GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await provider.OpenReadAsync(new FileStorageReadInput("tenants/key.txt", null), CancellationToken.None);

        await using (result.Content)
        {
            using var reader = new StreamReader(result.Content, Encoding.UTF8);
            await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("read me");
        }

        await Assert.That(result.ContentType).IsEqualTo("text/plain");
        await Assert.That(result.Length).IsEqualTo(7);
    }

    [Test]
    public async Task DeleteAsync_SubmitsDeleteAndReturnsAcceptedResult()
    {
        var config = CreateConfig();
        var s3Client = Substitute.For<IAmazonS3>();
        var provider = CreateProvider(config, s3Client);

        s3Client
            .DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteObjectResponse());

        var result = await provider.DeleteAsync(new FileStorageDeleteInput("tenants/key.txt"), CancellationToken.None);

        await Assert.That(result.Provider).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(result.ObjectKey).IsEqualTo("tenants/key.txt");
        await Assert.That(result.Deleted).IsTrue();
        await s3Client.Received(1).DeleteObjectAsync(
            Arg.Is<DeleteObjectRequest>(request => request.BucketName == config.BucketName && request.Key == "tenants/key.txt"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TestAsync_WhenConfigMissing_ReturnsUnavailableWithoutCreatingClient()
    {
        var configResolver = Substitute.For<IS3ConfigResolver>();
        var clientFactory = Substitute.For<IS3ClientFactory>();
        var preflightVerifier = new S3PreflightVerifier(
            configResolver,
            clientFactory,
            NullLogger<S3PreflightVerifier>.Instance);
        var provider = new S3FileStorageProvider(
            configResolver,
            clientFactory,
            preflightVerifier);

        var status = await provider.TestAsync(CancellationToken.None);

        await Assert.That(status.Provider).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(status.IsAvailable).IsFalse();
        await Assert.That(status.FailureCode).IsEqualTo("s3_not_configured");
        clientFactory.DidNotReceiveWithAnyArgs().CreateDataClient(default!);
    }

    [Test]
    public async Task TestAsync_WhenBucketReachable_ReturnsAvailable()
    {
        var config = CreateConfig();
        var s3Client = Substitute.For<IAmazonS3>();
        var provider = CreateProvider(config, s3Client);

        s3Client
            .HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns(new HeadBucketResponse());

        var status = await provider.TestAsync(CancellationToken.None);

        await Assert.That(status.Provider).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(status.IsAvailable).IsTrue();
        await Assert.That(status.SupportsServerSideStreaming).IsTrue();
        await Assert.That(status.SupportsBrowserDirectUpload).IsTrue();
    }

    [Test]
    public async Task TestAsync_WhenBucketProbeFails_LogsFailureTypeWithoutRawProviderPayload()
    {
        var config = CreateConfig();
        var s3Client = Substitute.For<IAmazonS3>();
        var logger = new TestListLogger<S3PreflightVerifier>();
        var provider = CreateProvider(config, s3Client, logger);
        s3Client
            .HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<HeadBucketResponse>>(_ => throw new InvalidOperationException(
                $"provider leaked endpoint {config.Endpoint} bucket {config.BucketName} secret {config.SecretAccessKey}"));

        var status = await provider.TestAsync(CancellationToken.None);

        await Assert.That(status.IsAvailable).IsFalse();
        await Assert.That(status.FailureCode).IsEqualTo("s3_endpoint_unreachable");

        var log = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        await Assert.That(log.Exception).IsNull();
        await Assert.That(log.Message).Contains("FailureType=configuration");
        await Assert.That(log.Message).DoesNotContain("provider leaked endpoint");
        await Assert.That(log.Message).DoesNotContain(config.Endpoint);
        await Assert.That(log.Message).DoesNotContain(config.BucketName);
        await Assert.That(log.Message).DoesNotContain(config.SecretAccessKey);
    }

    private static S3FileStorageProvider CreateProvider(
        S3Configuration config,
        IAmazonS3 s3Client,
        ILogger<S3PreflightVerifier>? logger = null)
    {
        var configResolver = Substitute.For<IS3ConfigResolver>();
        var clientFactory = Substitute.For<IS3ClientFactory>();
        configResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(config);
        clientFactory.CreateDataClient(config).Returns(s3Client);

        var preflightVerifier = new S3PreflightVerifier(
            configResolver,
            clientFactory,
            logger ?? NullLogger<S3PreflightVerifier>.Instance);
        return new S3FileStorageProvider(configResolver, clientFactory, preflightVerifier);
    }

    private static S3Configuration CreateConfig()
        => new()
        {
            Endpoint = "https://s3.example.test",
            BucketName = "tenant-files",
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            Region = "us-east-1",
            ForcePathStyle = true
        };
}
