// ABOUTME: Result type for email send and connection test operations.
// ABOUTME: Captures typed handoff evidence, safe error details, and timing diagnostics.

namespace Explore.Application.Models;

/// <summary>
/// Result of an email send or SMTP connection test operation.
/// </summary>
public class EmailResult
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool Success => Outcome == SmtpDeliveryOutcome.Accepted;

    /// <summary>Transport evidence; an unspecified failure is conservatively uncertain.</summary>
    public SmtpDeliveryOutcome Outcome { get; init; }

    /// <summary>Descriptive message (success note or error detail).</summary>
    public string? Message { get; set; }

    /// <summary>Error message on failure. Null on success.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>How long the operation took.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Creates a success result.</summary>
    public static EmailResult Ok(string? message = null, TimeSpan duration = default)
        => new() { Outcome = SmtpDeliveryOutcome.Accepted, Message = message, Duration = duration };

    /// <summary>Creates a failure result; acceptance is only represented by a successful result.</summary>
    public static EmailResult Fail(string errorMessage, TimeSpan duration = default,
        SmtpDeliveryOutcome outcome = SmtpDeliveryOutcome.Uncertain)
    {
        if (outcome == SmtpDeliveryOutcome.Accepted)
            throw new ArgumentException("A failure cannot indicate SMTP acceptance.", nameof(outcome));

        return new() { Outcome = outcome, ErrorMessage = errorMessage, Duration = duration };
    }
}
