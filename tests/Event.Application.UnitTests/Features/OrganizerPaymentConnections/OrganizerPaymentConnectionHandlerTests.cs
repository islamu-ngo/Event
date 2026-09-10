using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Authorization;
using Explore.Application.DTOs.OrganizerPaymentConnections;
using Explore.Application.Features.OrganizerPaymentConnections;
using Explore.Application.Features.OrganizerPaymentConnections.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Application.UnitTests.Features.OrganizerPaymentConnections;

public sealed class OrganizerPaymentConnectionHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid ForeignTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");
    private static readonly Guid EventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000003");
    private static readonly Guid UserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000010");
    private static readonly Guid ActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000020");
    private static readonly DateTime Now = new(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task RecordConnection_UnauthenticatedCallerIsDenied()
    {
        Harness harness = new(authenticated: false);

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_1"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(harness.Repository.Connections).IsEmpty();
    }

    [Test]
    public async Task RecordConnection_ExplicitActorNotControlledByCurrentUserIsDenied()
    {
        Harness harness = new(controlled: false);

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_1"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(harness.Repository.Connections).IsEmpty();
    }

    [Test]
    public async Task RecordConnection_TenantIneligibleActorIsDenied()
    {
        Harness harness = new(activeTenantUser: false);

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_1"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(harness.Repository.Connections).IsEmpty();
    }

    [Test]
    public async Task RecordConnection_AdminOrSessionUserNeverBecomesRecipient()
    {
        Harness harness = new();

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_1"), CancellationToken.None);

        OrganizerPaymentProviderConnection created = harness.Repository.Connections.Single();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(created.OrganizerActorId).IsEqualTo(ActorId);
        await Assert.That(created.OrganizerActorId).IsNotEqualTo(UserId);
    }

    [Test]
    public async Task RecordConnection_SameActiveScopeAndExternalAccountReturnsExistingId()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection existing = harness.Repository.AddExisting("acct_1");

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_1"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(existing.Id);
        await Assert.That(harness.Repository.Connections.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RecordConnection_SameActiveScopeDifferentAccountRequiresReplace()
    {
        Harness harness = new();
        harness.Repository.AddExisting("acct_1");

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_2"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_connection_replace_required");
        await Assert.That(harness.Repository.Connections.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RecordConnection_ExternalAccountBoundToAnotherActorIsRejected()
    {
        Harness harness = new();
        harness.Repository.AddExisting("acct_1", organizerActorId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000099"));

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_1"), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_external_account_bound");
    }

    [Test]
    public async Task ReplaceConnection_IsFutureOnlyAndKeepsOldExternalAccountImmutable()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection current = harness.Repository.AddExisting("acct_old");

        BaseCommandResponse<Guid> result = await harness.ReplaceHandler.Handle(new ReplaceOrganizerPaymentConnectionCommand(TenantId, ActorId, current.Id, "acct_new"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(current.ExternalAccountId).IsEqualTo("acct_old");
        await Assert.That(current.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Replaced);
        await Assert.That(current.ReplacedByConnectionId).IsEqualTo(result.Id);
        await Assert.That(harness.Repository.Connections.Single(connection => connection.Id == result.Id).ReplacesConnectionId).IsEqualTo(current.Id);
        await Assert.That(harness.Repository.SaveChangesCount).IsEqualTo(3);
    }

    [Test]
    public async Task ReplaceConnection_NewExternalAccountAlreadyBoundIsRejectedWithoutMutatingCurrent()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection current = harness.Repository.AddExisting("acct_old");
        harness.Repository.AddExisting("acct_new", organizerActorId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000099"));

        BaseCommandResponse<Guid> result = await harness.ReplaceHandler.Handle(new ReplaceOrganizerPaymentConnectionCommand(TenantId, ActorId, current.Id, "acct_new"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(current.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
        await Assert.That(current.ExternalAccountId).IsEqualTo("acct_old");
    }

    [Test]
    public async Task RecordConnection_CrossTenantHistoricalDisabledExternalAccountBoundIsRejectedWithoutReturningForeignId()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection historical = harness.Repository.AddExisting(
            "acct_1",
            organizerActorId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000099"),
            tenantId: ForeignTenantId);
        historical.Disable("operator_disabled", Now.AddMinutes(1));

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_1"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_external_account_bound");
        await Assert.That(result.Id).IsNotEqualTo(historical.Id);
    }

    [Test]
    public async Task RecordConnection_HistoricalReplacedExternalAccountBoundToAnotherActorIsRejected()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection historical = harness.Repository.AddExisting("acct_1", organizerActorId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000099"));
        _ = historical.ReplaceWith(Guid.CreateVersion7(), "acct_other", Now.AddMinutes(1));

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_1"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_external_account_bound");
    }

    [Test]
    public async Task RecordConnection_HistoricalDisabledExternalAccountInSameScopeIsRejected()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection historical = harness.Repository.AddExisting("acct_1");
        historical.Disable("operator_disabled", Now.AddMinutes(1));

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_1"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_external_account_bound");
    }

    [Test]
    public async Task RecordConnection_HistoricalReplacedExternalAccountInSameScopeIsRejected()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection historical = harness.Repository.AddExisting("acct_1");
        _ = historical.ReplaceWith(Guid.CreateVersion7(), "acct_other", Now.AddMinutes(1));

        BaseCommandResponse<Guid> result = await harness.RecordHandler.Handle(harness.RecordCommand("acct_1"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_external_account_bound");
    }

    [Test]
    public async Task ReplaceConnection_CrossTenantHistoricalReplacedExternalAccountBoundIsRejectedWithoutReturningForeignId()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection current = harness.Repository.AddExisting("acct_old");
        OrganizerPaymentProviderConnection historical = harness.Repository.AddExisting(
            "acct_new",
            organizerActorId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000099"),
            tenantId: ForeignTenantId);
        historical.Disable("operator_disabled", Now.AddMinutes(1));

        BaseCommandResponse<Guid> result = await harness.ReplaceHandler.Handle(new ReplaceOrganizerPaymentConnectionCommand(TenantId, ActorId, current.Id, "acct_new"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_external_account_bound");
        await Assert.That(result.Id).IsNotEqualTo(historical.Id);
        await Assert.That(current.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
    }

    [Test]
    public async Task ReplaceConnection_CrossTenantHistoricalDisabledExternalAccountBoundIsRejectedWithoutReturningForeignId()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection current = harness.Repository.AddExisting("acct_old");
        OrganizerPaymentProviderConnection historical = harness.Repository.AddExisting(
            "acct_new",
            organizerActorId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000099"),
            tenantId: ForeignTenantId);
        _ = historical.ReplaceWith(Guid.CreateVersion7(), "acct_other", Now.AddMinutes(1));

        BaseCommandResponse<Guid> result = await harness.ReplaceHandler.Handle(new ReplaceOrganizerPaymentConnectionCommand(TenantId, ActorId, current.Id, "acct_new"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_external_account_bound");
        await Assert.That(result.Id).IsNotEqualTo(historical.Id);
        await Assert.That(current.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
    }

    [Test]
    public async Task DisableConnection_RequiresOwnershipAndUsesBoundedReasonCode()
    {
        Harness denied = new(controlled: false);
        OrganizerPaymentProviderConnection deniedConnection = denied.Repository.AddExisting("acct_1");
        Harness allowed = new();
        OrganizerPaymentProviderConnection allowedConnection = allowed.Repository.AddExisting("acct_1");

        BaseCommandResponse<Guid> deniedResult = await denied.DisableHandler.Handle(new DisableOrganizerPaymentConnectionCommand(TenantId, ActorId, deniedConnection.Id, "operator_disabled"), CancellationToken.None);
        BaseCommandResponse<Guid> allowedResult = await allowed.DisableHandler.Handle(new DisableOrganizerPaymentConnectionCommand(TenantId, ActorId, allowedConnection.Id, "operator_disabled"), CancellationToken.None);

        await Assert.That(deniedResult.IsSuccess).IsFalse();
        await Assert.That(allowedResult.IsSuccess).IsTrue();
        await Assert.That(allowedConnection.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Disabled);
        await Assert.That(allowedConnection.DisabledReasonCode).IsEqualTo("operator_disabled");
    }

    [Test]
    public async Task QueriesAreScopedToExplicitTenantAndActorAndExposeBoundedDtoOnly()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection owned = harness.Repository.AddExisting("acct_owned");
        harness.Repository.AddExisting("acct_other", organizerActorId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000099"));

        IReadOnlyList<OrganizerPaymentConnectionDto> rows = await harness.ListHandler.Handle(new ListOrganizerPaymentConnectionsQuery(TenantId, ActorId), CancellationToken.None);
        OrganizerPaymentConnectionDto? detail = await harness.GetHandler.Handle(new GetOrganizerPaymentConnectionQuery(TenantId, ActorId, owned.Id), CancellationToken.None);

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows.Single().StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
        await Assert.That(detail).IsNotNull();
        string[] exposedProperties = typeof(OrganizerPaymentConnectionDto).GetProperties().Select(property => property.Name).ToArray();
        await Assert.That(exposedProperties).DoesNotContain("Id");
        await Assert.That(exposedProperties).DoesNotContain("TenantId");
        await Assert.That(exposedProperties).DoesNotContain("OrganizerActorId");
        await Assert.That(exposedProperties).DoesNotContain("ProviderCode");
        await Assert.That(exposedProperties).DoesNotContain("ConnectPlatformId");
        await Assert.That(exposedProperties).DoesNotContain("ExternalAccountId");
        await Assert.That(exposedProperties).DoesNotContain("LastReadinessEvidenceRevision");
    }

    [Test]
    public async Task Queries_UnauthenticatedCallerReturnsEmptyAndDoesNotHitRepository()
    {
        Harness harness = new(authenticated: false);
        harness.Repository.AddExisting("acct_owned");

        IReadOnlyList<OrganizerPaymentConnectionDto> rows = await harness.ListHandler.Handle(new ListOrganizerPaymentConnectionsQuery(TenantId, ActorId), CancellationToken.None);
        OrganizerPaymentConnectionDto? detail = await harness.GetHandler.Handle(new GetOrganizerPaymentConnectionQuery(TenantId, ActorId, Guid.CreateVersion7()), CancellationToken.None);

        await Assert.That(rows).IsEmpty();
        await Assert.That(detail).IsNull();
        await Assert.That(harness.Repository.ReadCount).IsEqualTo(0);
    }

    [Test]
    public async Task Queries_UncontrolledActorReturnsEmptyAndDoesNotHitRepository()
    {
        Harness harness = new(controlled: false);
        harness.Repository.AddExisting("acct_owned");

        IReadOnlyList<OrganizerPaymentConnectionDto> rows = await harness.ListHandler.Handle(new ListOrganizerPaymentConnectionsQuery(TenantId, ActorId), CancellationToken.None);
        OrganizerPaymentConnectionDto? detail = await harness.GetHandler.Handle(new GetOrganizerPaymentConnectionQuery(TenantId, ActorId, Guid.CreateVersion7()), CancellationToken.None);

        await Assert.That(rows).IsEmpty();
        await Assert.That(detail).IsNull();
        await Assert.That(harness.Repository.ReadCount).IsEqualTo(0);
    }

    [Test]
    public async Task CreateOnboardingLink_ReusesActiveConnectionAndOnlyCreatesHostedLinkOutsideTransaction()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection existing = harness.Repository.AddExisting("acct_existing");
        harness.Provider.NextLinkUrl = new Uri("https://payments.example/onboard/existing");

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.OnboardingUrl).IsEqualTo(new Uri("https://payments.example/onboard/existing"));
        await Assert.That(result.Id.ReusedExistingConnection).IsTrue();
        await Assert.That(harness.Provider.AccountCreateCalls).IsEqualTo(0);
        await Assert.That(harness.Provider.LinkCreateCalls).IsEqualTo(1);
        await Assert.That(harness.UnitOfWork.SerializableCalls).IsEqualTo(0);
        await Assert.That(harness.Provider.CalledInsideTransaction).IsFalse();
        await Assert.That(harness.Repository.Connections.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CreateOnboardingLink_RejectsProviderNavigationOutsideHttpSchemes()
    {
        Harness harness = new();
        harness.Repository.AddExisting("acct_existing");
        harness.Provider.NextLinkUrl = new Uri("javascript:alert('unsafe')");

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Id).IsNull();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_onboarding_link_failed");
        await Assert.That(harness.Provider.LinkCreateCalls).IsEqualTo(1);
    }

    [Test]
    public async Task CreateOnboardingLink_PersistsConnectionBeforeCreatingHostedLinkOutsideTransaction()
    {
        Harness harness = new();
        harness.Provider.NextAccountResult = OrganizerPaymentProviderAccountCreationResult.Created("acct_new");
        harness.Provider.NextLinkUrl = new Uri("https://payments.example/onboard/new");

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        OrganizerPaymentProviderConnection created = harness.Repository.Connections.Single();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.OnboardingUrl).IsEqualTo(new Uri("https://payments.example/onboard/new"));
        await Assert.That(result.Id.ReusedExistingConnection).IsFalse();
        await Assert.That(created.OrganizerActorId).IsEqualTo(ActorId);
        await Assert.That(created.ExternalAccountId).IsEqualTo("acct_new");
        await Assert.That(harness.UnitOfWork.SerializableCalls).IsEqualTo(2);
        await Assert.That(harness.Provider.CalledInsideTransaction).IsFalse();
        OrganizerPaymentProviderAccountOperation operation = harness.OperationRepository.Operations.Single();
        await Assert.That(operation.StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.BoundToConnection);
        await Assert.That(operation.ConnectionId).IsEqualTo(created.Id);
        await Assert.That(harness.Provider.LastAccountRequest!.ProviderIdempotencyKey).IsEqualTo(operation.ProviderIdempotencyKey);
        await Assert.That(harness.Events).IsEquivalentTo(["persist-operation", "save-operation", "create-account", "persist", "save", "create-link"]);
        await Assert.That(harness.Repository.SaveChangesCount).IsEqualTo(1);
    }

    [Test]
    public async Task CreateOnboardingLink_AmbiguousAccountCreationPersistsManualFenceAndBlocksRetry()
    {
        Harness harness = new();
        harness.Provider.NextAccountResult = OrganizerPaymentProviderAccountCreationResult.ManualReconciliationRequired();

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);
        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> retry = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(retry.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_provider_manual_reconciliation_required");
        await Assert.That(harness.Provider.AccountCreateCalls).IsEqualTo(1);
        await Assert.That(harness.Provider.LinkCreateCalls).IsEqualTo(0);
        await Assert.That(harness.OperationRepository.Operations.Single().StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.ManualReconciliationRequired);
        await Assert.That(harness.Repository.Connections).IsEmpty();
    }

    [Test]
    public async Task CreateOnboardingLink_ProviderThrowAfterHandoffSettlesManualAndReturnsControlledFailure()
    {
        Harness harness = new();
        harness.Provider.AccountCreateExceptionFactory = _ => new InvalidOperationException("provider transport failed");

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_provider_manual_reconciliation_required");
        await Assert.That(harness.Provider.AccountCreateCalls).IsEqualTo(1);
        await Assert.That(harness.Provider.LinkCreateCalls).IsEqualTo(0);
        await Assert.That(harness.Repository.Connections).IsEmpty();
        OrganizerPaymentProviderAccountOperation operation = harness.OperationRepository.Operations.Single();
        await Assert.That(operation.StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.ManualReconciliationRequired);
        await Assert.That(operation.FailureCode).IsEqualTo("organizer_payment_provider_account_creation_exception");
        await Assert.That(harness.Events).IsEquivalentTo(["persist-operation", "save-operation", "create-account", "save-operation"]);
    }

    [Test]
    public async Task CreateOnboardingLink_CallerCancellationAfterHandoffSettlesManualAndPropagatesCancellation()
    {
        Harness harness = new();
        using var cts = new CancellationTokenSource();
        harness.Provider.AccountCreateExceptionFactory = token =>
        {
            cts.Cancel();
            return new OperationCanceledException(token);
        };

        await Assert.That(async () => await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), cts.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(harness.Provider.AccountCreateCalls).IsEqualTo(1);
        await Assert.That(harness.Provider.LinkCreateCalls).IsEqualTo(0);
        await Assert.That(harness.Repository.Connections).IsEmpty();
        OrganizerPaymentProviderAccountOperation operation = harness.OperationRepository.Operations.Single();
        await Assert.That(operation.StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.ManualReconciliationRequired);
        await Assert.That(operation.FailureCode).IsEqualTo("organizer_payment_provider_account_creation_canceled");
        await Assert.That(harness.Events).IsEquivalentTo(["persist-operation", "save-operation", "create-account", "save-operation"]);
    }

    [Test]
    public async Task CreateOnboardingLink_StaleRequestedResidueSettlesManualAndBlocksProviderRetry()
    {
        Harness harness = new();
        OrganizerPaymentProviderAccountOperation operation = harness.OperationRepository.AddRequested();

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_provider_manual_reconciliation_required");
        await Assert.That(harness.Provider.AccountCreateCalls).IsEqualTo(0);
        await Assert.That(harness.Repository.Connections).IsEmpty();
        await Assert.That(operation.StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.ManualReconciliationRequired);
        await Assert.That(operation.FailureCode).IsEqualTo("organizer_payment_provider_create_requested_recovered");
    }

    [Test]
    public async Task CreateOnboardingLink_DefinitiveRejectionTerminatesOperationAndLaterCallCreatesNewOperation()
    {
        Harness harness = new();
        harness.Provider.NextAccountResult = OrganizerPaymentProviderAccountCreationResult.Failed("organizer_payment_provider_account_invalid");

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> rejected = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);
        harness.Provider.NextAccountResult = OrganizerPaymentProviderAccountCreationResult.ManualReconciliationRequired();
        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> later = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(later.IsSuccess).IsFalse();
        await Assert.That(harness.Provider.AccountCreateCalls).IsEqualTo(2);
        await Assert.That(harness.OperationRepository.Operations.Count).IsEqualTo(2);
        await Assert.That(harness.OperationRepository.Operations.Any(operation => operation.StatusId == (int)OrganizerPaymentProviderAccountOperationStatus.ProviderRejected)).IsTrue();
        await Assert.That(harness.OperationRepository.Operations.Any(operation => operation.StatusId == (int)OrganizerPaymentProviderAccountOperationStatus.ManualReconciliationRequired)).IsTrue();
    }

    [Test]
    public async Task CreateOnboardingLink_RereadsActiveConnectionInsideSerializableTransactionAndDoesNotCreateDuplicateAfterRace()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection raced = null!;
        harness.Provider.NextAccountResult = OrganizerPaymentProviderAccountCreationResult.Created("acct_new");
        harness.Provider.NextLinkUrl = new Uri("https://payments.example/onboard/race");
        harness.UnitOfWork.BeforeSerializableOperationCallNumber = 2;
        harness.UnitOfWork.BeforeSerializableOperation = () => raced = harness.Repository.AddExisting("acct_new");

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.ReusedExistingConnection).IsTrue();
        await Assert.That(harness.Repository.Connections.Count).IsEqualTo(1);
        await Assert.That(harness.Repository.HistoricalReadCount).IsGreaterThanOrEqualTo(1);
        await Assert.That(harness.Provider.LinkCreateCalls).IsEqualTo(1);
        await Assert.That(harness.Events).IsEquivalentTo(["persist-operation", "save-operation", "create-account", "save-operation", "create-link"]);
        await Assert.That(harness.Provider.CalledInsideTransaction).IsFalse();
    }

    [Test]
    public async Task CreateOnboardingLink_RacedActiveConnectionWithDifferentAccountFailsWithoutCreatingHostedLink()
    {
        Harness harness = new();
        harness.Provider.NextAccountResult = OrganizerPaymentProviderAccountCreationResult.Created("acct_new");
        harness.UnitOfWork.BeforeSerializableOperationCallNumber = 2;
        harness.UnitOfWork.BeforeSerializableOperation = () => harness.Repository.AddExisting("acct_other");

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_provider_manual_reconciliation_required");
        await Assert.That(harness.Provider.LinkCreateCalls).IsEqualTo(0);
        await Assert.That(harness.OperationRepository.Operations.Single().StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.ManualReconciliationRequired);
        await Assert.That(harness.Events).IsEquivalentTo(["persist-operation", "save-operation", "create-account", "save-operation"]);
        await Assert.That(harness.Provider.CalledInsideTransaction).IsFalse();
    }

    [Test]
    public async Task CreateOnboardingLink_RereadsHistoricalExternalIdentityInsideTransactionAndFailsClosed()
    {
        Harness harness = new();
        harness.Provider.NextAccountResult = OrganizerPaymentProviderAccountCreationResult.Created("acct_new");
        harness.Provider.NextLinkUrl = new Uri("https://payments.example/onboard/bound");
        harness.UnitOfWork.BeforeSerializableOperationCallNumber = 2;
        harness.UnitOfWork.BeforeSerializableOperation = () => harness.Repository.AddExisting("acct_new", organizerActorId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000099"));

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_provider_manual_reconciliation_required");
        await Assert.That(harness.Repository.Connections.Count).IsEqualTo(1);
        await Assert.That(harness.Repository.Connections.Single().OrganizerActorId).IsNotEqualTo(ActorId);
        await Assert.That(harness.Provider.LinkCreateCalls).IsEqualTo(0);
        await Assert.That(harness.OperationRepository.Operations.Single().StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.ManualReconciliationRequired);
        await Assert.That(harness.Events).IsEquivalentTo(["persist-operation", "save-operation", "create-account", "save-operation"]);
        await Assert.That(harness.Provider.CalledInsideTransaction).IsFalse();
    }

    [Test]
    public async Task CreateOnboardingLink_TreatsReturnAndRefreshUrlsAsNavigationOnly()
    {
        Harness harness = new();
        Uri returnUrl = new("https://app.example/return?state=client-only");
        Uri refreshUrl = new("https://app.example/refresh?state=client-only");

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(returnUrl, refreshUrl), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(harness.Provider.LastLinkRequest!.ReturnUrl).IsEqualTo(returnUrl);
        await Assert.That(harness.Provider.LastLinkRequest.RefreshUrl).IsEqualTo(refreshUrl);
        await Assert.That(typeof(OrganizerPaymentProviderConnection).GetProperties().Any(property => property.Name.Contains("Url", StringComparison.Ordinal))).IsFalse();
        await Assert.That(harness.Repository.Connections.Single().StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
    }

    [Test]
    public async Task CreateOnboardingLink_UsesPersistedEventOrganizerAndConfiguredProviderScope()
    {
        Harness harness = new();

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> result = await harness.OnboardingHandler.Handle(harness.OnboardingCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(harness.Provider.LastAccountRequest!.TenantId).IsEqualTo(TenantId);
        await Assert.That(harness.Provider.LastAccountRequest.OrganizerActorId).IsEqualTo(ActorId);
        await Assert.That(harness.Provider.LastAccountRequest.ProviderCode).IsEqualTo("stripe");
        await Assert.That(harness.Provider.LastAccountRequest.ConnectPlatformId).IsEqualTo("platform-live-eu");
    }

    [Test]
    public async Task OnboardingCommand_UsesPaidCommerceAuthorizationMetadata()
    {
        var authorize = (AuthorizeResourceAttribute)Attribute.GetCustomAttribute(
            typeof(CreateOrganizerPaymentOnboardingLinkCommand),
            typeof(AuthorizeResourceAttribute))!;
        var request = (ISecureRequest)new CreateOrganizerPaymentOnboardingLinkCommand(
            EventId,
            new Uri("https://app.example/return"),
            new Uri("https://app.example/refresh"));

        await Assert.That(authorize.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(authorize.Action).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
        await Assert.That(request.ResourceId).IsEqualTo(EventId.ToString());
        await Assert.That(request.AuthorizationFacts).IsTypeOf<EventScopedAuthorizationFacts>();
    }

    [Test]
    public async Task EventPaymentConnectionQuery_ReturnsEnvelopeWhenNoConnectionExists()
    {
        Harness harness = new();

        EventOrganizerPaymentConnectionManagementDto? result = await harness.EventPaymentHandler.Handle(new GetEventOrganizerPaymentConnectionQuery(EventId), CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.EventId).IsEqualTo(EventId);
        await Assert.That(result.TenantId).IsEqualTo(TenantId);
        await Assert.That(result.OrganizerActorId).IsEqualTo(ActorId);
        await Assert.That(result.Connection).IsNull();
    }

    [Test]
    public async Task EventPaymentConnectionQuery_MissingOrganizerContextReturnsNull()
    {
        Harness harness = new(hasEventOrganizer: false);

        EventOrganizerPaymentConnectionManagementDto? result = await harness.EventPaymentHandler.Handle(new GetEventOrganizerPaymentConnectionQuery(EventId), CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadinessMapper_RequiresChargesBothCapabilitiesAndSatisfiedRequirementsForReady()
    {
        OrganizerPaymentProviderReadiness ready = Readiness(true, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderRequirementsState.Satisfied, Now);
        OrganizerPaymentProviderReadiness missingCharges = Readiness(false, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderRequirementsState.Satisfied, Now.AddMinutes(1));
        OrganizerPaymentProviderReadiness due = Readiness(true, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderRequirementsState.CurrentlyDue, Now.AddMinutes(2));

        OrganizerPaymentProviderReadinessObservation readyObservation = OrganizerPaymentReadinessMapper.ToObservation(ready);
        OrganizerPaymentProviderReadinessObservation missingChargesObservation = OrganizerPaymentReadinessMapper.ToObservation(missingCharges);
        OrganizerPaymentProviderReadinessObservation dueObservation = OrganizerPaymentReadinessMapper.ToObservation(due);

        await Assert.That(readyObservation.IsReady).IsTrue();
        await Assert.That(missingChargesObservation.IsReady).IsFalse();
        await Assert.That(missingChargesObservation.ChargeCapabilityState).IsEqualTo(ChargeCapabilityState.Inactive);
        await Assert.That(dueObservation.IsReady).IsFalse();
        await Assert.That(dueObservation.RequirementsState).IsEqualTo(ProviderRequirementsState.CurrentlyDue);
    }

    [Test]
    public async Task ReadinessReconciliation_CallsProviderOutsideTransactionAndAppliesReadyObservationInsideSerializableReload()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection connection = harness.Repository.AddExisting("acct_due");
        harness.Provider.EnqueueReadiness(Readiness(true, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderRequirementsState.Satisfied, Now.AddMinutes(10)));

        OrganizerPaymentReadinessReconciliationResult result = await harness.ReadinessService().ReconcileOnceAsync(CancellationToken.None);

        await Assert.That(result.UpdatedCount).IsEqualTo(1);
        await Assert.That(connection.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Ready);
        await Assert.That(harness.Provider.ReadinessCalls).IsEqualTo(1);
        await Assert.That(harness.Provider.CalledInsideTransaction).IsFalse();
        await Assert.That(harness.UnitOfWork.SerializableCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ReadinessReconciliation_ProviderFailureDoesNotSaveAndNextDueRowContinues()
    {
        Harness harness = new();
        OrganizerPaymentProviderConnection failed = harness.Repository.AddExisting("acct_failed");
        OrganizerPaymentProviderConnection next = harness.Repository.AddExisting("acct_next");
        harness.Provider.EnqueueReadinessFailure("organizer_payment_provider_network_failure", OrganizerPaymentProviderFailureKind.Network, "req_1");
        harness.Provider.EnqueueReadiness(Readiness(true, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderRequirementsState.Satisfied, Now.AddMinutes(10)));

        OrganizerPaymentReadinessReconciliationResult result = await harness.ReadinessService().ReconcileOnceAsync(CancellationToken.None);

        await Assert.That(result.FailureCount).IsEqualTo(1);
        await Assert.That(result.UpdatedCount).IsEqualTo(1);
        await Assert.That(failed.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
        await Assert.That(next.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Ready);
        await Assert.That(harness.Repository.SaveChangesCount).IsEqualTo(1);
        await Assert.That(result.Failures.Single().ProviderRequestId).IsEqualTo("req_1");
    }

    [Test]
    public async Task ReadinessReconciliation_StaleObservationAndTerminalRaceAreSkippedWithoutMutation()
    {
        Harness staleHarness = new();
        OrganizerPaymentProviderConnection stale = staleHarness.Repository.AddExisting("acct_stale");
        stale.ApplyReadiness(OrganizerPaymentReadinessMapper.ToObservation(Readiness(false, OrganizerPaymentProviderCapabilityState.Pending, OrganizerPaymentProviderCapabilityState.Pending, OrganizerPaymentProviderRequirementsState.CurrentlyDue, Now.AddMinutes(10))));
        staleHarness.Provider.EnqueueReadiness(Readiness(true, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderRequirementsState.Satisfied, Now.AddMinutes(9)));

        OrganizerPaymentReadinessReconciliationResult staleResult = await staleHarness.ReadinessService(staleIntervalMinutes: 1).ReconcileOnceAsync(CancellationToken.None);

        Harness terminalHarness = new();
        OrganizerPaymentProviderConnection terminal = terminalHarness.Repository.AddExisting("acct_terminal");
        terminalHarness.Provider.EnqueueReadiness(Readiness(true, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderRequirementsState.Satisfied, Now.AddMinutes(10)));
        terminalHarness.UnitOfWork.BeforeSerializableOperation = () => terminal.Disable("operator_disabled", Now.AddMinutes(8));

        OrganizerPaymentReadinessReconciliationResult terminalResult = await terminalHarness.ReadinessService().ReconcileOnceAsync(CancellationToken.None);

        await Assert.That(staleResult.SkippedCount).IsEqualTo(1);
        await Assert.That(stale.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Restricted);
        await Assert.That(terminalResult.SkippedCount).IsEqualTo(1);
        await Assert.That(terminal.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Disabled);
        await Assert.That(staleHarness.Repository.SaveChangesCount + terminalHarness.Repository.SaveChangesCount).IsEqualTo(0);
    }

    [Test]
    public async Task ReadinessReconciliation_CancellationPropagatesAndBatchLimitIsRespected()
    {
        Harness canceled = new();
        canceled.Repository.AddExisting("acct_cancel");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () => await canceled.ReadinessService().ReconcileOnceAsync(cts.Token))
            .Throws<OperationCanceledException>();

        Harness limited = new();
        limited.Repository.AddExisting("acct_one");
        limited.Repository.AddExisting("acct_two");
        limited.Provider.EnqueueReadiness(Readiness(true, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderCapabilityState.Active, OrganizerPaymentProviderRequirementsState.Satisfied, Now.AddMinutes(10)));

        OrganizerPaymentReadinessReconciliationResult result = await limited.ReadinessService(batchSize: 1).ReconcileOnceAsync(CancellationToken.None);

        await Assert.That(result.DueCount).IsEqualTo(1);
        await Assert.That(limited.Provider.ReadinessCalls).IsEqualTo(1);
        await Assert.That(limited.Repository.LastReadinessLimit).IsEqualTo(1);
    }

    private sealed class Harness
    {
        public Harness(bool authenticated = true, bool controlled = true, bool activeTenantUser = true, bool hasEventOrganizer = true)
        {
            Repository = new FakeOrganizerPaymentConnectionRepository();
            OperationRepository = new FakeOrganizerPaymentAccountOperationRepository();
            Events = [];
            IActorRepository actorRepository = Substitute.For<IActorRepository>();
            actorRepository.GetActorWithDetails(ActorId, Arg.Any<CancellationToken>()).Returns(UserActor());
            ITenantUserRepository tenantUserRepository = Substitute.For<ITenantUserRepository>();
            tenantUserRepository.IsActiveTenantUserAsync(TenantId, UserId, Arg.Any<CancellationToken>()).Returns(activeTenantUser);
            IOrganizationTenantRepository organizationTenantRepository = Substitute.For<IOrganizationTenantRepository>();
            IGroupTenantRepository groupTenantRepository = Substitute.For<IGroupTenantRepository>();
            IOrganizationMemberRepository organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
            IGroupMemberRepository groupMemberRepository = Substitute.For<IGroupMemberRepository>();
            ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
            currentUser.UserId.Returns(authenticated ? UserId : null);
            currentUser.IsAuthenticated.Returns(authenticated);
            if (!controlled)
            {
                actorRepository.GetActorWithDetails(ActorId, Arg.Any<CancellationToken>()).Returns(UserActor(Guid.Parse("018e4e5c-7f00-7000-8000-000000000088")));
            }

            ITenantContext tenantContext = Substitute.For<ITenantContext>();
            tenantContext.TenantId.Returns(TenantId);
            IEventRepository eventRepository = Substitute.For<IEventRepository>();
            var eventEntity = new Explore.Domain.Event
            {
                Id = EventId,
                Title = "Paid event",
                TenantId = TenantId,
                Tenant = null!,
                Actor = null!,
                VisibilityType = null!,
                EventStatus = null!,
                EventFormat = null!,
                OrganizerActorId = hasEventOrganizer ? ActorId : null,
                OrganizerActor = hasEventOrganizer ? UserActor() : null
            };
            eventRepository.GetAuthorizationTargetByIdAsync(EventId, Arg.Any<CancellationToken>()).Returns(eventEntity);
            eventRepository.GetEventWithDetails(EventId).Returns(eventEntity);
            IOrganizerPaymentCommerceConfiguration commerceConfiguration = Substitute.For<IOrganizerPaymentCommerceConfiguration>();
            commerceConfiguration.ProviderCode.Returns("stripe");
            commerceConfiguration.ConnectPlatformId.Returns("platform-live-eu");
            IUnitOfWork unitOfWork = new InlineSerializableUnitOfWork();
            UnitOfWork = new RecordingSerializableUnitOfWork();
            TimeProvider timeProvider = new FixedTimeProvider(Now);
            RecordHandler = new RecordOrganizerPaymentConnectionCommandHandler(Repository, actorRepository, tenantUserRepository, organizationTenantRepository, groupTenantRepository, organizationMemberRepository, groupMemberRepository, unitOfWork, tenantContext, currentUser, timeProvider);
            ReplaceHandler = new ReplaceOrganizerPaymentConnectionCommandHandler(Repository, actorRepository, tenantUserRepository, organizationTenantRepository, groupTenantRepository, organizationMemberRepository, groupMemberRepository, unitOfWork, tenantContext, currentUser, timeProvider);
            DisableHandler = new DisableOrganizerPaymentConnectionCommandHandler(Repository, actorRepository, tenantUserRepository, organizationTenantRepository, groupTenantRepository, organizationMemberRepository, groupMemberRepository, unitOfWork, tenantContext, currentUser, timeProvider);
            ListHandler = new ListOrganizerPaymentConnectionsQueryHandler(Repository, actorRepository, tenantUserRepository, organizationTenantRepository, groupTenantRepository, organizationMemberRepository, groupMemberRepository, tenantContext, currentUser);
            GetHandler = new GetOrganizerPaymentConnectionQueryHandler(Repository, actorRepository, tenantUserRepository, organizationTenantRepository, groupTenantRepository, organizationMemberRepository, groupMemberRepository, tenantContext, currentUser);
            EventPaymentHandler = new GetEventOrganizerPaymentConnectionQueryHandler(eventRepository, Repository, commerceConfiguration, tenantContext);
            Repository.Events = Events;
            OperationRepository.Events = Events;
            Provider = new RecordingOrganizerPaymentOnboardingProvider(() => UnitOfWork.InSerializableOperation, Events);
            OnboardingHandler = new CreateOrganizerPaymentOnboardingLinkCommandHandler(Repository, OperationRepository, eventRepository, commerceConfiguration, Provider, actorRepository, tenantUserRepository, organizationTenantRepository, groupTenantRepository, organizationMemberRepository, groupMemberRepository, UnitOfWork, tenantContext, currentUser, timeProvider, NullLogger<CreateOrganizerPaymentOnboardingLinkCommandHandler>.Instance);
        }

        public FakeOrganizerPaymentConnectionRepository Repository { get; }
        public FakeOrganizerPaymentAccountOperationRepository OperationRepository { get; }
        public RecordingOrganizerPaymentOnboardingProvider Provider { get; }
        public RecordingSerializableUnitOfWork UnitOfWork { get; }
        public List<string> Events { get; }
        public RecordOrganizerPaymentConnectionCommandHandler RecordHandler { get; }
        public ReplaceOrganizerPaymentConnectionCommandHandler ReplaceHandler { get; }
        public DisableOrganizerPaymentConnectionCommandHandler DisableHandler { get; }
        public CreateOrganizerPaymentOnboardingLinkCommandHandler OnboardingHandler { get; }
        public ListOrganizerPaymentConnectionsQueryHandler ListHandler { get; }
        public GetOrganizerPaymentConnectionQueryHandler GetHandler { get; }
        public GetEventOrganizerPaymentConnectionQueryHandler EventPaymentHandler { get; }

        public OrganizerPaymentReadinessReconciliationService ReadinessService(int batchSize = 25, int staleIntervalMinutes = 5) => new(
            Repository,
            Provider,
            UnitOfWork,
            Options.Create(new OrganizerPaymentReadinessReconciliationOptions
            {
                BatchSize = batchSize,
                StaleIntervalMinutes = staleIntervalMinutes,
                PollingIntervalSeconds = 60,
                InitialDelaySeconds = 0
            }),
            new FixedTimeProvider(Now.AddMinutes(20)));

        public RecordOrganizerPaymentConnectionCommand RecordCommand(string externalAccountId) =>
            new(TenantId, ActorId, "stripe", "platform-live-eu", externalAccountId);

        public CreateOrganizerPaymentOnboardingLinkCommand OnboardingCommand(Uri? returnUrl = null, Uri? refreshUrl = null) =>
            new(EventId, returnUrl ?? new Uri("https://app.example/return"), refreshUrl ?? new Uri("https://app.example/refresh"));
    }

    private sealed class InlineSerializableUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
    }

    private sealed class RecordingSerializableUnitOfWork : IUnitOfWork
    {
        public int SerializableCalls { get; private set; }
        public bool InSerializableOperation { get; private set; }
        public Action? BeforeSerializableOperation { get; set; }
        public int BeforeSerializableOperationCallNumber { get; set; } = 1;

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);

        public async Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            SerializableCalls++;
            if (SerializableCalls == BeforeSerializableOperationCallNumber)
            {
                BeforeSerializableOperation?.Invoke();
            }
            InSerializableOperation = true;
            try
            {
                return await operation(ct);
            }
            finally
            {
                InSerializableOperation = false;
            }
        }
    }

    private sealed class RecordingOrganizerPaymentOnboardingProvider(Func<bool> isInsideTransaction, List<string> events) : IOrganizerPaymentOnboardingProvider
    {
        private readonly Queue<OrganizerPaymentProviderReadinessResult> _readinessResults = [];

        public int AccountCreateCalls { get; private set; }
        public int LinkCreateCalls { get; private set; }
        public int ReadinessCalls { get; private set; }
        public bool CalledInsideTransaction { get; private set; }
        public OrganizerPaymentProviderAccountCreationResult NextAccountResult { get; set; } = OrganizerPaymentProviderAccountCreationResult.Created("acct_new");
        public Func<CancellationToken, Exception?>? AccountCreateExceptionFactory { get; set; }
        public Uri NextLinkUrl { get; set; } = new("https://payments.example/onboard/default");
        public OrganizerPaymentProviderAccountCreationRequest? LastAccountRequest { get; private set; }
        public OrganizerPaymentOnboardingLinkRequest? LastLinkRequest { get; private set; }

        public void EnqueueReadiness(OrganizerPaymentProviderReadiness readiness) =>
            _readinessResults.Enqueue(OrganizerPaymentProviderReadinessResult.Retrieved(readiness, readiness.EvidenceRevision));

        public void EnqueueReadinessFailure(
            string failureCode,
            OrganizerPaymentProviderFailureKind failureKind,
            string? providerRequestId) =>
            _readinessResults.Enqueue(OrganizerPaymentProviderReadinessResult.Failed(failureCode, failureKind, providerRequestId));

        public Task<OrganizerPaymentProviderAccountCreationResult> CreateAccountAsync(OrganizerPaymentProviderAccountCreationRequest request, CancellationToken cancellationToken)
        {
            AccountCreateCalls++;
            CalledInsideTransaction |= isInsideTransaction();
            LastAccountRequest = request;
            events.Add("create-account");
            Exception? exception = AccountCreateExceptionFactory?.Invoke(cancellationToken);
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(NextAccountResult);
        }

        public Task<OrganizerPaymentOnboardingLinkCreationResult> CreateOnboardingLinkAsync(OrganizerPaymentOnboardingLinkRequest request, CancellationToken cancellationToken)
        {
            LinkCreateCalls++;
            CalledInsideTransaction |= isInsideTransaction();
            LastLinkRequest = request;
            events.Add("create-link");
            return Task.FromResult(OrganizerPaymentOnboardingLinkCreationResult.Created(NextLinkUrl));
        }

        public Task<OrganizerPaymentProviderReadinessResult> GetReadinessAsync(OrganizerPaymentProviderReadinessRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadinessCalls++;
            CalledInsideTransaction |= isInsideTransaction();
            events.Add("get-readiness");
            return Task.FromResult(_readinessResults.Count == 0
                ? OrganizerPaymentProviderReadinessResult.Failed("not_used")
                : _readinessResults.Dequeue());
        }
    }

    private sealed class FakeOrganizerPaymentConnectionRepository : IOrganizerPaymentProviderConnectionRepository
    {
        public List<OrganizerPaymentProviderConnection> Connections { get; } = [];
        public List<string> Events { get; set; } = [];
        public int ReadCount { get; private set; }
        public int HistoricalReadCount { get; private set; }
        public int SaveChangesCount { get; private set; }
        public int LastReadinessLimit { get; private set; }

        public OrganizerPaymentProviderConnection AddExisting(string externalAccountId, Guid? organizerActorId = null, Guid? tenantId = null)
        {
            OrganizerPaymentProviderConnection connection = OrganizerPaymentProviderConnection.Create(
                Guid.CreateVersion7(),
                tenantId ?? TenantId,
                organizerActorId ?? ActorId,
                "stripe",
                "platform-live-eu",
                externalAccountId,
                Now);
            Connections.Add(connection);
            return connection;
        }

        public Task<OrganizerPaymentProviderConnection?> GetActiveByScopeAsync(Guid tenantId, Guid organizerActorId, string providerCode, string connectPlatformId, CancellationToken cancellationToken) =>
            Task.FromResult(Connections.SingleOrDefault(connection =>
                connection.TenantId == tenantId
                && connection.OrganizerActorId == organizerActorId
                && connection.ProviderCode == providerCode
                && connection.ConnectPlatformId == connectPlatformId
                && connection.StatusId is not (int)OrganizerPaymentProviderConnectionStatusEnum.Disabled and not (int)OrganizerPaymentProviderConnectionStatusEnum.Replaced));

        public Task<OrganizerPaymentProviderConnection?> GetHistoricalByExternalAccountAsync(string providerCode, string connectPlatformId, string externalAccountId, CancellationToken cancellationToken)
        {
            HistoricalReadCount++;
            return Task.FromResult(Connections.SingleOrDefault(connection =>
                connection.ProviderCode == providerCode
                && connection.ConnectPlatformId == connectPlatformId
                && connection.ExternalAccountId == externalAccountId));
        }

        public Task<IReadOnlyList<OrganizerPaymentProviderConnection>> ListHistoricalByExternalAccountAsync(string providerCode, string externalAccountId, int limit, CancellationToken cancellationToken)
        {
            HistoricalReadCount++;
            return Task.FromResult<IReadOnlyList<OrganizerPaymentProviderConnection>>(Connections
                .Where(connection => connection.ProviderCode == providerCode && connection.ExternalAccountId == externalAccountId)
                .Take(limit)
                .ToArray());
        }

        public Task<IReadOnlyList<OrganizerPaymentProviderConnection>> ListDueReadinessChecksAsync(DateTime observedBefore, int limit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastReadinessLimit = limit;
            return Task.FromResult<IReadOnlyList<OrganizerPaymentProviderConnection>>(Connections
                .Where(connection =>
                    !connection.IsDeleted
                    && connection.StatusId is (int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding or (int)OrganizerPaymentProviderConnectionStatusEnum.Restricted
                    && (connection.LastReadinessObservedAt is null || connection.LastReadinessObservedAt < observedBefore))
                .OrderBy(connection => connection.LastReadinessObservedAt is null ? 0 : 1)
                .ThenBy(connection => connection.LastReadinessObservedAt)
                .ThenBy(connection => connection.CreatedAt)
                .Take(limit)
                .ToArray());
        }

        public Task<OrganizerPaymentProviderConnection?> GetByTenantProviderAndExternalAccountForUpdateAsync(Guid tenantId, string providerCode, string externalAccountId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(Connections.SingleOrDefault(connection =>
                connection.TenantId == tenantId
                && connection.ProviderCode == providerCode
                && connection.ExternalAccountId == externalAccountId));
        }

        public Task<OrganizerPaymentProviderConnection?> GetByTenantAndIdForUpdateAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(Connections.SingleOrDefault(connection => connection.TenantId == tenantId && connection.Id == connectionId));
        }

        public Task<IReadOnlyList<OrganizerPaymentProviderConnection>> ListByTenantAndActorAsync(Guid tenantId, Guid organizerActorId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<OrganizerPaymentProviderConnection>>(Connections.Where(connection => connection.TenantId == tenantId && connection.OrganizerActorId == organizerActorId).ToArray());
        }

        public Task CreateAsync(OrganizerPaymentProviderConnection connection, CancellationToken cancellationToken)
        {
            Connections.Add(connection);
            Events.Add("persist");
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCount++;
            Events.Add("save");
            return Task.CompletedTask;
        }
    }

    private static OrganizerPaymentProviderReadiness Readiness(
        bool chargesEnabled,
        OrganizerPaymentProviderCapabilityState cardPayments,
        OrganizerPaymentProviderCapabilityState transfers,
        OrganizerPaymentProviderRequirementsState requirements,
        DateTime observedAt) => new(
            chargesEnabled,
            cardPayments,
            transfers,
            requirements,
            requirements == OrganizerPaymentProviderRequirementsState.CurrentlyDue ? ["business_profile.url"] : [],
            [],
            [],
            null,
            "BE",
            ["EUR"],
            observedAt,
            $"readiness-{observedAt.Ticks}");

    private sealed class FakeOrganizerPaymentAccountOperationRepository : IOrganizerPaymentProviderAccountOperationRepository
    {
        public List<OrganizerPaymentProviderAccountOperation> Operations { get; } = [];
        public List<string> Events { get; set; } = [];

        public OrganizerPaymentProviderAccountOperation AddRequested()
        {
            OrganizerPaymentProviderAccountOperation operation = OrganizerPaymentProviderAccountOperation.CreateRequested(
                Guid.CreateVersion7(),
                TenantId,
                ActorId,
                "stripe",
                "platform-live-eu",
                Now);
            Operations.Add(operation);
            return operation;
        }

        public Task<OrganizerPaymentProviderAccountOperation?> GetActiveByScopeAsync(Guid tenantId, Guid organizerActorId, string providerCode, string connectPlatformId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Operations.SingleOrDefault(operation =>
                operation.TenantId == tenantId
                && operation.OrganizerActorId == organizerActorId
                && operation.ProviderCode == providerCode
                && operation.ConnectPlatformId == connectPlatformId
                && operation.ActiveUniquenessSlot == "active"));
        }

        public Task<OrganizerPaymentProviderAccountOperation?> GetByTenantAndIdForUpdateAsync(Guid tenantId, Guid operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Operations.SingleOrDefault(operation => operation.TenantId == tenantId && operation.Id == operationId));
        }

        public Task CreateAsync(OrganizerPaymentProviderAccountOperation operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add(operation);
            Events.Add("persist-operation");
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add("save-operation");
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Actor UserActor(Guid? userId = null) => new()
    {
        Id = ActorId,
        UserId = userId ?? UserId,
        ActorTypeId = (int)ActorTypeEnum.User,
        ActorType = null!,
        Pii = new ActorPii { DisplayName = "Organizer" }
    };
}
