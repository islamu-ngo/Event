
namespace Explore.Application.Models;

public enum SmtpDeliveryOutcome
{
    /// <summary>Acceptance cannot be established; automatic resend could duplicate delivery.</summary>
    Uncertain = 0,
    /// <summary>The SMTP server acknowledged the message; later disconnect failures do not undo it.</summary>
    Accepted,
    /// <summary>No send began, or the server explicitly rejected delivery with a temporary failure.</summary>
    TransientFailure,
    /// <summary>Authentication, configuration, or permanent rejection requires correction.</summary>
    ConfigurationFailure,
    /// <summary>The included contact expired before SMTP send began; no acceptance is possible.</summary>
    RetentionExpired
}
