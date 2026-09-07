using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Application.Models.Storage;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Storage;

public sealed class S3PreflightVerifier(
    IS3ConfigResolver configResolver,
    IS3ClientFactory clientFactory,
    ILogger<S3PreflightVerifier> logger) : IS3PreflightVerifier
{
    private const string EndpointStep = "Endpoint Reachability";
    private const string BucketStep = "Bucket Access";
    private const string WriteStep = "Write/Delete Permissions";
    private const string IdentityStep = "Account Identity";

    public async Task<S3PreflightResult> VerifyAsync(
        S3PreflightRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var config = request.Configuration ?? await configResolver.ResolveAsync(cancellationToken);
        if (config is null)
        {
            return MissingConfigurationResult();
        }

        var result = new S3PreflightResult
        {
            BucketName = config.BucketName,
            Endpoint = config.Endpoint
        };

        IAmazonS3 client;
        try
        {
            client = clientFactory.CreateDataClient(config);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or UriFormatException)
        {
            LogFailure("client_creation", exception);
            result.Steps.Add(Failed(EndpointStep, "s3_endpoint_invalid", "The S3-compatible endpoint configuration is invalid.", "Verify the endpoint URL and region."));
            AddSkippedRemainingSteps(result, "Endpoint validation did not pass.");
            return result;
        }

        using (client)
        {
            if (string.IsNullOrWhiteSpace(config.BucketName))
            {
                await VerifyIdentityWithoutBucketAsync(client, result, cancellationToken);
                return result;
            }

            if (!await VerifyBucketAsync(client, config, result, cancellationToken))
            {
                return result;
            }

            var writeSucceeded = !request.TestWritePermissions
                || await VerifyWriteAndDeleteAsync(client, config, result, cancellationToken);
            if (!request.TestWritePermissions)
            {
                result.Steps.Add(Skipped(WriteStep, "Write and delete permissions were not requested."));
            }

            result.Steps.Add(Skipped(IdentityStep, "Provider-specific identity checks are not required for S3 compatibility."));
            result.IsSuccess = result.CanRead && writeSucceeded;
            return result;
        }
    }

    private async Task<bool> VerifyBucketAsync(
        IAmazonS3 client,
        S3Configuration config,
        S3PreflightResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.HeadBucketAsync(
                new HeadBucketRequest { BucketName = config.BucketName },
                cancellationToken);

            result.CanRead = true;
            result.Steps.Add(Passed(EndpointStep, "The S3-compatible endpoint responded."));
            result.Steps.Add(Passed(BucketStep, "The target bucket is reachable with the configured credentials."));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode != 0)
        {
            LogFailure("head_bucket", exception);
            result.Steps.Add(Passed(EndpointStep, "The S3-compatible endpoint responded."));
            result.Steps.Add(MapBucketFailure(exception));
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            LogFailure("head_bucket", exception);
            result.Steps.Add(Failed(EndpointStep, "s3_endpoint_unreachable", "The S3-compatible endpoint could not be reached.", "Verify DNS, network access, TLS, endpoint URL, and region."));
            result.Steps.Add(Skipped(BucketStep, "Bucket access was not checked because the endpoint did not respond."));
        }

        result.Steps.Add(Skipped(WriteStep, "Write and delete permissions were not checked because bucket access failed."));
        result.Steps.Add(Skipped(IdentityStep, "Account identity was not checked."));
        return false;
    }

    private async Task<bool> VerifyWriteAndDeleteAsync(
        IAmazonS3 client,
        S3Configuration config,
        S3PreflightResult result,
        CancellationToken cancellationToken)
    {
        var objectKey = $".system/preflight-probe-{Guid.CreateVersion7():N}";
        var putRequest = new PutObjectRequest
        {
            BucketName = config.BucketName,
            Key = objectKey,
            InputStream = Stream.Null,
            AutoCloseStream = false,
            ContentType = "application/octet-stream"
        };
        putRequest.Headers.ContentLength = 0;

        try
        {
            await client.PutObjectAsync(putRequest, cancellationToken);
            result.CanWrite = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            LogFailure("put_object", exception);
            result.Steps.Add(MapObjectFailure(exception, "put"));
            return false;
        }

        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await client.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = config.BucketName,
                    Key = objectKey
                },
                cleanupTimeout.Token);
            result.Steps.Add(Passed(WriteStep, "A zero-byte probe object was written and deleted successfully."));
            return true;
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            LogFailure("delete_probe_object", exception);
            result.Steps.Add(MapObjectFailure(exception, "delete"));
            return false;
        }
    }

    private async Task VerifyIdentityWithoutBucketAsync(
        IAmazonS3 client,
        S3PreflightResult result,
        CancellationToken cancellationToken)
    {
        result.Steps.Add(Failed(BucketStep, "s3_bucket_required", "A target bucket is required for storage preflight.", "Configure the bucket name and retry."));
        result.Steps.Add(Skipped(WriteStep, "Write and delete permissions require a target bucket."));

        try
        {
            await client.ListBucketsAsync(cancellationToken);
            result.Steps.Insert(0, Passed(EndpointStep, "The S3-compatible endpoint responded."));
            result.Steps.Add(Passed(IdentityStep, "The configured credentials are accepted for bucket listing."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode != 0)
        {
            LogFailure("list_buckets", exception);
            result.Steps.Insert(0, Passed(EndpointStep, "The S3-compatible endpoint responded."));
            result.Steps.Add(new S3PreflightStepResult
            {
                StepName = IdentityStep,
                Status = S3PreflightStepStatus.Warning,
                ErrorCode = "s3_list_buckets_forbidden",
                Message = "Bucket listing is not permitted by the configured credentials.",
                Detail = "Provide a target bucket; bucket-scoped credentials do not need account-wide listing permission."
            });
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            LogFailure("list_buckets", exception);
            result.Steps.Insert(0, Failed(EndpointStep, "s3_endpoint_unreachable", "The S3-compatible endpoint could not be reached.", "Verify DNS, network access, TLS, endpoint URL, and region."));
            result.Steps.Add(Failed(IdentityStep, "s3_identity_unavailable", "Credential identity could not be checked.", "Configure a target bucket and verify endpoint connectivity."));
        }
    }

    private static S3PreflightStepResult MapBucketFailure(AmazonS3Exception exception) =>
        exception.StatusCode switch
        {
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
                Failed(BucketStep, "s3_bucket_forbidden", "The target bucket rejected the configured credentials.", "Verify the access key, secret key, bucket policy, and HeadBucket/ListBucket permission."),
            HttpStatusCode.NotFound =>
                Failed(BucketStep, "s3_bucket_not_found", "The target bucket does not exist at this endpoint.", "Verify the bucket name, endpoint, and region."),
            HttpStatusCode.BadRequest or HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect =>
                Failed(BucketStep, "s3_bucket_region_mismatch", "The target bucket rejected the endpoint or region.", "Verify the endpoint, region, bucket name, and path-style setting."),
            _ =>
                Failed(BucketStep, "s3_bucket_unavailable", "The target bucket could not be verified.", "Verify the bucket policy and provider configuration.")
        };

    private static S3PreflightStepResult MapObjectFailure(Exception exception, string operation)
    {
        var forbidden = exception is AmazonS3Exception s3Exception
            && s3Exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;
        var deleting = operation == "delete";

        return new S3PreflightStepResult
        {
            StepName = WriteStep,
            Status = deleting ? S3PreflightStepStatus.Warning : S3PreflightStepStatus.Failed,
            ErrorCode = forbidden
                ? deleting ? "s3_delete_forbidden" : "s3_put_forbidden"
                : deleting ? "s3_delete_failed" : "s3_put_failed",
            Message = deleting
                ? "The zero-byte probe was written, but its cleanup delete failed."
                : "The zero-byte write probe failed.",
            Detail = deleting
                ? "Remove the probe object manually if present and grant DeleteObject permission."
                : "Grant PutObject permission for the target bucket and retry."
        };
    }

    private static S3PreflightResult MissingConfigurationResult() => new()
    {
        Steps =
        [
            Failed(EndpointStep, "s3_not_configured", "S3-compatible storage is not fully configured.", "Configure endpoint, bucket, access key, and secret key."),
            Skipped(BucketStep, "Bucket access was not checked."),
            Skipped(WriteStep, "Write and delete permissions were not checked."),
            Skipped(IdentityStep, "Account identity was not checked.")
        ]
    };

    private static void AddSkippedRemainingSteps(S3PreflightResult result, string reason)
    {
        result.Steps.Add(Skipped(BucketStep, reason));
        result.Steps.Add(Skipped(WriteStep, reason));
        result.Steps.Add(Skipped(IdentityStep, reason));
    }

    private static S3PreflightStepResult Passed(string stepName, string message) => new()
    {
        StepName = stepName,
        Status = S3PreflightStepStatus.Passed,
        Message = message
    };

    private static S3PreflightStepResult Failed(
        string stepName,
        string errorCode,
        string message,
        string detail) => new()
        {
            StepName = stepName,
            Status = S3PreflightStepStatus.Failed,
            ErrorCode = errorCode,
            Message = message,
            Detail = detail
        };

    private static S3PreflightStepResult Skipped(string stepName, string message) => new()
    {
        StepName = stepName,
        Status = S3PreflightStepStatus.Skipped,
        Message = message
    };

    private static bool IsExpectedProviderFailure(Exception exception) =>
        exception is AmazonServiceException
            or HttpRequestException
            or IOException
            or TimeoutException
            or OperationCanceledException
            or InvalidOperationException;

    private void LogFailure(string operation, Exception exception) =>
        logger.LogWarning(
            "S3-compatible storage preflight failed. Operation={Operation} FailureType={FailureType}",
            operation,
            exception switch
            {
                AmazonS3Exception s3Exception when s3Exception.StatusCode != 0 => "provider_response",
                AmazonServiceException => "provider_service_error",
                HttpRequestException => "transport",
                IOException => "provider_io",
                TimeoutException or OperationCanceledException => "timeout",
                InvalidOperationException or ArgumentException or UriFormatException => "configuration",
                _ => "unknown"
            });
}
