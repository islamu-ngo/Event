namespace Explore.Blazor.Client.Models.Responses;

/// <summary>
/// Discriminated result type for service operations.
/// Distinguishes between success (with data), empty success (no data), and failure (with error details).
/// </summary>
/// <typeparam name="T">The type of the data payload on success.</typeparam>
public sealed record ServiceResult<T>
{
    /// <summary>Whether the operation completed without error.</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>The data payload. Null on failure or when no data is available.</summary>
    public T? Data { get; private init; }

    /// <summary>Error message for display to users. Null on success.</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>HTTP status code from API response, if applicable.</summary>
    public int? StatusCode { get; private init; }

    /// <summary>Whether the error is an authentication failure (401/403).</summary>
    public bool IsAuthError => StatusCode is 401 or 403;

    /// <summary>Whether the resource was not found (404).</summary>
    public bool IsNotFound => StatusCode == 404;

    /// <summary>Creates a successful result with data.</summary>
    public static ServiceResult<T> Success(T data) => new()
    {
        IsSuccess = true,
        Data = data
    };

    /// <summary>Creates a failure result with an error message and optional HTTP status code.</summary>
    public static ServiceResult<T> Failure(string errorMessage, int? statusCode = null) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage,
        StatusCode = statusCode
    };

    /// <summary>Creates a failure from an ApiException (NSwag generated).</summary>
    public static ServiceResult<T> FromApiException(Exception ex)
    {
        // NSwag-generated ApiException has StatusCode property
        var statusCode = ex.GetType().GetProperty("StatusCode")?.GetValue(ex) as int?;
        var message = statusCode switch
        {
            401 => "You are not authenticated. Please log in again.",
            403 => "You don't have permission to perform this action.",
            404 => "The requested resource was not found.",
            500 => "A server error occurred. Please try again later.",
            _ => $"An error occurred: {ex.Message}"
        };

        return Failure(message, statusCode);
    }

    /// <summary>Creates a failure from a general exception.</summary>
    public static ServiceResult<T> FromException(Exception ex) => new()
    {
        IsSuccess = false,
        ErrorMessage = $"An unexpected error occurred: {ex.Message}"
    };
}

/// <summary>
/// Non-generic result for operations that don't return data (e.g., delete, update).
/// </summary>
public sealed record ServiceResult
{
    public bool IsSuccess { get; private init; }
    public string? ErrorMessage { get; private init; }
    public int? StatusCode { get; private init; }
    public bool IsAuthError => StatusCode is 401 or 403;

    public static ServiceResult Success() => new() { IsSuccess = true };

    public static ServiceResult Failure(string errorMessage, int? statusCode = null) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage,
        StatusCode = statusCode
    };

    public static ServiceResult FromApiException(Exception ex)
    {
        var statusCode = ex.GetType().GetProperty("StatusCode")?.GetValue(ex) as int?;
        var message = statusCode switch
        {
            401 => "You are not authenticated. Please log in again.",
            403 => "You don't have permission to perform this action.",
            404 => "The requested resource was not found.",
            500 => "A server error occurred. Please try again later.",
            _ => $"An error occurred: {ex.Message}"
        };
        return Failure(message, statusCode);
    }

    public static ServiceResult FromException(Exception ex) => new()
    {
        IsSuccess = false,
        ErrorMessage = $"An unexpected error occurred: {ex.Message}"
    };
}
