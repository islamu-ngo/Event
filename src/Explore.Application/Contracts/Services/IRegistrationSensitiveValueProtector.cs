namespace Explore.Application.Contracts.Services;

public interface IRegistrationSensitiveValueProtector
{
    RegistrationProtectedValue Protect(string plaintext);
    string Unprotect(string ciphertext, int keyVersion);
}

public sealed record RegistrationProtectedValue(string Ciphertext, int KeyVersion);
