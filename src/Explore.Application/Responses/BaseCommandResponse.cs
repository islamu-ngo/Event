using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Explore.Application.Responses;

public static class BaseCommandResponse
{
    public static BaseCommandResponse<TKey> Success<TKey>(TKey id, string? message = null)
    {
        RequireId(id);
        return new BaseCommandResponse<TKey>(id, true, message, null, null, null);
    }

    public static BaseCommandResponse<TKey> Validation<TKey>(
        IEnumerable<string> errors,
        string? message = null,
        TKey? id = default)
    {
        ReadOnlyCollection<string> snapshot = SnapshotRequiredErrors(errors);
        return new BaseCommandResponse<TKey>(id, false, message ?? snapshot[0], snapshot, null, null);
    }

    public static BaseCommandResponse<TKey> NotFound<TKey>(string? message = null, TKey? id = default) =>
        new(id, false, message, null, FailureCodes.NotFound, null);

    public static BaseCommandResponse<TKey> Conflict<TKey>(TKey id, string? message = null)
    {
        RequireId(id);
        return new BaseCommandResponse<TKey>(
            id,
            false,
            message,
            null,
            FailureCodes.ConcurrencyConflict,
            null);
    }

    public static BaseCommandResponse<TKey> Authorization<TKey>(string? message = null) =>
        new(default, false, message, null, FailureCodes.AdminRequired, null);

    public static BaseCommandResponse<TKey> Authentication<TKey>(string? message = null) =>
        new(default, false, message, null, FailureCodes.AuthenticationRequired, null);

    public static BaseCommandResponse<TKey> Quota<TKey>(
        string message,
        QuotaExceededDetails quotaExceeded,
        string? error = null,
        TKey? id = default)
    {
        ArgumentNullException.ThrowIfNull(quotaExceeded);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (error is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(error);
        }

        return new BaseCommandResponse<TKey>(
            id,
            false,
            message,
            SnapshotRequiredErrors([error ?? quotaExceeded.ToErrorMessage()]),
            FailureCodes.QuotaExceeded,
            quotaExceeded);
    }

    public static BaseCommandResponse<TKey> Failure<TKey>(
        string failureCode,
        string? message = null,
        IEnumerable<string>? errors = null,
        TKey? id = default)
    {
        ValidateFeatureFailureCode(failureCode);
        return new BaseCommandResponse<TKey>(
            id,
            false,
            message,
            SnapshotOptionalErrors(errors),
            failureCode,
            null);
    }

    internal static BaseCommandResponse<TKey> Restore<TKey>(
        TKey? id,
        bool isSuccess,
        string? message,
        IReadOnlyList<string>? errors,
        string? failureCode,
        QuotaExceededDetails? quotaExceeded) =>
        new(id, isSuccess, message, errors, failureCode, quotaExceeded);

    internal static BaseCommandResponse<TKey> RequireFailure<TKey>(BaseCommandResponse<TKey> failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (failure.IsSuccess)
        {
            throw new ArgumentException("A concrete failure response requires a failed base state.", nameof(failure));
        }

        return failure;
    }

    internal static void ValidateFeatureFailureCode(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (failureCode is FailureCodes.QuotaExceeded
            or FailureCodes.NotFound
            or FailureCodes.ConcurrencyConflict
            or FailureCodes.AdminRequired
            or FailureCodes.AuthenticationRequired)
        {
            throw new ArgumentException(
                "The failure code is owned by a named command response factory.",
                nameof(failureCode));
        }
    }

    internal static void RequireId<TKey>(TKey? id)
    {
        if (id is null)
        {
            throw new ArgumentNullException(nameof(id));
        }
    }

    private static ReadOnlyCollection<string> SnapshotRequiredErrors(IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        string[] snapshot = errors.ToArray();
        ValidateErrors(snapshot);
        return Array.AsReadOnly(snapshot);
    }

    private static ReadOnlyCollection<string>? SnapshotOptionalErrors(IEnumerable<string>? errors) =>
        errors is null ? null : SnapshotRequiredErrors(errors);

    private static void ValidateErrors(string[] errors)
    {
        if (errors.Length == 0 || errors.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-blank error is required.", nameof(errors));
        }
    }
}

public record BaseCommandResponse<TKey>
{
    [JsonConstructor]
    internal BaseCommandResponse(
        TKey? id,
        bool isSuccess,
        string? message,
        IReadOnlyList<string>? errors,
        string? failureCode,
        QuotaExceededDetails? quotaExceeded)
    {
        ValidateState(id, isSuccess, message, errors, failureCode, quotaExceeded);
        Id = id;
        IsSuccess = isSuccess;
        Message = message;
        Errors = Snapshot(errors);
        FailureCode = failureCode;
        QuotaExceeded = quotaExceeded;
    }

    protected BaseCommandResponse(BaseCommandResponse<TKey> state, bool _)
        : this(
            state.Id,
            state.IsSuccess,
            state.Message,
            state.Errors,
            state.FailureCode,
            state.QuotaExceeded)
    {
    }

    public TKey? Id { get; }

    [JsonPropertyName("success")]
    public bool IsSuccess { get; }

    public string? Message { get; }
    public IReadOnlyList<string>? Errors { get; }

    /// <summary>Machine-readable standard or feature-owned failure code.</summary>
    public string? FailureCode { get; }

    /// <summary>Structured quota metadata for quota failures.</summary>
    public QuotaExceededDetails? QuotaExceeded { get; }

    private static void ValidateState(
        TKey? id,
        bool isSuccess,
        string? message,
        IReadOnlyList<string>? errors,
        string? failureCode,
        QuotaExceededDetails? quotaExceeded)
    {
        if (message is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
        }

        if (isSuccess)
        {
            BaseCommandResponse.RequireId(id);
            if (errors is not null || failureCode is not null || quotaExceeded is not null)
            {
                throw new ArgumentException("A successful response cannot contain failure state.");
            }

            return;
        }

        if (failureCode is null)
        {
            ValidateErrors(errors);
            if (message is null)
            {
                throw new ArgumentException("A validation failure requires a message.", nameof(message));
            }

            if (quotaExceeded is not null)
            {
                throw new ArgumentException("Quota metadata requires the standard quota failure code.");
            }

            return;
        }

        if (failureCode == FailureCodes.QuotaExceeded)
        {
            ArgumentNullException.ThrowIfNull(quotaExceeded);
            ValidateErrors(errors);
            if (errors!.Count != 1 || message is null)
            {
                throw new ArgumentException("A quota failure requires one error and a message.");
            }

            return;
        }

        if (failureCode is FailureCodes.NotFound
            or FailureCodes.ConcurrencyConflict
            or FailureCodes.AdminRequired
            or FailureCodes.AuthenticationRequired)
        {
            if (failureCode == FailureCodes.ConcurrencyConflict)
            {
                BaseCommandResponse.RequireId(id);
            }

            if (errors is not null || quotaExceeded is not null)
            {
                throw new ArgumentException("A named failure cannot contain validation or quota state.");
            }

            if (failureCode is FailureCodes.AdminRequired or FailureCodes.AuthenticationRequired
                && !EqualityComparer<TKey?>.Default.Equals(id, default))
            {
                throw new ArgumentException(
                    "Authentication and authorization failures cannot identify a result.",
                    nameof(id));
            }

            return;
        }

        BaseCommandResponse.ValidateFeatureFailureCode(failureCode);
        if (quotaExceeded is not null)
        {
            throw new ArgumentException("A feature failure cannot contain quota metadata.", nameof(quotaExceeded));
        }

        if (errors is not null)
        {
            ValidateErrors(errors);
        }
    }

    private static ReadOnlyCollection<string>? Snapshot(IReadOnlyList<string>? errors) =>
        errors is null ? null : new ReadOnlyCollection<string>(errors.ToArray());

    private static void ValidateErrors(IReadOnlyList<string>? errors)
    {
        if (errors is null || errors.Count == 0 || errors.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-blank error is required.", nameof(errors));
        }
    }
}
