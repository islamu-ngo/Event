// ABOUTME: Rich email message DTO supporting HTML, plain text, attachments, CC/BCC,
// reply-to, and custom headers. Provider-agnostic — works with any SMTP server.

namespace Explore.Application.Models;

/// <summary>
/// Represents an email message to be sent via the provider-agnostic email service.
/// </summary>
public class EmailMessage
{
    /// <summary>Operational contact deadline; null leaves ordinary email delivery unchanged.</summary>
    public DateTime? DisclosureUntilUtc { get; init; }

    /// <summary>Primary recipient email address.</summary>
    public required string To { get; set; }

    /// <summary>Carbon copy recipients.</summary>
    public List<string> Cc { get; set; } = [];

    /// <summary>Blind carbon copy recipients.</summary>
    public List<string> Bcc { get; set; } = [];

    /// <summary>Reply-to email address (optional).</summary>
    public string? ReplyTo { get; set; }

    /// <summary>Email subject line.</summary>
    public required string Subject { get; set; }

    /// <summary>HTML body content (optional, recommended for rich emails).</summary>
    public string? HtmlBody { get; set; }

    /// <summary>Plain text body content (optional, used as fallback).</summary>
    public string? PlainTextBody { get; set; }

    /// <summary>File attachments and inline images.</summary>
    public List<EmailAttachment> Attachments { get; set; } = [];

    /// <summary>Custom SMTP headers (e.g., X-Mailer, List-Unsubscribe).</summary>
    public Dictionary<string, string> CustomHeaders { get; set; } = [];

    /// <summary>Override sender email address. If null, uses SMTP config default.</summary>
    public string? FromAddress { get; set; }

    /// <summary>Override sender display name. If null, uses SMTP config default.</summary>
    public string? FromName { get; set; }
}
