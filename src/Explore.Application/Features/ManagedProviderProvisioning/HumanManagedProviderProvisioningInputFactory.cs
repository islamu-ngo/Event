using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.ManagedProviderProvisioning.Validators;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;

namespace Explore.Application.Features.ManagedProviderProvisioning;

/// <summary>Validates and snapshots human provisioning values before native DATA observation.</summary>
public static class HumanManagedProviderProvisioningInputFactory
{
    public static async Task<HumanManagedProviderProvisioningInput> CaptureAsync(
        EnsureManagedProviderClientProvisionedCommand command, CancellationToken cancellationToken)
    {
        if (command.ManagementRequest is not null || command.OperationId is not null || command.ExpectedOutboxMessageId is not null)
            throw new UnauthorizedAccessException("Human DATA does not own managed provisioning generations.");
        var dto = command.ProvisioningDto ?? throw new ArgumentException("Provisioning input is required.", nameof(command));
        var validation = await new ManagedProviderClientProvisioningDtoValidator().ValidateAsync(dto, cancellationToken);
        if (!validation.IsValid) throw new ArgumentException("Provisioning input is invalid.", nameof(command));
        var external = dto.ExternalAdmin;
        var organizer = dto.Organizer;
        // Every leaf is a scalar or an immutable record; mapping captures the nested
        // values without retaining transport DTOs or replacing the original command.
        return new(
            dto.ProviderKey.Trim(),
            dto.ExternalSystem.Trim(),
            dto.ExternalCustomerId.Trim(),
            dto.TenantFullName.Trim(),
            dto.TenantSlug.Trim().ToLowerInvariant(),
            dto.ActivateTenant,
            dto.DirectoryOperatorIdentity?.ToPayload(),
            external is null ? null : new(
                external.IdentityProvider.Trim(),
                external.Subject,
                external.Email.Trim(),
                external.FirstName.Trim(),
                external.LastName.Trim(),
                external.EmailVerified,
                DisplayName(external)),
            dto.LocalIdentity is null ? null : new(dto.LocalIdentity.LocalSubjectId),
            organizer is null ? null : new(
                organizer.Kind switch
                {
                    ManagedProviderOrganizerKindDto.Organization => ManagedProviderOrganizerKind.Organization,
                    ManagedProviderOrganizerKindDto.Group => ManagedProviderOrganizerKind.Group,
                    _ => throw new ArgumentException("Provisioning input is invalid.", nameof(command))
                },
                organizer.FullName.Trim(),
                organizer.Email?.Trim(),
                organizer.WebsiteUrl?.Trim(),
                organizer.Country?.Trim(),
                organizer.City?.Trim(),
                organizer.Address?.Trim(),
                organizer.Postcode?.Trim(),
                organizer.Description?.Trim()));
    }

    private static string DisplayName(ManagedProviderExternalAdminDto admin)
    {
        if (!string.IsNullOrWhiteSpace(admin.DisplayName)) return admin.DisplayName.Trim();
        string name = $"{admin.FirstName.Trim()} {admin.LastName.Trim()}".Trim();
        return string.IsNullOrWhiteSpace(name) ? admin.Email.Trim() : name;
    }
}
