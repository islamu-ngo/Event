// ABOUTME: Setting definitions for non-secret email and SMTP delivery governance.
// ABOUTME: Authentication credentials are intentionally absent and externally resolved.

namespace Explore.Domain.Settings.Definitions;

public static class EmailSettingDefinitions
{
    public static readonly SettingDefinition DeliveryEnabled = new(
        Key: "email.delivery_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Email",
        Description: "Explicitly enable outbound email using the effective transport owner",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition SmtpHost = new(
        Key: "email.smtp_host",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Email",
        Description: "SMTP server hostname (e.g., smtp.gmail.com, smtp.mailgun.org)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition SmtpPort = new(
        Key: "email.smtp_port",
        ValueType: SettingValueType.Integer,
        DefaultValue: "587",
        Category: "Email",
        Description: "SMTP server port (587 for StartTLS, 465 for SSL, 25 for unencrypted)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition SmtpSecurity = new(
        Key: "email.smtp_security",
        ValueType: SettingValueType.String,
        DefaultValue: "\"StartTls\"",
        Category: "Email",
        Description: "SMTP connection security mode",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["None", "StartTls", "SslOnConnect", "Auto"]);

    public static readonly SettingDefinition FromAddress = new(
        Key: "email.from_address",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Email",
        Description: "Default sender email address for outbound emails",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition FromName = new(
        Key: "email.from_name",
        ValueType: SettingValueType.String,
        DefaultValue: "\"Explore\"",
        Category: "Email",
        Description: "Default sender display name for outbound emails",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition SmtpTimeoutSeconds = new(
        Key: "email.smtp_timeout_seconds",
        ValueType: SettingValueType.Integer,
        DefaultValue: "30",
        Category: "Email",
        Description: "SMTP connection timeout in seconds",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition SmtpSkipCertValidation = new(
        Key: "email.smtp_skip_cert_validation",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Email",
        Description: "Skip TLS certificate validation (development/self-signed certs only)",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        DeliveryEnabled, SmtpHost, SmtpPort, SmtpSecurity,
        FromAddress, FromName, SmtpTimeoutSeconds, SmtpSkipCertValidation
    ];
}
