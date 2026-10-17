using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Domain.ValueObjects;
using Explore.Application.Features.Settings;

namespace Explore.Application.Features.TenantOnboarding.Handlers.Commands;

public class CompleteTenantOnboardingCommandHandler : ICommandHandler<CompleteTenantOnboardingCommand, BaseCommandResponse<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantOnboardingStateRepository _tenantOnboardingStateRepository;
    private readonly IAdminContext _adminContext;
    private readonly ITenantPolicySettingService _policySettingService;
    private readonly ITenantBrandingSettingsDocumentProvisioningService _tenantBrandingProvisioningService;
    private readonly ITenantSettingsDocumentRepository _tenantSettingsDocumentRepository;
    private readonly ITypedSettingsDocumentResolver _typedSettingsDocumentResolver;
    private readonly IHumanSettingsMutationPreparation _preparation;
    private readonly ITypedSettingsOriginalStore _originalStore;
    private readonly IHierarchicalSettingsResolver _hierarchicalSettingsResolver;
    private readonly IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> _notificationHandlers;

    public CompleteTenantOnboardingCommandHandler(
        ITenantContext tenantContext,
        ITenantOnboardingStateRepository tenantOnboardingStateRepository,
        IAdminContext adminContext,
        ITenantPolicySettingService policySettingService,
        ITenantBrandingSettingsDocumentProvisioningService tenantBrandingProvisioningService,
        ITenantSettingsDocumentRepository tenantSettingsDocumentRepository,
        ITypedSettingsDocumentResolver typedSettingsDocumentResolver,
        IHumanSettingsMutationPreparation preparation,
        ITypedSettingsOriginalStore originalStore,
        IHierarchicalSettingsResolver hierarchicalSettingsResolver,
        IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> notificationHandlers)
    {
        _tenantContext = tenantContext;
        _tenantOnboardingStateRepository = tenantOnboardingStateRepository;
        _adminContext = adminContext;
        _policySettingService = policySettingService;
        _tenantBrandingProvisioningService = tenantBrandingProvisioningService;
        _tenantSettingsDocumentRepository = tenantSettingsDocumentRepository;
        _typedSettingsDocumentResolver = typedSettingsDocumentResolver;
        _preparation = preparation;
        _originalStore = originalStore;
        _hierarchicalSettingsResolver = hierarchicalSettingsResolver;
        _notificationHandlers = notificationHandlers;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CompleteTenantOnboardingCommand request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        if (!await IsUserAuthorizedAsync(tenantId, cancellationToken))
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only tenant administrators or instance administrators can complete tenant onboarding.");
        }

        if (request.DirectoryOperatorIdentity is null)
        {
            return BaseCommandResponse.Failure<Guid>(
                "tenant_directory_operator_identity_incomplete",
                "Tenant directory operator identity is not ready.");
        }

        TenantDirectoryOperatorIdentityReadiness identityReadiness =
            TenantDirectoryOperatorIdentity.Evaluate(
                request.DirectoryOperatorIdentity.ToPayload(),
                TenantDirectoryOperatorIdentityCapability.Activation);
        if (!identityReadiness.IsReady)
        {
            return BaseCommandResponse.Failure<Guid>(
                "tenant_directory_operator_identity_incomplete",
                "Tenant directory operator identity is not ready.",
                identityReadiness.ReasonCodes);
        }

        // Pre-read for create-or-update decision — BEFORE transaction (fast rejection, no write)
        var existingState = await _tenantOnboardingStateRepository.GetByTenantId(tenantId);

        // The identity revision is checked before any write; its mandatory save remains last so a
        // failure proves every preceding write participates in this transaction.
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        var outcome = await _preparation.ExecutePreparedAsync(prepared, async (_, ct) =>
        {
            var original = _preparation.CurrentTypedOriginal
                ?? throw new UnauthorizedAccessException("The original onboarding owner is required.");
            var command = original.Onboarding
                ?? throw new UnauthorizedAccessException("The original onboarding request is required.");
            (TenantSettingsDocument Document, bool IsNew) identityWrite =
                await PrepareDirectoryOperatorIdentityWriteAsync(
                    tenantId,
                    command.UserId,
                    command.ExpectedDirectoryOperatorIdentityConcurrencyStamp,
                    identityReadiness.Identity!,
                    ct);
            IReadOnlyList<SettingChangedNotification> notifications =
                await _policySettingService.ApplyTenantSettingsAsync(original, _originalStore, ct);
            await _tenantBrandingProvisioningService.EnsureTenantBrandingDocumentAsync(tenantId, cancellationToken: ct);

            if (existingState == null)
            {
                var created = await _tenantOnboardingStateRepository.Create(new TenantOnboardingState
                {
                    TenantId = tenantId,
                    Tenant = null!,
                    IsCompleted = true,
                    CurrentStep = 4,
                    TotalSteps = 4,
                    CompletedStepsJson = "[\"Identity\",\"Policies\",\"Branding\",\"Review\"]",
                    CreatedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    CompletedByUserId = command.UserId
                });
                await PersistIdentityWriteAsync(identityWrite);
                return (OnboardingStateId: created.Id, Notifications: notifications);
            }

            existingState.IsCompleted = true;
            existingState.CurrentStep = Math.Max(existingState.CurrentStep, 4);
            existingState.TotalSteps = Math.Max(existingState.TotalSteps, 4);
            if (string.IsNullOrWhiteSpace(existingState.CompletedStepsJson))
                existingState.CompletedStepsJson = "[\"Identity\",\"Policies\",\"Branding\",\"Review\"]";
            existingState.CompletedAt = DateTime.UtcNow;
            existingState.CompletedByUserId = command.UserId;
            await _tenantOnboardingStateRepository.Update(existingState);
            await PersistIdentityWriteAsync(identityWrite);
            return (OnboardingStateId: existingState.Id, Notifications: notifications);
        }, cancellationToken);

        _hierarchicalSettingsResolver.InvalidateCache(Explore.Domain.Settings.SettingScope.Tenant, tenantId);
        _typedSettingsDocumentResolver.InvalidateTenantDocumentCache(
            tenantId,
            SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity);
        foreach (SettingChangedNotification notification in outcome.Notifications)
        {
            await _notificationHandlers.HandleAsync(notification, cancellationToken);
        }

        return BaseCommandResponse.Success(
            outcome.OnboardingStateId,
            "Tenant onboarding completed successfully.");
    }

    private async Task<(TenantSettingsDocument Document, bool IsNew)> PrepareDirectoryOperatorIdentityWriteAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid? expectedConcurrencyStamp,
        TenantDirectoryOperatorIdentity identity,
        CancellationToken cancellationToken)
    {
        TenantSettingsDocument replacement =
            TenantDirectoryOperatorIdentityDocumentDefaults.Create(
                tenantId,
                identity.ToSettings());
        TenantSettingsDocument? existing =
            await _tenantSettingsDocumentRepository.GetTrackedByTenantAndDocumentKey(
                tenantId,
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                cancellationToken);
        DateTime changedAt = DateTime.UtcNow;
        if (existing is null)
        {
            replacement.Id = Guid.CreateVersion7();
            replacement.CreatedAt = changedAt;
            replacement.CreatedBy = actorUserId;
            replacement.ConcurrencyStamp = Guid.CreateVersion7();
            return (replacement, true);
        }

        if (!expectedConcurrencyStamp.HasValue
            || existing.ConcurrencyStamp != expectedConcurrencyStamp.Value)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "Tenant directory-operator identity changed since it was loaded.",
                "tenant_settings_document",
                existing.Id.ToString());
        }

        existing.UpdatePayload(
            replacement.SchemaVersion,
            replacement.DefaultsVersion,
            replacement.PayloadJson);
        existing.ConcurrencyStamp = Guid.CreateVersion7();
        existing.UpdatedAt = changedAt;
        existing.UpdatedBy = actorUserId;
        return (existing, false);
    }

    private Task PersistIdentityWriteAsync((TenantSettingsDocument Document, bool IsNew) identityWrite)
        => identityWrite.IsNew
            ? _tenantSettingsDocumentRepository.Create(identityWrite.Document)
            : _tenantSettingsDocumentRepository.Update(identityWrite.Document);

    private async Task<bool> IsUserAuthorizedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken))
        {
            return true;
        }

        return await _adminContext.IsInstanceAdminAsync(cancellationToken);
    }
}
