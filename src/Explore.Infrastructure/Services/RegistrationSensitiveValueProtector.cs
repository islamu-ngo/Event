using System.Text;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Services;

public sealed class RegistrationSensitiveValueProtector(IDataProtectionProvider provider)
    : IRegistrationSensitiveValueProtector
{
    public const int CurrentKeyVersion = 1;
    private readonly IDataProtector _protector = provider.CreateProtector(
        "Explore.RegistrationSensitiveAnswerValue",
        $"v{CurrentKeyVersion}");

    public RegistrationProtectedValue Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        byte[] protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(plaintext));
        return new(Convert.ToBase64String(protectedBytes), CurrentKeyVersion);
    }

    public string Unprotect(string ciphertext, int keyVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        if (keyVersion != CurrentKeyVersion)
        {
            throw new InvalidOperationException("The registration value purpose version is unavailable.");
        }

        return Encoding.UTF8.GetString(_protector.Unprotect(Convert.FromBase64String(ciphertext)));
    }
}
