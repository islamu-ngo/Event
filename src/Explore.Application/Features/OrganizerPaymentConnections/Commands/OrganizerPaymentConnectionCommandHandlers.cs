using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.OrganizerPaymentConnections.Commands;

public sealed class RecordOrganizerPaymentConnectionCommandHandler(
    IOrganizerPaymentProviderConnectionRepository repository,
    IActorRepository actorRepository,
    ITenantUserRepository tenantUserRepository,
    IOrganizationTenantRepository organizationTenantRepository,
    IGroupTenantRepository groupTenantRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IGroupMemberRepository groupMemberRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider) : IRequestHandler<RecordOrganizerPaymentConnectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(RecordOrganizerPaymentConnectionCommand request, CancellationToken cancellationToken)
    {
        if (!await OrganizerPaymentActorAccess.AuthorizeAsync(request.TenantId, request.OrganizerActorId, tenantContext, currentUserService, actorRepository, tenantUserRepository, organizationTenantRepository, groupTenantRepository, organizationMemberRepository, groupMemberRepository, cancellationToken))
        {
            return Failure(Guid.Empty, "organizer_payment_actor_denied", "The organizer actor is not controlled by the current user in this tenant.");
        }

        string providerCode;
        string connectPlatformId;
        string externalAccountId;
        try
        {
            OrganizerPaymentProviderConnection candidate = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), request.TenantId, request.OrganizerActorId, request.ProviderCode, request.ConnectPlatformId, request.ExternalAccountId, timeProvider.GetUtcNow().UtcDateTime);
            providerCode = candidate.ProviderCode;
            connectPlatformId = candidate.ConnectPlatformId;
            externalAccountId = candidate.ExternalAccountId;
        }
        catch (ArgumentException exception)
        {
            return Failure(Guid.Empty, "organizer_payment_connection_validation_failed", exception.Message);
        }

        Guid newId = Guid.CreateVersion7();
        DateTime createdAt = timeProvider.GetUtcNow().UtcDateTime;
        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            OrganizerPaymentProviderConnection? externalOwner = await repository.GetHistoricalByExternalAccountAsync(providerCode, connectPlatformId, externalAccountId, token);
            if (externalOwner is not null
                && (externalOwner.TenantId != request.TenantId
                    || externalOwner.OrganizerActorId != request.OrganizerActorId
                    || externalOwner.StatusId is (int)OrganizerPaymentProviderConnectionStatusEnum.Disabled or (int)OrganizerPaymentProviderConnectionStatusEnum.Replaced))
            {
                return Failure(Guid.Empty, "organizer_payment_external_account_bound", "External account is already bound to another organizer scope.");
            }

            OrganizerPaymentProviderConnection? existing = await repository.GetActiveByScopeAsync(request.TenantId, request.OrganizerActorId, providerCode, connectPlatformId, token);
            if (existing is not null)
            {
                return existing.ExternalAccountId == externalAccountId
                    ? Success(existing.Id, "Organizer payment connection already exists.")
                    : Failure(existing.Id, "organizer_payment_connection_replace_required", "Active organizer payment connection must be replaced to change accounts.");
            }

            OrganizerPaymentProviderConnection connection = OrganizerPaymentProviderConnection.Create(newId, request.TenantId, request.OrganizerActorId, providerCode, connectPlatformId, externalAccountId, createdAt);
            await repository.CreateAsync(connection, token);
            await repository.SaveChangesAsync(token);
            return Success(connection.Id, "Organizer payment connection recorded.");
        }, cancellationToken);
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) => OrganizerPaymentConnectionResponses.Success(id, message);
    private static BaseCommandResponse<Guid> Failure(Guid id, string code, string message) => OrganizerPaymentConnectionResponses.Failure(id, code, message);
}

public sealed class ReplaceOrganizerPaymentConnectionCommandHandler(
    IOrganizerPaymentProviderConnectionRepository repository,
    IActorRepository actorRepository,
    ITenantUserRepository tenantUserRepository,
    IOrganizationTenantRepository organizationTenantRepository,
    IGroupTenantRepository groupTenantRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IGroupMemberRepository groupMemberRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider) : IRequestHandler<ReplaceOrganizerPaymentConnectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ReplaceOrganizerPaymentConnectionCommand request, CancellationToken cancellationToken)
    {
        if (!await OrganizerPaymentActorAccess.AuthorizeAsync(request.TenantId, request.OrganizerActorId, tenantContext, currentUserService, actorRepository, tenantUserRepository, organizationTenantRepository, groupTenantRepository, organizationMemberRepository, groupMemberRepository, cancellationToken))
        {
            return OrganizerPaymentConnectionResponses.Failure(request.CurrentConnectionId, "organizer_payment_actor_denied", "The organizer actor is not controlled by the current user in this tenant.");
        }

        Guid replacementId = Guid.CreateVersion7();
        DateTime replacedAt = timeProvider.GetUtcNow().UtcDateTime;
        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            OrganizerPaymentProviderConnection? current = await repository.GetByTenantAndIdForUpdateAsync(request.TenantId, request.CurrentConnectionId, token);
            if (current is null || current.OrganizerActorId != request.OrganizerActorId)
            {
                return OrganizerPaymentConnectionResponses.Failure(request.CurrentConnectionId, "organizer_payment_connection_not_found", "Organizer payment connection was not found for this actor.");
            }

            string normalizedAccount;
            try
            {
                OrganizerPaymentProviderConnection probe = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), current.TenantId, current.OrganizerActorId, current.ProviderCode, current.ConnectPlatformId, request.NewExternalAccountId, replacedAt);
                normalizedAccount = probe.ExternalAccountId;
            }
            catch (ArgumentException exception)
            {
                return OrganizerPaymentConnectionResponses.Failure(current.Id, "organizer_payment_connection_validation_failed", exception.Message);
            }

            OrganizerPaymentProviderConnection? externalOwner = await repository.GetHistoricalByExternalAccountAsync(current.ProviderCode, current.ConnectPlatformId, normalizedAccount, token);
            if (externalOwner is not null)
            {
                return OrganizerPaymentConnectionResponses.Failure(current.Id, "organizer_payment_external_account_bound", "External account is already bound.");
            }

            try
            {
                OrganizerPaymentProviderConnection replacement = current.ReplaceWith(replacementId, normalizedAccount, replacedAt);
                await repository.SaveChangesAsync(token);
                await repository.CreateAsync(replacement, token);
                await repository.SaveChangesAsync(token);
                current.MarkReplacedBy(replacement.Id);
                await repository.SaveChangesAsync(token);
                return OrganizerPaymentConnectionResponses.Success(replacement.Id, "Organizer payment connection replaced.");
            }
            catch (ArgumentException exception)
            {
                return OrganizerPaymentConnectionResponses.Failure(current.Id, "organizer_payment_connection_validation_failed", exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return OrganizerPaymentConnectionResponses.Failure(current.Id, "organizer_payment_connection_terminal", exception.Message);
            }
        }, cancellationToken);
    }
}

public sealed class DisableOrganizerPaymentConnectionCommandHandler(
    IOrganizerPaymentProviderConnectionRepository repository,
    IActorRepository actorRepository,
    ITenantUserRepository tenantUserRepository,
    IOrganizationTenantRepository organizationTenantRepository,
    IGroupTenantRepository groupTenantRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IGroupMemberRepository groupMemberRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider) : IRequestHandler<DisableOrganizerPaymentConnectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DisableOrganizerPaymentConnectionCommand request, CancellationToken cancellationToken)
    {
        if (!await OrganizerPaymentActorAccess.AuthorizeAsync(request.TenantId, request.OrganizerActorId, tenantContext, currentUserService, actorRepository, tenantUserRepository, organizationTenantRepository, groupTenantRepository, organizationMemberRepository, groupMemberRepository, cancellationToken))
        {
            return OrganizerPaymentConnectionResponses.Failure(request.ConnectionId, "organizer_payment_actor_denied", "The organizer actor is not controlled by the current user in this tenant.");
        }

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            OrganizerPaymentProviderConnection? connection = await repository.GetByTenantAndIdForUpdateAsync(request.TenantId, request.ConnectionId, token);
            if (connection is null || connection.OrganizerActorId != request.OrganizerActorId)
            {
                return OrganizerPaymentConnectionResponses.Failure(request.ConnectionId, "organizer_payment_connection_not_found", "Organizer payment connection was not found for this actor.");
            }

            try
            {
                connection.Disable(request.ReasonCode, timeProvider.GetUtcNow().UtcDateTime);
                await repository.SaveChangesAsync(token);
                return OrganizerPaymentConnectionResponses.Success(connection.Id, "Organizer payment connection disabled.");
            }
            catch (ArgumentException exception)
            {
                return OrganizerPaymentConnectionResponses.Failure(connection.Id, "organizer_payment_connection_validation_failed", exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return OrganizerPaymentConnectionResponses.Failure(connection.Id, "organizer_payment_connection_terminal", exception.Message);
            }
        }, cancellationToken);
    }
}

public sealed class CreateOrganizerPaymentOnboardingLinkCommandHandler(
    IOrganizerPaymentProviderConnectionRepository repository,
    IOrganizerPaymentProviderAccountOperationRepository operationRepository,
    IEventRepository eventRepository,
    IOrganizerPaymentCommerceConfiguration commerceConfiguration,
    IOrganizerPaymentOnboardingProvider onboardingProvider,
    IActorRepository actorRepository,
    ITenantUserRepository tenantUserRepository,
    IOrganizationTenantRepository organizationTenantRepository,
    IGroupTenantRepository groupTenantRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IGroupMemberRepository groupMemberRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider,
    ILogger<CreateOrganizerPaymentOnboardingLinkCommandHandler> logger) : IRequestHandler<CreateOrganizerPaymentOnboardingLinkCommand, BaseCommandResponse<OrganizerPaymentOnboardingLinkResult>>
{
    private static readonly TimeSpan ProviderHandoffRecoveryTimeout = TimeSpan.FromSeconds(5);
    private const string ProviderHandoffRecoveryFailedLogMessage = "Organizer payment provider account cancellation recovery manual settlement failed.";
    private const string ManualReconciliationFailureCode = "organizer_payment_provider_manual_reconciliation_required";
    private const string ProviderCreateRequestedRecoveredFailureCode = "organizer_payment_provider_create_requested_recovered";
    private const string ProviderAccountCreationCanceledFailureCode = "organizer_payment_provider_account_creation_canceled";
    private const string ProviderAccountCreationExceptionFailureCode = "organizer_payment_provider_account_creation_exception";

    public async Task<BaseCommandResponse<OrganizerPaymentOnboardingLinkResult>> Handle(CreateOrganizerPaymentOnboardingLinkCommand request, CancellationToken cancellationToken)
    {
        Event? eventTarget = await eventRepository.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (eventTarget?.TenantId != tenantContext.TenantId || eventTarget.OrganizerActorId is null)
        {
            return Failure("organizer_payment_event_not_found", "The event organizer payment scope was not found.");
        }

        Guid tenantId = eventTarget.TenantId;
        Guid organizerActorId = eventTarget.OrganizerActorId.Value;
        if (!await OrganizerPaymentActorAccess.AuthorizeAsync(tenantId, organizerActorId, tenantContext, currentUserService, actorRepository, tenantUserRepository, organizationTenantRepository, groupTenantRepository, organizationMemberRepository, groupMemberRepository, cancellationToken))
        {
            return Failure("organizer_payment_actor_denied", "The organizer actor is not controlled by the current user in this tenant.");
        }

        if (!IsNavigationUrl(request.ReturnUrl) || !IsNavigationUrl(request.RefreshUrl))
        {
            return Failure("organizer_payment_onboarding_navigation_invalid", "Return and refresh URLs must be absolute HTTP navigation URLs.");
        }

        string providerCode;
        string connectPlatformId;
        try
        {
            OrganizerPaymentProviderConnection probe = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), tenantId, organizerActorId, commerceConfiguration.ProviderCode, commerceConfiguration.ConnectPlatformId, "validation-probe", timeProvider.GetUtcNow().UtcDateTime);
            providerCode = probe.ProviderCode;
            connectPlatformId = probe.ConnectPlatformId;
        }
        catch (ArgumentException exception)
        {
            return Failure("organizer_payment_connection_validation_failed", exception.Message);
        }

        OrganizerPaymentProviderConnection? existing = await repository.GetActiveByScopeAsync(tenantId, organizerActorId, providerCode, connectPlatformId, cancellationToken);
        if (existing is not null)
        {
            OrganizerPaymentOnboardingLinkCreationResult existingLink = await CreateLinkAsync(providerCode, connectPlatformId, existing.ExternalAccountId, request, cancellationToken);
            return existingLink.Success && existingLink.OnboardingUrl is not null && IsNavigationUrl(existingLink.OnboardingUrl)
                ? Success(existingLink.OnboardingUrl, reusedExistingConnection: true)
                : Failure(existingLink.FailureCode ?? "organizer_payment_onboarding_link_failed", "Provider onboarding link creation failed.");
        }

        Guid operationId = Guid.CreateVersion7();
        DateTime requestedAt = timeProvider.GetUtcNow().UtcDateTime;
        OrganizerPaymentAccountOperationAdmission admission = await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            OrganizerPaymentProviderConnection? racedActive = await repository.GetActiveByScopeAsync(tenantId, organizerActorId, providerCode, connectPlatformId, token);
            if (racedActive is not null)
            {
                return OrganizerPaymentAccountOperationAdmission.ExistingConnection(racedActive.Id, racedActive.ExternalAccountId);
            }

            OrganizerPaymentProviderAccountOperation? unresolved = await operationRepository.GetActiveByScopeAsync(tenantId, organizerActorId, providerCode, connectPlatformId, token);
            if (unresolved is not null)
            {
                if (unresolved.StatusId == (int)OrganizerPaymentProviderAccountOperationStatus.ProviderCreateRequested)
                {
                    unresolved.MarkManualReconciliationRequired(ProviderCreateRequestedRecoveredFailureCode, null, requestedAt);
                    await operationRepository.SaveChangesAsync(token);
                }

                return OrganizerPaymentAccountOperationAdmission.Unresolved();
            }

            OrganizerPaymentProviderAccountOperation operation = OrganizerPaymentProviderAccountOperation.CreateRequested(operationId, tenantId, organizerActorId, providerCode, connectPlatformId, requestedAt);
            await operationRepository.CreateAsync(operation, token);
            await operationRepository.SaveChangesAsync(token);
            return OrganizerPaymentAccountOperationAdmission.Created(operation.Id, operation.ProviderIdempotencyKey);
        }, cancellationToken);

        if (admission.ReusedExistingConnection)
        {
            OrganizerPaymentOnboardingLinkCreationResult existingLink = await CreateLinkAsync(providerCode, connectPlatformId, admission.ExternalAccountId!, request, cancellationToken);
            return existingLink.Success && existingLink.OnboardingUrl is not null && IsNavigationUrl(existingLink.OnboardingUrl)
                ? Success(existingLink.OnboardingUrl, reusedExistingConnection: true)
                : Failure(existingLink.FailureCode ?? "organizer_payment_onboarding_link_failed", "Provider onboarding link creation failed.");
        }

        if (!admission.ShouldCallProvider)
        {
            return Failure(ManualReconciliationFailureCode, "Provider account creation requires manual reconciliation before retry.");
        }

        OrganizerPaymentProviderAccountCreationResult account;
        try
        {
            account = await onboardingProvider.CreateAccountAsync(
                new OrganizerPaymentProviderAccountCreationRequest(tenantId, organizerActorId, providerCode, connectPlatformId, admission.ProviderIdempotencyKey!),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await TryMarkOperationManualAfterCanceledProviderHandoffAsync(tenantId, admission.OperationId, ProviderAccountCreationCanceledFailureCode, null, timeProvider.GetUtcNow().UtcDateTime);
            throw;
        }
        catch (Exception)
        {
            await MarkOperationManualWithRecoveryTokenAsync(tenantId, admission.OperationId, ProviderAccountCreationExceptionFailureCode, null, timeProvider.GetUtcNow().UtcDateTime);
            return Failure(ManualReconciliationFailureCode, "Provider account creation requires manual reconciliation before retry.");
        }

        DateTime providerSettledAt = timeProvider.GetUtcNow().UtcDateTime;
        if (account.Status == OrganizerPaymentProviderAccountCreationStatus.Failed)
        {
            await MarkOperationRejectedAsync(tenantId, admission.OperationId, account.FailureCode, account.ProviderRequestId, providerSettledAt, cancellationToken);
            return Failure(account.FailureCode ?? "organizer_payment_provider_account_creation_failed", "Provider account creation was rejected.");
        }

        if (account.Status == OrganizerPaymentProviderAccountCreationStatus.ManualReconciliationRequired || string.IsNullOrWhiteSpace(account.ExternalAccountId))
        {
            await MarkOperationManualAsync(tenantId, admission.OperationId, account.FailureCode, account.ProviderRequestId, providerSettledAt, cancellationToken);
            return Failure(ManualReconciliationFailureCode, "Provider account creation requires manual reconciliation before retry.");
        }

        Guid connectionId = Guid.CreateVersion7();
        DateTime createdAt = timeProvider.GetUtcNow().UtcDateTime;
        OrganizerPaymentConnectionPersistenceResult persistence = await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            OrganizerPaymentProviderAccountOperation? operation = await operationRepository.GetByTenantAndIdForUpdateAsync(tenantId, admission.OperationId, token);
            if (operation is null || !operation.IsUnresolved)
            {
                return OrganizerPaymentConnectionPersistenceResult.Failure("organizer_payment_provider_manual_reconciliation_required", "Provider account operation is no longer safely bindable.");
            }

            OrganizerPaymentProviderConnection? racedActive = await repository.GetActiveByScopeAsync(tenantId, organizerActorId, providerCode, connectPlatformId, token);
            OrganizerPaymentProviderConnection? externalOwner = await repository.GetHistoricalByExternalAccountAsync(providerCode, connectPlatformId, account.ExternalAccountId, token);
            if (racedActive is not null)
            {
                if (racedActive.ExternalAccountId == account.ExternalAccountId && (externalOwner is null || externalOwner.Id == racedActive.Id))
                {
                    operation.BindToConnection(racedActive.Id, racedActive.ExternalAccountId, createdAt);
                    await operationRepository.SaveChangesAsync(token);
                    return OrganizerPaymentConnectionPersistenceResult.Success(racedActive.Id, racedActive.ExternalAccountId, reusedExistingConnection: true);
                }

                operation.MarkManualReconciliationRequired("organizer_payment_provider_bind_race", account.ProviderRequestId, createdAt);
                await operationRepository.SaveChangesAsync(token);
                return OrganizerPaymentConnectionPersistenceResult.Failure("organizer_payment_provider_manual_reconciliation_required", "A concurrent organizer payment connection prevented a safe provider bind.");
            }

            if (externalOwner is not null)
            {
                operation.MarkManualReconciliationRequired("organizer_payment_external_account_bound", account.ProviderRequestId, createdAt);
                await operationRepository.SaveChangesAsync(token);
                return OrganizerPaymentConnectionPersistenceResult.Failure("organizer_payment_provider_manual_reconciliation_required", "External account is already bound to another organizer scope.");
            }

            try
            {
                OrganizerPaymentProviderConnection connection = OrganizerPaymentProviderConnection.Create(connectionId, tenantId, organizerActorId, providerCode, connectPlatformId, account.ExternalAccountId, createdAt);
                await repository.CreateAsync(connection, token);
                operation.BindToConnection(connection.Id, connection.ExternalAccountId, createdAt);
                await repository.SaveChangesAsync(token);
                return OrganizerPaymentConnectionPersistenceResult.Success(connection.Id, connection.ExternalAccountId, reusedExistingConnection: false);
            }
            catch (ArgumentException exception)
            {
                return OrganizerPaymentConnectionPersistenceResult.Failure("organizer_payment_connection_validation_failed", exception.Message);
            }
        }, cancellationToken);

        if (!persistence.Succeeded)
        {
            return Failure(persistence.FailureCode!, persistence.Message!);
        }

        OrganizerPaymentOnboardingLinkCreationResult link = await CreateLinkAsync(providerCode, connectPlatformId, persistence.ExternalAccountId!, request, cancellationToken);
        return link.Success && link.OnboardingUrl is not null && IsNavigationUrl(link.OnboardingUrl)
            ? Success(link.OnboardingUrl, persistence.ReusedExistingConnection)
            : Failure(link.FailureCode ?? "organizer_payment_onboarding_link_failed", "Provider onboarding link creation failed.");
    }

    private async Task MarkOperationManualAsync(Guid tenantId, Guid operationId, string? failureCode, string? providerRequestId, DateTime occurredAt, CancellationToken cancellationToken) =>
        await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            OrganizerPaymentProviderAccountOperation? operation = await operationRepository.GetByTenantAndIdForUpdateAsync(tenantId, operationId, token);
            if (operation?.IsUnresolved == true)
            {
                operation.MarkManualReconciliationRequired(failureCode ?? ManualReconciliationFailureCode, providerRequestId, occurredAt);
                await operationRepository.SaveChangesAsync(token);
            }

            return true;
        }, cancellationToken);

    private async Task MarkOperationManualWithRecoveryTokenAsync(Guid tenantId, Guid operationId, string failureCode, string? providerRequestId, DateTime occurredAt)
    {
        using var recovery = new CancellationTokenSource(ProviderHandoffRecoveryTimeout);
        await MarkOperationManualAsync(tenantId, operationId, failureCode, providerRequestId, occurredAt, recovery.Token);
    }

    private async Task TryMarkOperationManualAfterCanceledProviderHandoffAsync(Guid tenantId, Guid operationId, string failureCode, string? providerRequestId, DateTime occurredAt)
    {
        try
        {
            await MarkOperationManualWithRecoveryTokenAsync(tenantId, operationId, failureCode, providerRequestId, occurredAt);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, ProviderHandoffRecoveryFailedLogMessage);
        }
    }

    private async Task MarkOperationRejectedAsync(Guid tenantId, Guid operationId, string? failureCode, string? providerRequestId, DateTime occurredAt, CancellationToken cancellationToken) =>
        await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            OrganizerPaymentProviderAccountOperation? operation = await operationRepository.GetByTenantAndIdForUpdateAsync(tenantId, operationId, token);
            if (operation?.IsUnresolved == true)
            {
                operation.RejectByProvider(failureCode ?? "organizer_payment_provider_rejected", providerRequestId, occurredAt);
                await operationRepository.SaveChangesAsync(token);
            }

            return true;
        }, cancellationToken);

    private async Task<OrganizerPaymentOnboardingLinkCreationResult> CreateLinkAsync(
        string providerCode,
        string connectPlatformId,
        string externalAccountId,
        CreateOrganizerPaymentOnboardingLinkCommand request,
        CancellationToken cancellationToken) =>
        await onboardingProvider.CreateOnboardingLinkAsync(
            new OrganizerPaymentOnboardingLinkRequest(
                providerCode,
                connectPlatformId,
                externalAccountId,
                request.ReturnUrl,
                request.RefreshUrl,
                OrganizerPaymentOnboardingType.AccountOnboarding),
            cancellationToken);

    private static bool IsNavigationUrl(Uri url) =>
        url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp);

    private static BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> Success(Uri onboardingUrl, bool reusedExistingConnection) =>
        BaseCommandResponse.Success(
            new OrganizerPaymentOnboardingLinkResult(onboardingUrl, reusedExistingConnection),
            reusedExistingConnection
                ? "Organizer payment onboarding link created for existing connection."
                : "Organizer payment onboarding link created.");

    private static BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> Failure(string code, string message) =>
        BaseCommandResponse.Failure<OrganizerPaymentOnboardingLinkResult>(code, message, [message]);

    private sealed record OrganizerPaymentConnectionPersistenceResult(
        bool Succeeded,
        Guid ConnectionId,
        string? ExternalAccountId,
        bool ReusedExistingConnection,
        string? FailureCode,
        string? Message)
    {
        public static OrganizerPaymentConnectionPersistenceResult Success(Guid connectionId, string externalAccountId, bool reusedExistingConnection) =>
            new(true, connectionId, externalAccountId, reusedExistingConnection, null, null);

        public static OrganizerPaymentConnectionPersistenceResult Failure(string failureCode, string message) =>
            new(false, Guid.Empty, null, false, failureCode, message);
    }

    private sealed record OrganizerPaymentAccountOperationAdmission(
        bool ShouldCallProvider,
        bool ReusedExistingConnection,
        Guid OperationId,
        Guid ConnectionId,
        string? ExternalAccountId,
        string? ProviderIdempotencyKey)
    {
        public static OrganizerPaymentAccountOperationAdmission Created(Guid operationId, string providerIdempotencyKey) =>
            new(true, false, operationId, Guid.Empty, null, providerIdempotencyKey);

        public static OrganizerPaymentAccountOperationAdmission ExistingConnection(Guid connectionId, string externalAccountId) =>
            new(false, true, Guid.Empty, connectionId, externalAccountId, null);

        public static OrganizerPaymentAccountOperationAdmission Unresolved() =>
            new(false, false, Guid.Empty, Guid.Empty, null, null);
    }
}
