using Explore.Domain.ValueObjects;

namespace Explore.Application.Contracts.Services;

public interface IGuestCapabilityTokenService
{
    GuestCapabilityTokenIssue Issue();

    bool Matches(string? rawToken, CapabilityTokenHash expectedHash);
}

public sealed record GuestCapabilityTokenIssue(string RawToken, CapabilityTokenHash Hash)
{
    // A token is a bearer capability, not identity proof; never log, persist, or return plaintext after issuance.
    public override string ToString() => "GuestCapabilityTokenIssue { Redacted = true }";
}
