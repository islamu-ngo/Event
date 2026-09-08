namespace Explore.Application.Contracts.Admissions;

public sealed record AdmissionScannerAuthenticationRequest(string Capability)
{
    public override string ToString() => "AdmissionScannerAuthenticationRequest(<redacted>)";
}

public sealed record AdmissionScannerAuthenticationResult
{
    private AdmissionScannerAuthenticationResult(
        bool authenticated,
        Guid scannerCapabilityId,
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        IReadOnlyList<AdmissionCheckInAction> actions)
    {
        Authenticated = authenticated;
        ScannerCapabilityId = scannerCapabilityId;
        TenantId = tenantId;
        EventId = eventId;
        TargetId = targetId;
        Actions = actions;
    }

    public bool Authenticated { get; }
    public Guid ScannerCapabilityId { get; }
    public Guid TenantId { get; }
    public Guid EventId { get; }
    public Guid TargetId { get; }
    public IReadOnlyList<AdmissionCheckInAction> Actions { get; }

    public static AdmissionScannerAuthenticationResult Failed() =>
        new(false, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, []);

    public static AdmissionScannerAuthenticationResult Success(
        Guid scannerCapabilityId,
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        IReadOnlyList<AdmissionCheckInAction> actions)
    {
        if (scannerCapabilityId == Guid.Empty || tenantId == Guid.Empty || eventId == Guid.Empty ||
            targetId == Guid.Empty || actions is null or { Count: 0 } ||
            actions.Any(action => !Enum.IsDefined(action)) ||
            actions.Distinct().Count() != actions.Count)
        {
            throw new ArgumentException("Complete scanner authentication authority is required.");
        }

        return new AdmissionScannerAuthenticationResult(
            true,
            scannerCapabilityId,
            tenantId,
            eventId,
            targetId,
            actions.ToArray());
    }

    public override string ToString() =>
        $"AdmissionScannerAuthenticationResult(authenticated={Authenticated}, <redacted>)";
}

public interface IAdmissionScannerAuthenticationService
{
    Task<AdmissionScannerAuthenticationResult> AuthenticateAsync(
        AdmissionScannerAuthenticationRequest request,
        CancellationToken cancellationToken);
}
