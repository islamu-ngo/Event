using Microsoft.AspNetCore.DataProtection;

namespace Explore.API.OpenApi;

/// <summary>
/// Allows schema-only middleware construction without acquiring deployment keys.
/// The document generator cannot encrypt or decrypt runtime data.
/// </summary>
internal sealed class OpenApiGenerationDataProtectionProvider : IDataProtectionProvider, IDataProtector
{
    public IDataProtector CreateProtector(string purpose) => this;

    public byte[] Protect(byte[] plaintext) =>
        throw new InvalidOperationException("openapi_data_protection_unavailable");

    public byte[] Unprotect(byte[] protectedData) =>
        throw new InvalidOperationException("openapi_data_protection_unavailable");
}
