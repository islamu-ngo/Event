using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management.Requests.Queries;
using Explore.Application.Management;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Options;

namespace Explore.Application.Features.Management.Handlers.Queries;

public sealed class GetManagementCapabilitiesQueryHandler(
    IOptions<ManagedControlPlaneOptions> options,
    IInstanceBootstrapStateRepository bootstrapStateRepository,
    IManagedControlPlaneRegistrationRepository registrationRepository,
    ManagedTenantProvisioningCapacityReader tenantProvisioningCapacityReader)
    : IRequestHandler<GetManagementCapabilitiesQuery, ManagementCapabilitiesDto>
{
    public async Task<ManagementCapabilitiesDto> Handle(
        GetManagementCapabilitiesQuery request,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return new ManagementCapabilitiesDto(
                false,
                ManagedControlPlaneContract.ManagementApiVersion,
                ManagementVersionResolver.EventVersion,
                DeploymentMode.SingleTenant,
                null,
                "Disabled",
                [],
                null);
        }

        var bootstrap = await bootstrapStateRepository.GetCurrent(cancellationToken);
        var registration = await registrationRepository.GetCurrentAsync(cancellationToken);
        DeploymentMode mode = bootstrap?.Status == InstanceBootstrapStatus.Completed
            ? bootstrap.DeploymentMode
            : DeploymentMode.SingleTenant;
        ManagementTenantProvisioningCapacityDto? capacity = mode == DeploymentMode.MultiTenant
            && options.Value.MaximumTenantCount > 0
                ? await tenantProvisioningCapacityReader.ReadAsync(cancellationToken, mode)
                : null;
        IReadOnlyList<string> capabilities = capacity is not null
                ? ManagedControlPlaneContract.Capabilities
                    .Concat(ManagedControlPlaneContract.TenantProvisioningCapabilities)
                    .ToArray()
                : ManagedControlPlaneContract.Capabilities;

        return new ManagementCapabilitiesDto(
            true,
            ManagedControlPlaneContract.ManagementApiVersion,
            ManagementVersionResolver.EventVersion,
            mode,
            bootstrap is { Status: InstanceBootstrapStatus.Completed } ? bootstrap.Id : null,
            registration?.Status.ToString() ?? "Unregistered",
            capabilities,
            capacity);
    }
}

public sealed class GetManagedEventInstanceStatusQueryHandler(
    IOptions<ManagedControlPlaneOptions> options,
    IDeploymentModeProvider deploymentModeProvider,
    IManagedControlPlaneRegistrationRepository registrationRepository)
    : IRequestHandler<GetManagedEventInstanceStatusQuery, ManagedEventInstanceStatusDto?>
{
    public async Task<ManagedEventInstanceStatusDto?> Handle(
        GetManagedEventInstanceStatusQuery request,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return null;
        }

        var registration = await registrationRepository.GetCurrentAsync(cancellationToken);
        if (registration is null)
        {
            return null;
        }

        var mode = await deploymentModeProvider.GetCurrentModeAsync(cancellationToken);

        return new ManagedEventInstanceStatusDto(
            registration.ManagedInstanceId,
            registration.EventInstanceId,
            mode,
            ManagementVersionResolver.EventVersion,
            ManagedControlPlaneContract.ManagementApiVersion,
            registration.Status.ToString(),
            registration.RegisteredAt,
            registration.EventToControlPlaneCredentialExpiresAt,
            registration.ControlPlaneToEventCredentialExpiresAt);
    }
}

public sealed class GetManagementHealthQueryHandler(IManagedEventHealthProbe healthProbe)
    : IRequestHandler<GetManagementHealthQuery, ManagementHealthDto>
{
    public async Task<ManagementHealthDto> Handle(
        GetManagementHealthQuery request,
        CancellationToken cancellationToken)
    {
        var health = await healthProbe.CheckAsync(cancellationToken);
        return new ManagementHealthDto(health.Status, health.ObservedAt);
    }
}

public sealed class GetManagementUpgradePreflightQueryHandler(
    IOptions<ManagedControlPlaneOptions> options,
    IDeploymentModeProvider deploymentModeProvider,
    IInstanceBootstrapStateRepository bootstrapStateRepository,
    IManagedControlPlaneRegistrationRepository registrationRepository,
    IManagedEventHealthProbe healthProbe)
    : IRequestHandler<GetManagementUpgradePreflightQuery, ManagementUpgradePreflightDto>
{
    public async Task<ManagementUpgradePreflightDto> Handle(
        GetManagementUpgradePreflightQuery request,
        CancellationToken cancellationToken)
    {
        var currentVersion = ManagementVersionResolver.EventVersion;
        var targetVersion = request.TargetEventVersion?.Trim() ?? string.Empty;
        var targetApiVersion = request.TargetManagementApiVersion?.Trim() ?? string.Empty;
        var now = DateTime.UtcNow;
        var blockers = new List<ManagementUpgradeBlockerDto>();
        if (!options.Value.Enabled)
        {
            ManagementUpgradeAssessment.AddBlocker(
                blockers,
                "managed_mode_disabled",
                "Managed mode is disabled on this Event instance.");
            return new ManagementUpgradePreflightDto(
                null,
                null,
                DeploymentMode.SingleTenant,
                currentVersion,
                targetVersion,
                ManagedControlPlaneContract.ManagementApiVersion,
                targetApiVersion,
                "Disabled",
                "Unavailable",
                false,
                blockers,
                now);
        }

        var registration = await registrationRepository.GetCurrentAsync(cancellationToken);
        var bootstrap = await bootstrapStateRepository.GetCurrent(cancellationToken);
        var mode = await deploymentModeProvider.GetCurrentModeAsync(cancellationToken);
        var health = await healthProbe.CheckAsync(cancellationToken);
        ManagementUpgradeAssessment.AddCommonBlockers(
            blockers,
            registration,
            bootstrap is { Status: InstanceBootstrapStatus.Completed },
            mode,
            health,
            now);
        ManagementUpgradeAssessment.AddPreflightVersionBlockers(
            blockers,
            currentVersion,
            targetVersion,
            targetApiVersion);

        return new ManagementUpgradePreflightDto(
            registration?.ManagedInstanceId,
            registration?.EventInstanceId,
            mode,
            currentVersion,
            targetVersion,
            ManagedControlPlaneContract.ManagementApiVersion,
            targetApiVersion,
            registration?.Status.ToString() ?? "Unregistered",
            health.Status,
            blockers.Count == 0,
            blockers,
            health.ObservedAt);
    }
}

public sealed class GetManagementUpgradePostflightQueryHandler(
    IOptions<ManagedControlPlaneOptions> options,
    IDeploymentModeProvider deploymentModeProvider,
    IInstanceBootstrapStateRepository bootstrapStateRepository,
    IManagedControlPlaneRegistrationRepository registrationRepository,
    IManagedEventHealthProbe healthProbe)
    : IRequestHandler<GetManagementUpgradePostflightQuery, ManagementUpgradePostflightDto>
{
    public async Task<ManagementUpgradePostflightDto> Handle(
        GetManagementUpgradePostflightQuery request,
        CancellationToken cancellationToken)
    {
        var currentVersion = ManagementVersionResolver.EventVersion;
        var expectedVersion = request.ExpectedEventVersion?.Trim() ?? string.Empty;
        var expectedApiVersion = request.ExpectedManagementApiVersion?.Trim() ?? string.Empty;
        var now = DateTime.UtcNow;
        var blockers = new List<ManagementUpgradeBlockerDto>();
        if (!options.Value.Enabled)
        {
            ManagementUpgradeAssessment.AddBlocker(
                blockers,
                "managed_mode_disabled",
                "Managed mode is disabled on this Event instance.");
            return new ManagementUpgradePostflightDto(
                null,
                null,
                DeploymentMode.SingleTenant,
                currentVersion,
                expectedVersion,
                ManagedControlPlaneContract.ManagementApiVersion,
                expectedApiVersion,
                "Disabled",
                "Unavailable",
                false,
                blockers,
                now);
        }

        var registration = await registrationRepository.GetCurrentAsync(cancellationToken);
        var bootstrap = await bootstrapStateRepository.GetCurrent(cancellationToken);
        var mode = await deploymentModeProvider.GetCurrentModeAsync(cancellationToken);
        var health = await healthProbe.CheckAsync(cancellationToken);
        ManagementUpgradeAssessment.AddCommonBlockers(
            blockers,
            registration,
            bootstrap is { Status: InstanceBootstrapStatus.Completed },
            mode,
            health,
            now);
        ManagementUpgradeAssessment.AddPostflightVersionBlockers(
            blockers,
            currentVersion,
            expectedVersion,
            expectedApiVersion);

        return new ManagementUpgradePostflightDto(
            registration?.ManagedInstanceId,
            registration?.EventInstanceId,
            mode,
            currentVersion,
            expectedVersion,
            ManagedControlPlaneContract.ManagementApiVersion,
            expectedApiVersion,
            registration?.Status.ToString() ?? "Unregistered",
            health.Status,
            blockers.Count == 0,
            blockers,
            health.ObservedAt);
    }
}

internal static class ManagementUpgradeAssessment
{
    public static void AddCommonBlockers(
        ICollection<ManagementUpgradeBlockerDto> blockers,
        ManagedControlPlaneRegistration? registration,
        bool bootstrapCompleted,
        DeploymentMode currentMode,
        ManagedEventHealthObservation health,
        DateTime observedAt)
    {
        if (registration?.Status != ManagedControlPlaneRegistrationStatus.Registered)
        {
            AddBlocker(
                blockers,
                "managed_registration_unavailable",
                "An active managed Control Plane registration is required.");
        }
        else
        {
            if (registration.ControlPlaneToEventCredentialExpiresAt <= observedAt)
            {
                AddBlocker(
                    blockers,
                    "control_plane_credential_expired",
                    "The inbound Control Plane credential has expired.");
            }

            if (registration.EventToControlPlaneCredentialExpiresAt <= observedAt)
            {
                AddBlocker(
                    blockers,
                    "event_callback_credential_expired",
                    "The Event callback credential has expired.");
            }

            if (!string.Equals(
                    registration.ManagementApiVersion,
                    ManagedControlPlaneContract.ManagementApiVersion,
                    StringComparison.Ordinal))
            {
                AddBlocker(
                    blockers,
                    "registration_contract_stale",
                    "The registered management API contract does not match this Event runtime.");
            }

            if (registration.DeploymentMode != currentMode)
            {
                AddBlocker(
                    blockers,
                    "deployment_mode_changed",
                    "The current Event deployment mode does not match the registered managed-mode identity.");
            }
        }

        if (!bootstrapCompleted)
        {
            AddBlocker(
                blockers,
                "instance_bootstrap_incomplete",
                "Event instance bootstrap must be complete before upgrade validation.");
        }

        if (!string.Equals(health.Status, "Healthy", StringComparison.Ordinal))
        {
            AddBlocker(
                blockers,
                "instance_health_not_ready",
                "Aggregate Event readiness must be healthy.");
        }
    }

    public static void AddPreflightVersionBlockers(
        ICollection<ManagementUpgradeBlockerDto> blockers,
        string currentVersion,
        string targetVersion,
        string targetApiVersion)
    {
        var currentVersionIsValid = TryParseSemanticVersion(currentVersion, out var current);
        if (!currentVersionIsValid)
        {
            AddBlocker(
                blockers,
                "current_version_unavailable",
                "The current Event runtime version is not a supported semantic version.");
        }

        if (!TryParseSemanticVersion(targetVersion, out var target))
        {
            AddBlocker(
                blockers,
                "target_version_invalid",
                "The target Event version must contain a semantic numeric core.");
        }
        else if (currentVersionIsValid && target.CompareTo(current) < 0)
        {
            AddBlocker(
                blockers,
                "target_version_older",
                "The target Event version cannot be older than the current runtime.");
        }

        AddManagementApiBlocker(blockers, targetApiVersion, "target_management_api_incompatible");
    }

    public static void AddPostflightVersionBlockers(
        ICollection<ManagementUpgradeBlockerDto> blockers,
        string currentVersion,
        string expectedVersion,
        string expectedApiVersion)
    {
        if (!VersionsMatch(currentVersion, expectedVersion))
        {
            AddBlocker(
                blockers,
                "event_version_mismatch",
                "The observed Event runtime does not match the expected upgraded version.");
        }

        AddManagementApiBlocker(blockers, expectedApiVersion, "management_api_version_mismatch");
    }

    public static void AddBlocker(
        ICollection<ManagementUpgradeBlockerDto> blockers,
        string code,
        string message) =>
        blockers.Add(new ManagementUpgradeBlockerDto(code, message));

    private static void AddManagementApiBlocker(
        ICollection<ManagementUpgradeBlockerDto> blockers,
        string candidate,
        string code)
    {
        if (!string.Equals(
                candidate,
                ManagedControlPlaneContract.ManagementApiVersion,
                StringComparison.Ordinal))
        {
            AddBlocker(
                blockers,
                code,
                "The requested management API version is not supported by this Event runtime.");
        }
    }

    private static bool VersionsMatch(string currentVersion, string expectedVersion)
    {
        if (!TryParseSemanticVersion(currentVersion, out var current)
            || !TryParseSemanticVersion(expectedVersion, out var expected))
        {
            return false;
        }

        return current.CompareTo(expected) == 0;
    }

    private static bool TryParseSemanticVersion(string value, out SemanticVersion version)
    {
        version = default;
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 100)
        {
            return false;
        }

        var buildParts = normalized.Split('+');
        if (buildParts.Length > 2
            || (buildParts.Length == 2 && !IdentifiersAreValid(buildParts[1], false)))
        {
            return false;
        }

        var versionAndPrerelease = buildParts[0];
        var prereleaseSeparator = versionAndPrerelease.IndexOf('-');
        var core = prereleaseSeparator < 0
            ? versionAndPrerelease
            : versionAndPrerelease[..prereleaseSeparator];
        var prerelease = prereleaseSeparator < 0
            ? []
            : versionAndPrerelease[(prereleaseSeparator + 1)..].Split('.');
        var segments = core.Split('.');
        if (segments.Length != 3
            || segments.Any(segment => !TryParseCoreIdentifier(segment, out _))
            || (prerelease.Length > 0 && !IdentifiersAreValid(prerelease, true)))
        {
            return false;
        }

        version = new SemanticVersion(
            int.Parse(segments[0], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(segments[1], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(segments[2], System.Globalization.CultureInfo.InvariantCulture),
            prerelease);
        return true;
    }

    private static bool TryParseCoreIdentifier(string value, out int parsed)
    {
        parsed = 0;
        return value.Length > 0
            && (value.Length == 1 || value[0] != '0')
            && int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out parsed);
    }

    private static bool IdentifiersAreValid(string value, bool rejectNumericLeadingZero) =>
        IdentifiersAreValid(value.Split('.'), rejectNumericLeadingZero);

    private static bool IdentifiersAreValid(
        IReadOnlyCollection<string> identifiers,
        bool rejectNumericLeadingZero) =>
        identifiers.Count > 0
        && identifiers.All(identifier =>
            identifier.Length > 0
            && identifier.All(character =>
                character is >= '0' and <= '9'
                or >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or '-')
            && (!rejectNumericLeadingZero
                || !IsNumericIdentifier(identifier)
                || identifier.Length == 1
                || identifier[0] != '0'));

    private static bool IsNumericIdentifier(string value) =>
        value.All(character => character is >= '0' and <= '9');

    private readonly record struct SemanticVersion(
        int Major,
        int Minor,
        int Patch,
        IReadOnlyList<string> Prerelease) : IComparable<SemanticVersion>
    {
        public int CompareTo(SemanticVersion other)
        {
            var coreComparison = Major.CompareTo(other.Major);
            if (coreComparison == 0)
            {
                coreComparison = Minor.CompareTo(other.Minor);
            }

            if (coreComparison == 0)
            {
                coreComparison = Patch.CompareTo(other.Patch);
            }

            if (coreComparison != 0)
            {
                return coreComparison;
            }

            if (Prerelease.Count == 0 || other.Prerelease.Count == 0)
            {
                return Prerelease.Count == other.Prerelease.Count
                    ? 0
                    : Prerelease.Count == 0 ? 1 : -1;
            }

            for (var index = 0; index < Math.Min(Prerelease.Count, other.Prerelease.Count); index++)
            {
                var identifierComparison = CompareIdentifier(Prerelease[index], other.Prerelease[index]);
                if (identifierComparison != 0)
                {
                    return identifierComparison;
                }
            }

            return Prerelease.Count.CompareTo(other.Prerelease.Count);
        }

        private static int CompareIdentifier(string left, string right)
        {
            var leftIsNumeric = IsNumericIdentifier(left);
            var rightIsNumeric = IsNumericIdentifier(right);
            if (leftIsNumeric && rightIsNumeric)
            {
                var lengthComparison = left.Length.CompareTo(right.Length);
                return lengthComparison != 0
                    ? lengthComparison
                    : string.Compare(left, right, StringComparison.Ordinal);
            }

            if (leftIsNumeric != rightIsNumeric)
            {
                return leftIsNumeric ? -1 : 1;
            }

            return string.Compare(left, right, StringComparison.Ordinal);
        }
    }
}
