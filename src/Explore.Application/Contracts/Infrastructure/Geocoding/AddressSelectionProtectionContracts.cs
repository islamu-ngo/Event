namespace Explore.Application.Contracts.Infrastructure.Geocoding;

public interface IAddressSelectionProtector
{
    string ConfigurationFingerprint { get; }

    Task<AddressSelectionToken> ProtectAsync(
        ProtectedAddressSelection selection,
        AddressSelectionContext context,
        CancellationToken cancellationToken);

    Task<AddressSelectionUnprotectResult> UnprotectAsync(
        string token,
        AddressSelectionContext context,
        CancellationToken cancellationToken);
}

public enum AddressSelectionPurpose
{
    CreateLocation = 1,
    UpdateLocation = 2
}

public enum AddressSelectionFailureCode
{
    None = 0,
    Invalid = 1,
    Expired = 2
}

public sealed record AddressSelectionTarget
{
    public Guid? LocationId { get; init; }
    public Guid? ExpectedConcurrencyStamp { get; init; }
}

public sealed record AddressSelectionContext
{
    public required Guid TenantId { get; init; }
    public required Guid ActorId { get; init; }
    public Guid? OrganizationId { get; init; }
    public required AddressSelectionPurpose Purpose { get; init; }
    public required AddressSelectionTarget Target { get; init; }
    public required string ConfigurationFingerprint { get; init; }
}

public sealed record ProtectedAddressProvenance
{
    public required string Provider { get; init; }
    public string? ProviderRecordId { get; init; }
    public string? DatasetVersion { get; init; }
}

public sealed record ProtectedAddressSelection
{
    public required string DisplayName { get; init; }
    public required string Address { get; init; }
    public required string Postcode { get; init; }
    public required string City { get; init; }
    public required string Country { get; init; }
    public string? Timezone { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required string Attribution { get; init; }
    public required ProtectedAddressProvenance Provenance { get; init; }
}

public sealed record AddressSelectionToken(string Value, DateTimeOffset ExpiresAt);

public sealed record AddressSelectionUnprotectResult(
    ProtectedAddressSelection? Selection,
    AddressSelectionFailureCode FailureCode)
{
    public bool IsSuccess =>
        Selection is not null && FailureCode == AddressSelectionFailureCode.None;

    public static AddressSelectionUnprotectResult Success(ProtectedAddressSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new(selection, AddressSelectionFailureCode.None);
    }

    public static AddressSelectionUnprotectResult Failure(AddressSelectionFailureCode code)
    {
        if (code == AddressSelectionFailureCode.None)
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        return new(null, code);
    }
}
