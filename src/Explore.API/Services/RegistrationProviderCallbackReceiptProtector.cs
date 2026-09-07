using System.Text.Json;
using Explore.Application.Contracts.Services.Registration;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.API.Services;

public sealed class RegistrationProviderCallbackReceiptProtector(IDataProtectionProvider provider)
    : IRegistrationProviderCallbackReceiptProtector
{
    public const int CurrentPurposeVersion = 1;
    private readonly IDataProtector _protector = provider.CreateProtector(
        "Explore.RegistrationProviderCallbackReceipt",
        $"v{CurrentPurposeVersion}");

    public string Protect(RegistrationProviderCallbackReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return _protector.Protect(JsonSerializer.Serialize(receipt));
    }

    public RegistrationProviderCallbackReceipt Unprotect(string protectedReceipt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedReceipt);
        string json = _protector.Unprotect(protectedReceipt);
        RegistrationProviderCallbackReceipt receipt = JsonSerializer.Deserialize<RegistrationProviderCallbackReceipt>(json)
            ?? throw new InvalidOperationException("Registration callback receipt is empty.");
        if (receipt.TenantId == Guid.Empty ||
            receipt.ConnectionId == Guid.Empty ||
            receipt.BindingId == Guid.Empty ||
            string.IsNullOrWhiteSpace(receipt.Provider) ||
            string.IsNullOrWhiteSpace(receipt.TupleKey) ||
            string.IsNullOrWhiteSpace(receipt.BodySha256) ||
            string.IsNullOrWhiteSpace(receipt.ProviderSubmissionId) ||
            string.IsNullOrWhiteSpace(receipt.Nonce))
        {
            throw new InvalidOperationException("Registration callback receipt is malformed.");
        }

        return receipt;
    }
}
