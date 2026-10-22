using Explore.Application.Contracts.Services;
using Explore.Application.Features.Tenants.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Creates the original authenticated human's complete tenant graph. Managed workers
/// have a separate issuer and cannot acquire or borrow this port's authority.
/// </summary>
public interface IHumanTenantCreationPort
{
    Task<HumanTenantCreationResult> CreateAsync(CreateTenantCommand request,
        CancellationToken cancellationToken = default);

    /// <summary>Validates an already acquired issuer-owned authority; never issues authority.</summary>
    Task RequireCurrentAsync(IHumanTenantCreationAuthority authority, HumanTenantCreationPlan plan,
        CancellationToken cancellationToken = default);
}

/// <summary>Opaque, single-attempt authority; implementing this interface does not confer permission.</summary>
public interface IHumanTenantCreationAuthority
{
}

/// <summary>Complete immutable effect identities, not a database role or an authorization assertion.</summary>
public sealed record HumanTenantCreationPlan(
    TenantCreationRequest Creation,
    Guid? AdministratorUserId,
    Guid? AdministratorMembershipId,
    Guid? AdministratorGrantId,
    bool CapacityEnforced,
    int MaximumTenantCount);

/// <summary>Fixed graph writer; there is deliberately no arbitrary-entity or callback executor.</summary>
public interface IHumanTenantCreationGraphWriter
{
    Task<HumanTenantCreationResult> WriteAsync(IHumanTenantCreationAuthority authority,
        HumanTenantCreationPlan plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads only this concrete writer's completed original effect. The returned
    /// image is comparison data, never a new authority or a caller-issued receipt.
    /// </summary>
    HumanTenantCreationAdministratorEffect? ReadCompletedAdministratorEffect(
        IHumanTenantCreationAuthority authority, HumanTenantCreationPlan plan);
}

public sealed record HumanTenantCreationAdministratorEffect(Guid TenantId, Guid UserId,
    Guid MembershipId, Guid GrantId, int RoleId, int RoleScopeId, string RoleMasterCode,
    int TenantStatusId, bool TenantIsActive);

public sealed record HumanTenantCreationResult
{
    private HumanTenantCreationResult(TenantCreationOutcome? creation, bool administratorAssigned,
        string? failureCode, string? error) =>
        (Creation, AdministratorAssigned, FailureCode, Error) =
            (creation, administratorAssigned, failureCode, error);

    public TenantCreationOutcome? Creation { get; }
    public bool AdministratorAssigned { get; }
    public string? FailureCode { get; }
    public string? Error { get; }

    public static HumanTenantCreationResult Created(TenantCreationOutcome creation, bool administratorAssigned) =>
        new(creation ?? throw new ArgumentNullException(nameof(creation)), administratorAssigned, null, null);

    public static HumanTenantCreationResult Refused(string failureCode, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new(null, false, failureCode, error);
    }
}
