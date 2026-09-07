namespace Explore.Application.DTOs.Onboarding;

/// <summary>
/// Response for the setup secret validation endpoint.
/// </summary>
public sealed record SecretValidationResponseDto
{
    public bool Valid { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Response for storage and SMTP connection test endpoints.
/// </summary>
public sealed record ConnectionTestResponseDto
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Response for the auth provider configuration status check endpoint.
/// </summary>
public sealed record AuthProviderConfiguredResponseDto
{
    public bool Configured { get; init; }
}
