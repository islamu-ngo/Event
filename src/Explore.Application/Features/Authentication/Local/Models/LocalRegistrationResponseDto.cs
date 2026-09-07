namespace Explore.Application.Features.Authentication.Local.Models;

public sealed record LocalRegistrationResponseDto
{
    private LocalRegistrationResponseDto(
        bool success,
        string failureCode,
        LocalAuthResponseDto? authentication)
    {
        Success = success;
        FailureCode = failureCode;
        Authentication = authentication;
    }

    public bool Success { get; }
    public string FailureCode { get; }
    public LocalAuthResponseDto? Authentication { get; }

    public static LocalRegistrationResponseDto Failed(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        return new LocalRegistrationResponseDto(false, failureCode, null);
    }

    public static LocalRegistrationResponseDto Registered(LocalAuthResponseDto authentication)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        if (!authentication.Success)
        {
            throw new ArgumentException(
                "A successful registration requires an authenticated session.",
                nameof(authentication));
        }

        return new LocalRegistrationResponseDto(true, string.Empty, authentication);
    }
}
