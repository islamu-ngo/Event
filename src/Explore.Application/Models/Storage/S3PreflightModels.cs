using Explore.Application.Models;

namespace Explore.Application.Models.Storage;

public sealed class S3PreflightRequest
{
    public S3Configuration? Configuration { get; set; }
    public bool TestWritePermissions { get; set; }
}

public sealed class S3PreflightStepResult
{
    public string StepName { get; set; } = string.Empty;
    public string Status { get; set; } = S3PreflightStepStatus.Skipped;
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
}

public sealed class S3PreflightResult
{
    public bool IsSuccess { get; set; }
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
    public string BucketName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public List<S3PreflightStepResult> Steps { get; set; } = [];
}

public static class S3PreflightStepStatus
{
    public const string Passed = "Passed";
    public const string Failed = "Failed";
    public const string Warning = "Warning";
    public const string Skipped = "Skipped";
}
