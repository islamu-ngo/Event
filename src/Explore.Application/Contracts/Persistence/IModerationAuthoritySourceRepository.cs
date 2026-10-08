using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Establishes native installation provenance under current server-resolved instance authority.
/// The implementation owns qualification, source fences, replay and the primary-store commit.
/// No source identifier grants moderation permission or establishes an active policy.
/// </summary>
public interface IModerationAuthoritySourceRepository
{
    Task<ModerationAuthoritySourceResult> EstablishAsync(
        ModerationAuthoritySourceInstallation installation, CancellationToken cancellationToken = default);
}

public sealed record ModerationAuthoritySourceInstallation(
    Guid HostBindingId,
    Guid TrustDomainId,
    Guid SolutionId,
    Guid InstanceId,
    Guid MaintenanceServicePrincipalId,
    Guid TenantId,
    Guid EpochId);

public enum ModerationAuthoritySourceOutcome
{
    Unavailable,
    Established,
    Unauthenticated,
    Denied,
    Conflict
}

/// <summary>Native source entities, never presentation DTOs. Failed outcomes carry no source authority.</summary>
public sealed record ModerationAuthoritySourceResult
{
    private ModerationAuthoritySourceResult(ModerationAuthoritySourceOutcome outcome,
        ModerationHostBinding? binding, ModerationQualificationEpoch? epoch)
    {
        Outcome = outcome;
        Binding = binding;
        Epoch = epoch;
    }

    public ModerationAuthoritySourceOutcome Outcome { get; }
    public ModerationHostBinding? Binding { get; }
    public ModerationQualificationEpoch? Epoch { get; }

    public static ModerationAuthoritySourceResult Established(
        ModerationHostBinding binding, ModerationQualificationEpoch epoch)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(epoch);
        if (epoch.HostBindingId != binding.Id || epoch.Revision <= 0)
            throw new ArgumentException("Established sources require matching ownership and positive qualification.");
        return new(ModerationAuthoritySourceOutcome.Established, binding, epoch);
    }

    public static ModerationAuthoritySourceResult Unavailable() => new(ModerationAuthoritySourceOutcome.Unavailable, null, null);
    public static ModerationAuthoritySourceResult Unauthenticated() => new(ModerationAuthoritySourceOutcome.Unauthenticated, null, null);
    public static ModerationAuthoritySourceResult Denied() => new(ModerationAuthoritySourceOutcome.Denied, null, null);
    public static ModerationAuthoritySourceResult Conflict() => new(ModerationAuthoritySourceOutcome.Conflict, null, null);
}
