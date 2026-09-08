// Used by EmailMessage for the provider-agnostic email service.

namespace Explore.Application.Models;

/// <summary>
/// Represents an email attachment or inline image.
/// </summary>
public class EmailAttachment
{
    /// <summary>File name for the attachment.</summary>
    public required string FileName { get; set; }

    /// <summary>Raw file content as bytes.</summary>
    public required byte[] Content { get; set; }

    /// <summary>MIME content type (e.g., "application/pdf", "image/png").</summary>
    public required string ContentType { get; set; }

    /// <summary>Whether this is an inline image (embedded in HTML body).</summary>
    public bool IsInline { get; set; }

    /// <summary>Content-ID for inline images, referenced in HTML as cid:ContentId.</summary>
    public string? ContentId { get; set; }
}
