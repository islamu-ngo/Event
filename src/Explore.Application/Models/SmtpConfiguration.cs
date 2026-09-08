// Supports any SMTP provider (SendGrid, SES, Mailgun, Office 365, self-hosted Postfix, etc.).

namespace Explore.Application.Models;

/// <summary>
/// SMTP connection parameters resolved from SystemSetting/TenantSetting.
/// Instance admin can lock settings (IsLocked) to enforce a SaaS-wide SMTP server,
/// or leave unlocked so tenants can bring their own SMTP provider.
/// </summary>
public class SmtpConfiguration
{
    /// <summary>SMTP server hostname (e.g., "smtp.sendgrid.net").</summary>
    public required string Host { get; set; }

    /// <summary>SMTP port. 587=STARTTLS (recommended), 465=implicit SSL, 25=unencrypted.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Username for SMTP authentication. For SendGrid, this is literally "apikey".</summary>
    public string? Username { get; set; }

    /// <summary>Password or API key for SMTP authentication. Decrypted from AppSetting.</summary>
    public string? Password { get; set; }

    /// <summary>TLS/SSL mode for the connection.</summary>
    public SmtpSecurityMode Security { get; set; } = SmtpSecurityMode.StartTls;

    /// <summary>Default sender email address.</summary>
    public required string FromAddress { get; set; }

    /// <summary>Default sender display name.</summary>
    public string FromName { get; set; } = "Explore";

    /// <summary>Connection timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Skip TLS certificate validation (for self-signed certs in dev/self-hosted).</summary>
    public bool SkipCertificateValidation { get; set; }
}

/// <summary>
/// TLS/SSL mode for SMTP connections.
/// Stored as string in SystemSetting (e.g., "StartTls").
/// </summary>
public enum SmtpSecurityMode
{
    /// <summary>No encryption (port 25, not recommended).</summary>
    None = 0,

    /// <summary>STARTTLS upgrade (port 587, recommended).</summary>
    StartTls = 1,

    /// <summary>Implicit SSL/TLS (port 465).</summary>
    SslOnConnect = 2,

    /// <summary>Auto-detect based on port.</summary>
    Auto = 3
}
