// Captures success/failure, error details, and timing diagnostics.

namespace Explore.Application.Models;

/// <summary>
/// Result of an email send or SMTP connection test operation.
/// </summary>
public class EmailResult
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Descriptive message (success note or error detail).</summary>
    public string? Message { get; set; }

    /// <summary>Error message on failure. Null on success.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>How long the operation took.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Creates a success result.</summary>
    public static EmailResult Ok(string? message = null, TimeSpan duration = default)
        => new() { Success = true, Message = message, Duration = duration };

    /// <summary>Creates a failure result.</summary>
    public static EmailResult Fail(string errorMessage, TimeSpan duration = default)
        => new() { Success = false, ErrorMessage = errorMessage, Duration = duration };
}
