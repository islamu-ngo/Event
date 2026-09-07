using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Application.Models.Storage;
using Explore.Infrastructure.Storage;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class S3PreflightVerifierTests
{
    [Test]
    public async Task VerifyAsync_WhenBucketIsReachable_ReturnsReadOnlySuccessWithoutWriting()
    {
        var client = Substitute.For<IAmazonS3>();
        client.HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns(new HeadBucketResponse());
        var verifier = CreateVerifier(CreateConfig(), client);

        var result = await verifier.VerifyAsync(new S3PreflightRequest(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.CanRead).IsTrue();
        await Assert.That(result.CanWrite).IsFalse();
        await Assert.That(result.Steps.Single(step => step.StepName == "Endpoint Reachability").Status)
            .IsEqualTo(S3PreflightStepStatus.Passed);
        await Assert.That(result.Steps.Single(step => step.StepName == "Bucket Access").Status)
            .IsEqualTo(S3PreflightStepStatus.Passed);
        await Assert.That(result.Steps.Single(step => step.StepName == "Write/Delete Permissions").Status)
            .IsEqualTo(S3PreflightStepStatus.Skipped);
        await client.DidNotReceive().PutObjectAsync(
            Arg.Any<PutObjectRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task VerifyAsync_WhenHeadBucketIsForbidden_ReportsReachableEndpointAndCredentialFailure()
    {
        var client = Substitute.For<IAmazonS3>();
        client.HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<HeadBucketResponse>>(_ => throw new AmazonS3Exception("provider detail")
            {
                StatusCode = HttpStatusCode.Forbidden
            });
        var verifier = CreateVerifier(CreateConfig(), client);

        var result = await verifier.VerifyAsync(new S3PreflightRequest(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Steps.Single(step => step.StepName == "Endpoint Reachability").Status)
            .IsEqualTo(S3PreflightStepStatus.Passed);
        var bucketStep = result.Steps.Single(step => step.StepName == "Bucket Access");
        await Assert.That(bucketStep.Status).IsEqualTo(S3PreflightStepStatus.Failed);
        await Assert.That(bucketStep.ErrorCode).IsEqualTo("s3_bucket_forbidden");
        await Assert.That(bucketStep.Message).DoesNotContain("provider detail");
    }

    [Test]
    public async Task VerifyAsync_WhenHeadBucketIsNotFound_ReportsMissingBucket()
    {
        var client = Substitute.For<IAmazonS3>();
        client.HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<HeadBucketResponse>>(_ => throw new AmazonS3Exception("provider detail")
            {
                StatusCode = HttpStatusCode.NotFound
            });
        var verifier = CreateVerifier(CreateConfig(), client);

        var result = await verifier.VerifyAsync(new S3PreflightRequest(), CancellationToken.None);

        var bucketStep = result.Steps.Single(step => step.StepName == "Bucket Access");
        await Assert.That(bucketStep.ErrorCode).IsEqualTo("s3_bucket_not_found");
        await Assert.That(bucketStep.Message).Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task VerifyAsync_WithWriteProbe_UploadsAndDeletesZeroByteMarker()
    {
        var client = Substitute.For<IAmazonS3>();
        client.HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns(new HeadBucketResponse());
        client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteObjectResponse());
        PutObjectRequest? putRequest = null;
        DeleteObjectRequest? deleteRequest = null;
        client.PutObjectAsync(
                Arg.Do<PutObjectRequest>(request => putRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        client.DeleteObjectAsync(
                Arg.Do<DeleteObjectRequest>(request => deleteRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(new DeleteObjectResponse());
        var verifier = CreateVerifier(CreateConfig(), client);

        var result = await verifier.VerifyAsync(
            new S3PreflightRequest { TestWritePermissions = true },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.CanWrite).IsTrue();
        await Assert.That(putRequest).IsNotNull();
        await Assert.That(putRequest!.Key).StartsWith(".system/preflight-probe-");
        await Assert.That(putRequest.Headers.ContentLength).IsEqualTo(0);
        await Assert.That(deleteRequest).IsNotNull();
        await Assert.That(deleteRequest!.Key).IsEqualTo(putRequest.Key);
        await Assert.That(result.Steps.Single(step => step.StepName == "Write/Delete Permissions").Status)
            .IsEqualTo(S3PreflightStepStatus.Passed);
    }

    [Test]
    public async Task VerifyAsync_WhenCleanupFails_ReportsWarningWithoutProviderDetail()
    {
        var client = Substitute.For<IAmazonS3>();
        client.HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns(new HeadBucketResponse());
        client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<DeleteObjectResponse>>(_ => throw new AmazonS3Exception("secret provider response")
            {
                StatusCode = HttpStatusCode.Forbidden
            });
        var verifier = CreateVerifier(CreateConfig(), client);

        var result = await verifier.VerifyAsync(
            new S3PreflightRequest { TestWritePermissions = true },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.CanWrite).IsTrue();
        var writeStep = result.Steps.Single(step => step.StepName == "Write/Delete Permissions");
        await Assert.That(writeStep.Status).IsEqualTo(S3PreflightStepStatus.Warning);
        await Assert.That(writeStep.ErrorCode).IsEqualTo("s3_delete_forbidden");
        await Assert.That(writeStep.Message).DoesNotContain("secret provider response");
    }

    [Test]
    public async Task VerifyAsync_WithoutBucket_UsesProviderNeutralBucketListingIdentityFallback()
    {
        var config = CreateConfig();
        config.BucketName = string.Empty;
        var client = Substitute.For<IAmazonS3>();
        client.ListBucketsAsync(Arg.Any<CancellationToken>()).Returns(new ListBucketsResponse());
        var verifier = CreateVerifier(config, client);

        var result = await verifier.VerifyAsync(
            new S3PreflightRequest { Configuration = config },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Steps.Single(step => step.StepName == "Account Identity").Status)
            .IsEqualTo(S3PreflightStepStatus.Passed);
        await Assert.That(result.Steps.Single(step => step.StepName == "Bucket Access").ErrorCode)
            .IsEqualTo("s3_bucket_required");
        await client.Received(1).ListBucketsAsync(Arg.Any<CancellationToken>());
    }

    private static S3PreflightVerifier CreateVerifier(S3Configuration config, IAmazonS3 client)
    {
        var configResolver = Substitute.For<IS3ConfigResolver>();
        var clientFactory = Substitute.For<IS3ClientFactory>();
        configResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(config);
        clientFactory.CreateDataClient(config).Returns(client);
        return new S3PreflightVerifier(
            configResolver,
            clientFactory,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<S3PreflightVerifier>.Instance);
    }

    private static S3Configuration CreateConfig() => new()
    {
        Endpoint = "https://s3.example.test",
        BucketName = "tenant-files",
        AccessKeyId = "test-access-key",
        SecretAccessKey = "test-secret-key",
        Region = "us-east-1",
        ForcePathStyle = true
    };
}
