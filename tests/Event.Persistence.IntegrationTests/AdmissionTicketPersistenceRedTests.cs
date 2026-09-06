// ABOUTME: Specifies Phase 20 admission-ticket, child-credential, and delivery-intent persistence behavior.
// ABOUTME: Proves provider parity, real PostgreSQL collisions, and public Application issuance replay.

using System.Data.Common;
using Event.Persistence.IntegrationTests;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Infrastructure.Services.Registration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace EventPersistence.IntegrationTests;

[Category("Phase20AdmissionPersistenceCharacterization")]
public sealed class AdmissionTicketPersistenceCharacterizationTests
{
    [Test]
    public async Task DeliveryIntentModelPersistsProtectedRecoveryLifecycleWithoutBearerColumns()
    {
        await using ExploreDbContext context = AdmissionPersistenceSurface.CreateModelContext("PostgreSql");
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType intent = model.FindEntityType(typeof(AdmissionDeliveryIntent))!;
        string[] properties = intent.GetProperties().Select(property => property.Name).ToArray();

        await Assert.That(properties).Contains(nameof(AdmissionDeliveryIntent.ProtectedCredential));
        await Assert.That(properties).Contains(nameof(AdmissionDeliveryIntent.ProtectionVersion));
        await Assert.That(properties).Contains(nameof(AdmissionDeliveryIntent.RoutedAt));
        await Assert.That(properties).Contains(nameof(AdmissionDeliveryIntent.HandoffCompletedAt));
        await Assert.That(properties).Contains(nameof(AdmissionDeliveryIntent.HandoffReceiptId));
        await Assert.That(properties.Any(property => property.Contains("Plaintext", StringComparison.OrdinalIgnoreCase) ||
            property.Contains("Bearer", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(intent.FindProperty(nameof(AdmissionDeliveryIntent.ProtectedCredential))!.GetMaxLength())
            .IsEqualTo(2048);
        await Assert.That(intent.FindProperty(nameof(AdmissionDeliveryIntent.HandoffReceiptId))!.GetMaxLength())
            .IsEqualTo(200);
        await Assert.That(intent.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_admission_delivery_intents_handoff_receipt");
    }

    [Test]
    public async Task CurrentAssignmentsAndFinalizationEffectsPersistWithTenantQualifiedReplayFences()
    {
        await using ExploreDbContext context = AdmissionPersistenceSurface.CreateModelContext("PostgreSql");
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType assignment = model.FindEntityType(typeof(RegistrationTicketAssignment))!;
        IEntityType effect = model.FindEntityType(typeof(RegistrationFinalizationEffect))!;

        await Assert.That(assignment.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(effect.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(assignment.GetKeys().Any(key => AdmissionPersistenceSurface.HasProperties(
            key.Properties,
            nameof(RegistrationTicketAssignment.TenantId),
            nameof(RegistrationTicketAssignment.RegistrationOrderId),
            nameof(RegistrationTicketAssignment.Id),
            nameof(RegistrationTicketAssignment.RegistrationOrderLineId)))).IsTrue();
        await Assert.That(assignment.GetIndexes().Any(index => index.IsUnique && AdmissionPersistenceSurface.HasProperties(
            index.Properties,
            nameof(RegistrationTicketAssignment.TenantId),
            nameof(RegistrationTicketAssignment.RegistrationOrderLineId),
            nameof(RegistrationTicketAssignment.Ordinal)))).IsTrue();
        await Assert.That(effect.GetIndexes().Any(index => index.IsUnique && AdmissionPersistenceSurface.HasProperties(
            index.Properties,
            nameof(RegistrationFinalizationEffect.TenantId),
            nameof(RegistrationFinalizationEffect.RegistrationOrderId)))).IsTrue();
        await Assert.That(effect.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(RegistrationOrder) &&
            AdmissionPersistenceSurface.HasProperties(
                foreignKey.Properties,
                nameof(RegistrationFinalizationEffect.TenantId),
                nameof(RegistrationFinalizationEffect.EventId),
                nameof(RegistrationFinalizationEffect.RegistrationOrderId)) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
    }
}

[Category("Phase20AdmissionPersistenceModelRed")]
public sealed class AdmissionTicketPersistenceModelRedTests
{
    [Test]
    public async Task AdmissionModelsOwnTenantQualifiedKeyedDigestsWithoutPlaintextCapabilities()
    {
        await using ExploreDbContext context = AdmissionPersistenceSurface.CreateModelContext("PostgreSql");
        (IEntityType ticket, IEntityType credential) =
            AdmissionPersistenceSurface.RequireAdmissionEntities(context.GetService<IDesignTimeModel>().Model);

        await Assert.That(ticket.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(credential.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(ticket.GetIndexes().Any(index => index.IsUnique && AdmissionPersistenceSurface.HasProperties(
            index.Properties,
            nameof(AdmissionTicket.TenantId),
            nameof(AdmissionTicket.RegistrationTicketAssignmentId)))).IsTrue();
        await Assert.That(credential.GetIndexes().Any(index => index.IsUnique && AdmissionPersistenceSurface.HasProperties(
            index.Properties,
            nameof(AdmissionTicketCredential.TenantId),
            nameof(AdmissionTicketCredential.AdmissionTicketId),
            nameof(AdmissionTicketCredential.CredentialVersion)))).IsTrue();
        await Assert.That(credential.GetIndexes().Any(index => index.IsUnique && AdmissionPersistenceSurface.HasProperties(
            index.Properties,
            nameof(AdmissionTicketCredential.TenantId),
            nameof(AdmissionTicketCredential.AdmissionTicketId),
            "ActiveUniquenessSlot") &&
            string.IsNullOrWhiteSpace(index.GetFilter()))).IsTrue();
        await Assert.That(credential.GetIndexes().Any(index => index.IsUnique && AdmissionPersistenceSurface.HasProperties(
            index.Properties,
            nameof(AdmissionTicketCredential.TenantId),
            nameof(AdmissionTicketCredential.LookupKeyVersion),
            nameof(AdmissionTicketCredential.LookupDigest)))).IsTrue();
        await Assert.That(credential.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType == ticket &&
            AdmissionPersistenceSurface.HasProperties(
                foreignKey.Properties,
                nameof(AdmissionTicketCredential.TenantId),
                nameof(AdmissionTicketCredential.AdmissionTicketId)) &&
            AdmissionPersistenceSurface.HasProperties(
                foreignKey.PrincipalKey.Properties,
                nameof(AdmissionTicket.TenantId),
                nameof(AdmissionTicket.Id)) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();

        string[] forbiddenFragments = ["Plaintext", "Bearer", "RawCredential", "CapabilityToken", "Secret"];
        string[] persistedNames = new[] { ticket, credential }
            .SelectMany(entity => entity.GetProperties())
            .SelectMany(property => new[] { property.Name, property.GetColumnName() })
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        await Assert.That(persistedNames.Any(name => forbiddenFragments.Any(fragment =>
            name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))).IsFalse();
        await Assert.That(credential.FindProperty(
                nameof(AdmissionTicketCredential.LookupDigest))!.ClrType)
            .IsEqualTo(typeof(string));
        await Assert.That(ticket.FindProperty("CredentialDigest")).IsNull();
        await Assert.That(ticket.FindProperty("CredentialPlaintext")).IsNull();
        await Assert.That(ticket.FindProperty(nameof(AdmissionTicket.Id))!.ValueGenerated)
            .IsEqualTo(ValueGenerated.Never);
        await Assert.That(credential.FindProperty(nameof(AdmissionTicketCredential.Id))!.ValueGenerated)
            .IsEqualTo(ValueGenerated.Never);
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task EveryProviderMapsTheSameChildCredentialAndCollisionConstraints(string provider)
    {
        await using ExploreDbContext context = AdmissionPersistenceSurface.CreateModelContext(provider);
        (IEntityType ticket, IEntityType credential) =
            AdmissionPersistenceSurface.RequireAdmissionEntities(context.GetService<IDesignTimeModel>().Model);

        await Assert.That(ticket.GetIndexes().Any(index => index.IsUnique && AdmissionPersistenceSurface.HasProperties(
            index.Properties,
            nameof(AdmissionTicket.TenantId),
            nameof(AdmissionTicket.RegistrationTicketAssignmentId)))).IsTrue();
        await Assert.That(credential.GetIndexes().Any(index => index.IsUnique && AdmissionPersistenceSurface.HasProperties(
            index.Properties,
            nameof(AdmissionTicketCredential.TenantId),
            nameof(AdmissionTicketCredential.LookupKeyVersion),
            nameof(AdmissionTicketCredential.LookupDigest)))).IsTrue();
        await Assert.That(credential.GetIndexes().Any(index => index.IsUnique && AdmissionPersistenceSurface.HasProperties(
            index.Properties,
            nameof(AdmissionTicketCredential.TenantId),
            nameof(AdmissionTicketCredential.AdmissionTicketId),
            "ActiveUniquenessSlot"))).IsTrue();
        await Assert.That(new[] { ticket, credential }.All(entity =>
            entity.FindProperty(nameof(AdmissionTicket.Id))!
                .GetDefaultValueSql() is null)).IsTrue();
    }
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
[Category("Phase20AdmissionPersistencePostgreSqlRed")]
public sealed class AdmissionTicketPersistencePostgreSqlRedTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task SameKeyVersionAndDigestPersistAcrossTenantsAndRepositoryLookupCannotCrossTenant()
    {
        AdmissionPersistenceSurface surface = AdmissionPersistenceSurface.RequirePublicSurface();
        await fixture.ResetAsync();
        SeededAssignment tenantA = await SeedAssignmentAsync("digest-a");
        SeededAssignment tenantB = await SeedAssignmentAsync("digest-b");
        const int keyVersion = 7;
        string sharedDigest = Digest(7);
        TicketGraph graphA = surface.IssueTicketGraph(tenantA, Guid.CreateVersion7(), Guid.CreateVersion7(), 1, keyVersion, sharedDigest);
        TicketGraph graphB = surface.IssueTicketGraph(tenantB, Guid.CreateVersion7(), Guid.CreateVersion7(), 1, keyVersion, sharedDigest);

        await PersistTicketAsync(tenantA.TenantId, graphA, CancellationToken.None);
        await PersistTicketAsync(tenantB.TenantId, graphB, CancellationToken.None);

        await using ExploreDbContext tenantAContext = TenantContext(tenantA.TenantId);
        var tenantARepository = new AdmissionTicketRepository(tenantAContext);
        AdmissionTicket? foundA = await tenantARepository.GetByCredentialDigestAsync(
            tenantA.TenantId, keyVersion, sharedDigest, CancellationToken.None);
        AdmissionTicket? blockedBFromTenantA = await tenantARepository.GetByCredentialDigestAsync(
            tenantB.TenantId, keyVersion, sharedDigest, CancellationToken.None);
        AdmissionTicket? absent = await tenantARepository.GetByCredentialDigestAsync(
            Guid.CreateVersion7(), keyVersion, sharedDigest, CancellationToken.None);
        AdmissionTicket? replayA = await tenantARepository.GetByAssignmentAsync(
            tenantA.TenantId, tenantA.AssignmentId, CancellationToken.None);

        await using ExploreDbContext tenantBContext = TenantContext(tenantB.TenantId);
        var tenantBRepository = new AdmissionTicketRepository(tenantBContext);
        AdmissionTicket? foundB = await tenantBRepository.GetByCredentialDigestAsync(
            tenantB.TenantId, keyVersion, sharedDigest, CancellationToken.None);

        await Assert.That(foundA).IsNotNull();
        await Assert.That(foundB).IsNotNull();
        await Assert.That(foundA!.Id).IsEqualTo(graphA.TicketId);
        await Assert.That(foundB!.Id).IsEqualTo(graphB.TicketId);
        await Assert.That(foundA.TenantId).IsEqualTo(tenantA.TenantId);
        await Assert.That(foundB.TenantId).IsEqualTo(tenantB.TenantId);
        await Assert.That(blockedBFromTenantA).IsNull();
        await Assert.That(absent).IsNull();
        await Assert.That(replayA).IsNotNull();
        await Assert.That(replayA!.Id).IsEqualTo(graphA.TicketId);
        await Assert.That(graphA.TicketId.Version).IsEqualTo(7);
        await Assert.That(graphB.TicketId.Version).IsEqualTo(7);
    }

    [Test]
    public async Task CredentialRotationPersistsNewDigestAndMakesPreviousDigestUnresolvable()
    {
        AdmissionPersistenceSurface surface = AdmissionPersistenceSurface.RequirePublicSurface();
        await fixture.ResetAsync();
        SeededAssignment seed = await SeedAssignmentAsync("rotation");
        string previousDigest = Digest(11);
        string replacementDigest = Digest(12);
        TicketGraph original = surface.IssueTicketGraph(
            seed, Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 11, previousDigest);
        await PersistTicketAsync(seed.TenantId, original, CancellationToken.None);

        await using ExploreDbContext context = TenantContext(seed.TenantId);
        var repository = new AdmissionTicketRepository(context);
        await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(
            async token =>
            {
                AdmissionTicket managed = await repository.GetByIdForUpdateAsync(
                    seed.TenantId,
                    original.TicketId,
                    token) ?? throw new InvalidOperationException("Missing admission ticket.");
                managed.RotateCredential(
                    Guid.CreateVersion7(), 2, 12, replacementDigest, UtcNow.AddMinutes(1));
                await repository.SaveChangesAsync(token);
                return true;
            },
            CancellationToken.None);
        context.ChangeTracker.Clear();

        AdmissionTicket? oldLookup = await repository.GetByCredentialDigestAsync(
            seed.TenantId, 11, previousDigest, CancellationToken.None);
        AdmissionTicket? newLookup = await repository.GetByCredentialDigestAsync(
            seed.TenantId, 12, replacementDigest, CancellationToken.None);

        await Assert.That(oldLookup).IsNull();
        await Assert.That(newLookup).IsNotNull();
        await Assert.That(newLookup!.Id).IsEqualTo(original.TicketId);
    }

    [Test]
    public async Task PublicApplicationServiceIssuesOnceAndReplayPreservesTicketCredentialDeliveryAndDispatchCounts()
    {
        await fixture.ResetAsync();
        SeededAssignment seed = await SeedAssignmentAsync("application-qa");
        RegistrationFinalizationEffect effect = RegistrationFinalizationEffect.Create(seed.Order, UtcNow);
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            setup.RegistrationFinalizationEffects.Add(effect);
            await setup.SaveChangesAsync();
        }

        await using ExploreDbContext context = TenantContext(seed.TenantId);
        var dispatcher = new RecordingAdmissionDispatcher(context);
        var service = new AdmissionIssuanceService(
            new AdmissionIssuanceRepository(
                context,
                ReadyParticipantAdmissionReadinessAuthority.Instance),
            new AdmissionCredentialDigestService(
                new AdmissionSecretResolver(),
                Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 7 })),
            new AdmissionDeliveryEnvelopeProtector(new EphemeralDataProtectionProvider()),
            dispatcher,
            new EfCoreUnitOfWork(context),
            new FixedAdmissionTimeProvider(UtcNow));
        var request = new AdmissionIssuanceRequest(seed.TenantId, seed.OrderId, effect.Id, "ConfirmedFreeOrder");

        AdmissionIssuanceResult issued = await service.IssueConfirmedAsync(request, CancellationToken.None);
        AdmissionIssuanceResult replay = await service.IssueConfirmedAsync(request, CancellationToken.None);
        int ticketCount = await context.AdmissionTickets.CountAsync();
        int credentialCount = await context.AdmissionTicketCredentials.CountAsync();
        int deliveryCount = await context.AdmissionDeliveryIntents.CountAsync();

        await Assert.That(issued.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        await Assert.That(issued.OneTimeCredentials.Count).IsEqualTo(1);
        await Assert.That(issued.DeliveryOutcome).IsEqualTo(AdmissionDeliveryOutcome.Delivered);
        await Assert.That(replay.Outcome).IsEqualTo(AdmissionIssuanceOutcome.AlreadyIssued);
        await Assert.That(replay.ExistingTicketIds).IsEquivalentTo(issued.IssuedTicketIds);
        await Assert.That(replay.OneTimeCredentials).IsEmpty();
        await Assert.That(ticketCount).IsEqualTo(1);
        await Assert.That(credentialCount).IsEqualTo(1);
        await Assert.That(deliveryCount).IsEqualTo(1);
        await Assert.That(dispatcher.DispatchCount).IsEqualTo(1);
        Console.WriteLine($"ADMISSION_ISSUANCE_QA outcome={issued.Outcome} replay={replay.Outcome} tickets={ticketCount} credentials={credentialCount} deliveryIntents={deliveryCount} dispatches={dispatcher.DispatchCount}");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DefaultIssuanceCompositionRejectsPendingAndRevokedReadinessWithoutEffects(
        bool revoked)
    {
        await fixture.ResetAsync();
        SeededAssignment seed = await SeedAssignmentAsync(
            revoked ? "readiness-revoked" : "readiness-pending");
        ParticipantAdmissionEligibility eligibility =
            ParticipantAdmissionEligibility.Create(
                seed.TenantId,
                seed.EventId,
                seed.Assignment,
                seed.Participant,
                consentRequired: false,
                approvalRequired: false,
                UtcNow);
        if (revoked)
        {
            eligibility.Revoke(
                Guid.CreateVersion7(),
                UtcNow.AddMinutes(1),
                Guid.CreateVersion7());
        }
        RegistrationFinalizationEffect effect =
            RegistrationFinalizationEffect.Create(seed.Order, UtcNow);

        await using ExploreDbContext context = TenantContext(seed.TenantId);
        context.AddRange(eligibility, effect);
        await context.SaveChangesAsync();
        var service = new AdmissionIssuanceService(
            new AdmissionIssuanceRepository(context),
            new AdmissionCredentialDigestService(
                new AdmissionSecretResolver(),
                Options.Create(new AdmissionCredentialOptions
                {
                    ActiveKeyVersion = 7,
                })),
            new AdmissionDeliveryEnvelopeProtector(
                new EphemeralDataProtectionProvider()),
            new RecordingAdmissionDispatcher(context),
            new EfCoreUnitOfWork(context),
            new FixedAdmissionTimeProvider(UtcNow));

        AdmissionIssuanceResult result = await service.IssueConfirmedAsync(
            new AdmissionIssuanceRequest(
                seed.TenantId,
                seed.OrderId,
                effect.Id,
                AdmissionIssuanceAuthority.ConfirmedFreeOrder),
            CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(AdmissionIssuanceOutcome.ReadinessPending);
        await Assert.That(await context.AdmissionTickets.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AdmissionTicketCredentials.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AdmissionDeliveryIntents.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ExactRefundAndCancellationFactsPersistCredentialRevocationWhilePartialFactsPreserve()
    {
        SeededAssignment refundSeed = await SeedAssignmentAsync("refund-revocation");
        await IssueSeedAsync(refundSeed);

        await using (ExploreDbContext context = TenantContext(refundSeed.TenantId))
        {
            var revocation = new AdmissionRevocationService(
                new AdmissionRevocationRepository(context),
                new EfCoreUnitOfWork(context),
                new FixedAdmissionTimeProvider(UtcNow.AddMinutes(2)));
            AdmissionRevocationResult partial = await revocation.ReconcileAsync(
                new AdmissionRevocationRequest(
                    refundSeed.TenantId,
                    refundSeed.OrderId,
                    AdmissionRevocationService.RefundReconciledReason,
                    [new AdmissionRefundAllocationFact(refundSeed.LineId, true, 0, 1)]),
                CancellationToken.None);

            await Assert.That(partial.RevokedTicketIds).IsEmpty();
            await Assert.That(partial.PreservedTicketIds.Count).IsEqualTo(1);
        }

        await using (ExploreDbContext context = TenantContext(refundSeed.TenantId))
        {
            var revocation = new AdmissionRevocationService(
                new AdmissionRevocationRepository(context),
                new EfCoreUnitOfWork(context),
                new FixedAdmissionTimeProvider(UtcNow.AddMinutes(3)));
            AdmissionRevocationResult full = await revocation.ReconcileAsync(
                new AdmissionRevocationRequest(
                    refundSeed.TenantId,
                    refundSeed.OrderId,
                    AdmissionRevocationService.RefundReconciledReason,
                    [new AdmissionRefundAllocationFact(refundSeed.LineId, true, 1, 1)]),
                CancellationToken.None);

            await Assert.That(full.RevokedTicketIds.Count).IsEqualTo(1);
            AdmissionTicket revoked = await context.AdmissionTickets
                .Include(ticket => ticket.Credentials)
                .SingleAsync(ticket => ticket.RegistrationOrderId == refundSeed.OrderId);
            await Assert.That((AdmissionTicketStatusEnum)revoked.AdmissionTicketStatusId)
                .IsEqualTo(AdmissionTicketStatusEnum.Revoked);
            await Assert.That(revoked.Credentials.Single().RevokedAt).IsNotNull();
        }

        SeededAssignment cancellationSeed = await SeedAssignmentAsync("cancellation-revocation");
        await IssueSeedAsync(cancellationSeed);
        await using (ExploreDbContext context = TenantContext(cancellationSeed.TenantId))
        {
            var repository = new AdmissionRevocationRepository(context);
            var revocation = new AdmissionRevocationService(
                repository,
                new EfCoreUnitOfWork(context),
                new FixedAdmissionTimeProvider(UtcNow.AddMinutes(2)));
            var eventCancellation = new AdmissionEventCancellationService(
                repository,
                revocation,
                new FixedAdmissionTimeProvider(UtcNow.AddMinutes(2)));
            int cancelled = await eventCancellation.ReconcileAsync(
                Guid.CreateVersion7(),
                cancellationSeed.TenantId,
                cancellationSeed.EventId,
                CancellationToken.None);

            await Assert.That(cancelled).IsEqualTo(1);
            AdmissionTicket ticket = await context.AdmissionTickets
                .Include(value => value.Credentials)
                .SingleAsync(value => value.RegistrationOrderId == cancellationSeed.OrderId);
            await Assert.That((AdmissionTicketStatusEnum)ticket.AdmissionTicketStatusId)
                .IsEqualTo(AdmissionTicketStatusEnum.Cancelled);
            await Assert.That(ticket.Credentials.Single().RevokedAt).IsNotNull();
        }
    }

    [Test]
    public async Task ReconciledPaidFinalizationIssuesOnceFromExactPersistedPaymentEvidence()
    {
        SeededAssignment seed = await SeedPaidAssignmentAsync("paid-finalization");
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        RegistrationFinalizationEffect effect = await context.RegistrationFinalizationEffects
            .SingleAsync(value => value.RegistrationOrderId == seed.OrderId);
        var dispatcher = new RecordingAdmissionDispatcher(context);
        var service = new AdmissionIssuanceService(
            new AdmissionIssuanceRepository(
                context,
                ReadyParticipantAdmissionReadinessAuthority.Instance),
            new AdmissionCredentialDigestService(
                new AdmissionSecretResolver(),
                Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 7 })),
            new AdmissionDeliveryEnvelopeProtector(new EphemeralDataProtectionProvider()),
            dispatcher,
            new EfCoreUnitOfWork(context),
            new FixedAdmissionTimeProvider(UtcNow.AddMinutes(1)));
        var request = new AdmissionIssuanceRequest(
            seed.TenantId,
            seed.OrderId,
            effect.Id,
            AdmissionIssuanceAuthority.ReconciledPaidFinalization);

        AdmissionIssuanceResult issued = await service.IssueConfirmedAsync(
            request, CancellationToken.None);
        AdmissionIssuanceResult replay = await service.IssueConfirmedAsync(
            request, CancellationToken.None);
        int ticketCount = await context.AdmissionTickets.CountAsync(
            ticket => ticket.RegistrationOrderId == seed.OrderId);

        await Assert.That(issued.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        await Assert.That(replay.Outcome).IsEqualTo(AdmissionIssuanceOutcome.AlreadyIssued);
        await Assert.That(ticketCount).IsEqualTo(1);
    }

    [Test]
    public async Task LostCommitAcknowledgementReloadsCommittedPaidIssuanceOutsideTransaction()
    {
        SeededAssignment seed = await SeedPaidAssignmentAsync("paid-commit-acknowledgement");
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        RegistrationFinalizationEffect effect = await context.RegistrationFinalizationEffects
            .SingleAsync(value => value.RegistrationOrderId == seed.OrderId);
        IAdmissionDeliveryDispatcher dispatcher = Substitute.For<IAdmissionDeliveryDispatcher>();
        dispatcher.DispatchAsync(
                Arg.Any<AdmissionDeliveryDispatchRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdmissionDeliveryDispatchResult(AdmissionDeliveryOutcome.Delivered));
        var service = new AdmissionIssuanceService(
            new AdmissionIssuanceRepository(
                context,
                ReadyParticipantAdmissionReadinessAuthority.Instance),
            new AdmissionCredentialDigestService(
                new AdmissionSecretResolver(),
                Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 7 })),
            new AdmissionDeliveryEnvelopeProtector(new EphemeralDataProtectionProvider()),
            dispatcher,
            new CommitAcknowledgementLostUnitOfWork(new EfCoreUnitOfWork(context)),
            new FixedAdmissionTimeProvider(UtcNow.AddMinutes(1)));

        AdmissionIssuanceResult result = await service.IssueConfirmedAsync(
            new AdmissionIssuanceRequest(
                seed.TenantId,
                seed.OrderId,
                effect.Id,
                AdmissionIssuanceAuthority.ReconciledPaidFinalization),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.AlreadyIssued);
        await Assert.That(result.DeliveryOutcome).IsEqualTo(AdmissionDeliveryOutcome.Delivered);
        await dispatcher.Received(1).DispatchAsync(
            Arg.Any<AdmissionDeliveryDispatchRequest>(),
            Arg.Any<CancellationToken>());
        await Assert.That(await context.AdmissionTickets.CountAsync(
            ticket => ticket.RegistrationOrderId == seed.OrderId)).IsEqualTo(1);
        await Assert.That(await context.AdmissionTickets
            .Where(ticket => ticket.RegistrationOrderId == seed.OrderId)
            .SelectMany(ticket => ticket.Credentials)
            .CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task FullRefundBeforeDelayedPaidFinalizationCannotCreateAdmission()
    {
        SeededAssignment seed = await SeedPaidAssignmentAsync("pre-issuance-refund");
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        PaymentAttempt payment = await context.PaymentAttempts
            .SingleAsync(value => value.RegistrationOrderId == seed.OrderId);
        PaidOrderAcceptanceSnapshot acceptance =
            await context.PaidOrderAcceptanceSnapshots
                .Include(value => value.Lines)
                .SingleAsync(value => value.Id == payment.PaidOrderAcceptanceSnapshotId);
        RefundAttempt refund = RefundAttempt.Create(
            Guid.CreateVersion7(),
            seed.TenantId,
            payment.Id,
            acceptance,
            payment.RecipientSnapshot.ExternalAccountId,
            payment.ProviderPaymentId!,
            $"refund:{Guid.CreateVersion7():N}",
            acceptance.OrganizerAmountMinor,
            UtcNow.AddMinutes(1));
        refund.MarkSucceeded(
            $"re_{refund.Id:N}",
            UtcNow.AddMinutes(2),
            "req_refund",
            0);
        context.RefundAttempts.Add(refund);
        await context.SaveChangesAsync();
        RegistrationFinalizationEffect effect = await context.RegistrationFinalizationEffects
            .SingleAsync(value => value.RegistrationOrderId == seed.OrderId);
        AdmissionIssuanceService service = PaidIssuanceService(context);

        AdmissionIssuanceResult result = await service.IssueConfirmedAsync(
            new AdmissionIssuanceRequest(
                seed.TenantId,
                seed.OrderId,
                effect.Id,
                AdmissionIssuanceAuthority.ReconciledPaidFinalization),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.NoAssignments);
        await Assert.That(await context.AdmissionTickets.CountAsync(
            ticket => ticket.RegistrationOrderId == seed.OrderId)).IsEqualTo(0);
        await Assert.That(await context.AdmissionTicketCredentials.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task EventCancelledBeforeDelayedPaidFinalizationCannotCreateAdmission()
    {
        SeededAssignment seed = await SeedPaidAssignmentAsync("pre-issuance-event-cancel");
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        DomainEvent eventEntity = await context.Events
            .SingleAsync(value => value.Id == seed.EventId);
        eventEntity.Cancel(UtcNow.AddMinutes(1));
        await context.SaveChangesAsync();
        RegistrationFinalizationEffect effect = await context.RegistrationFinalizationEffects
            .SingleAsync(value => value.RegistrationOrderId == seed.OrderId);
        AdmissionIssuanceService service = PaidIssuanceService(context);

        AdmissionIssuanceResult result = await service.IssueConfirmedAsync(
            new AdmissionIssuanceRequest(
                seed.TenantId,
                seed.OrderId,
                effect.Id,
                AdmissionIssuanceAuthority.ReconciledPaidFinalization),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.NotConfirmed);
        await Assert.That(await context.AdmissionTickets.CountAsync(
            ticket => ticket.RegistrationOrderId == seed.OrderId)).IsEqualTo(0);
    }

    [Test]
    public async Task EventCancellationRacingLoadedPaidIssuanceConvergesToRevokedCredential()
    {
        SeededAssignment seed = await SeedPaidAssignmentAsync("paid-event-cancel-race");
        await using ExploreDbContext issuanceContext = TenantContext(seed.TenantId);
        var gatedDigest = new GatedAdmissionDigestService(
            new AdmissionCredentialDigestService(
                new AdmissionSecretResolver(),
                Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 7 })));
        RegistrationFinalizationEffect effect =
            await issuanceContext.RegistrationFinalizationEffects
                .SingleAsync(value => value.RegistrationOrderId == seed.OrderId);
        var issuance = new AdmissionIssuanceService(
            new AdmissionIssuanceRepository(
                issuanceContext,
                ReadyParticipantAdmissionReadinessAuthority.Instance),
            gatedDigest,
            new AdmissionDeliveryEnvelopeProtector(new EphemeralDataProtectionProvider()),
            new RecordingAdmissionDispatcher(issuanceContext),
            new EfCoreUnitOfWork(issuanceContext),
            new FixedAdmissionTimeProvider(UtcNow.AddMinutes(1)));
        Task<AdmissionIssuanceResult> issuanceTask = issuance.IssueConfirmedAsync(
            new AdmissionIssuanceRequest(
                seed.TenantId,
                seed.OrderId,
                effect.Id,
                AdmissionIssuanceAuthority.ReconciledPaidFinalization),
            CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await gatedDigest.FirstGenerationEntered.WaitAsync(timeout.Token);

        PostgresException? lockFailure = await ObserveEventCancellationFenceAsync();
        gatedDigest.ReleaseFirstGeneration();
        AdmissionIssuanceResult issued = await issuanceTask.WaitAsync(timeout.Token);

        await using (ExploreDbContext cancellationContext = TenantContext(seed.TenantId))
        {
            DomainEvent eventEntity = await cancellationContext.Events
                .SingleAsync(value => value.Id == seed.EventId, timeout.Token);
            eventEntity.Cancel(UtcNow.AddMinutes(2));
            await cancellationContext.SaveChangesAsync(timeout.Token);
        }

        await using ExploreDbContext revocationContext = TenantContext(seed.TenantId);
        var repository = new AdmissionRevocationRepository(revocationContext);
        var revocation = new AdmissionRevocationService(
            repository,
            new EfCoreUnitOfWork(revocationContext),
            new FixedAdmissionTimeProvider(UtcNow.AddMinutes(3)));
        var eventCancellation = new AdmissionEventCancellationService(
            repository,
            revocation,
            new FixedAdmissionTimeProvider(UtcNow.AddMinutes(3)));
        int reconciled = await eventCancellation.ReconcileAsync(
            Guid.CreateVersion7(),
            seed.TenantId,
            seed.EventId,
            timeout.Token);

        await Assert.That(lockFailure).IsNotNull();
        await Assert.That(lockFailure!.SqlState)
            .IsEqualTo(PostgresErrorCodes.LockNotAvailable);
        await Assert.That(issued.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        await Assert.That(reconciled).IsEqualTo(1);
        AdmissionTicket ticket = await revocationContext.AdmissionTickets
            .Include(value => value.Credentials)
            .SingleAsync(value => value.RegistrationOrderId == seed.OrderId, timeout.Token);
        await Assert.That((AdmissionTicketStatusEnum)ticket.AdmissionTicketStatusId)
            .IsEqualTo(AdmissionTicketStatusEnum.Cancelled);
        await Assert.That(ticket.Credentials.Single().RevokedAt).IsNotNull();

        async Task<PostgresException?> ObserveEventCancellationFenceAsync()
        {
            await using ExploreDbContext probe = TenantContext(seed.TenantId);
            return await probe.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction =
                    await probe.Database.BeginTransactionAsync(timeout.Token);
                try
                {
                    await probe.Events
                        .FromSqlInterpolated(
                            $"SELECT * FROM events WHERE tenant_id = {seed.TenantId} AND id = {seed.EventId} FOR UPDATE NOWAIT")
                        .SingleAsync(timeout.Token);
                    return null;
                }
                catch (PostgresException exception)
                {
                    return exception;
                }
            });
        }
    }

    [Test]
    public async Task FullRefundRacingLoadedPaidIssuanceConvergesToRevokedCredential()
    {
        SeededAssignment seed = await SeedPaidAssignmentAsync("paid-refund-race");
        await using ExploreDbContext issuanceContext = TenantContext(seed.TenantId);
        var gatedDigest = new GatedAdmissionDigestService(
            new AdmissionCredentialDigestService(
                new AdmissionSecretResolver(),
                Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 7 })));
        RegistrationFinalizationEffect effect =
            await issuanceContext.RegistrationFinalizationEffects
                .SingleAsync(value => value.RegistrationOrderId == seed.OrderId);
        var issuance = new AdmissionIssuanceService(
            new AdmissionIssuanceRepository(
                issuanceContext,
                ReadyParticipantAdmissionReadinessAuthority.Instance),
            gatedDigest,
            new AdmissionDeliveryEnvelopeProtector(new EphemeralDataProtectionProvider()),
            new RecordingAdmissionDispatcher(issuanceContext),
            new EfCoreUnitOfWork(issuanceContext),
            new FixedAdmissionTimeProvider(UtcNow.AddMinutes(1)));
        Task<AdmissionIssuanceResult> issuanceTask = issuance.IssueConfirmedAsync(
            new AdmissionIssuanceRequest(
                seed.TenantId,
                seed.OrderId,
                effect.Id,
                AdmissionIssuanceAuthority.ReconciledPaidFinalization),
            CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await gatedDigest.FirstGenerationEntered.WaitAsync(timeout.Token);

        var refundInsert = new RefundInsertCommandSignal();
        Task<Guid> refundSaveTask = SaveFullRefundAsync();
        await refundInsert.CommandStarted.WaitAsync(timeout.Token);
        await Assert.That(refundSaveTask.IsCompleted).IsFalse();

        gatedDigest.ReleaseFirstGeneration();
        AdmissionIssuanceResult issued = await issuanceTask.WaitAsync(timeout.Token);
        Guid refundAttemptId = await refundSaveTask.WaitAsync(timeout.Token);

        await using ExploreDbContext revocationContext = TenantContext(seed.TenantId);
        var revocationRepository = new AdmissionRevocationRepository(revocationContext);
        var revocation = new AdmissionRevocationService(
            revocationRepository,
            new EfCoreUnitOfWork(revocationContext),
            new FixedAdmissionTimeProvider(UtcNow.AddMinutes(4)));
        var refundRevocation = new AdmissionRefundRevocationService(
            new RefundAttemptRepository(revocationContext),
            revocation);
        AdmissionRevocationResult? revoked =
            await refundRevocation.ReconcileSucceededAsync(
                seed.TenantId,
                refundAttemptId,
                timeout.Token);

        await Assert.That(issued.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        await Assert.That(revoked?.Outcome).IsEqualTo(AdmissionRevocationOutcome.Applied);
        await using ExploreDbContext verification = TenantContext(seed.TenantId);
        AdmissionTicket ticket = await verification.AdmissionTickets
            .Include(value => value.Credentials)
            .SingleAsync(value => value.RegistrationOrderId == seed.OrderId, timeout.Token);
        await Assert.That((AdmissionTicketStatusEnum)ticket.AdmissionTicketStatusId)
            .IsEqualTo(AdmissionTicketStatusEnum.Revoked);
        await Assert.That(ticket.Credentials.Single().RevokedAt).IsNotNull();

        async Task<Guid> SaveFullRefundAsync()
        {
            await using ExploreDbContext refundContext =
                TenantContext(seed.TenantId, refundInsert);
            PaymentAttempt payment = await refundContext.PaymentAttempts
                .SingleAsync(value => value.RegistrationOrderId == seed.OrderId, timeout.Token);
            PaidOrderAcceptanceSnapshot acceptance =
                await refundContext.PaidOrderAcceptanceSnapshots
                    .Include(value => value.Lines)
                    .SingleAsync(
                        value => value.Id == payment.PaidOrderAcceptanceSnapshotId,
                        timeout.Token);
            RefundAttempt refund = RefundAttempt.Create(
                Guid.CreateVersion7(),
                seed.TenantId,
                payment.Id,
                acceptance,
                payment.RecipientSnapshot.ExternalAccountId,
                payment.ProviderPaymentId!,
                $"refund:{Guid.CreateVersion7():N}",
                acceptance.OrganizerAmountMinor,
                UtcNow.AddMinutes(2));
            refund.MarkSucceeded(
                $"re_{refund.Id:N}",
                UtcNow.AddMinutes(3),
                "req_refund_race",
                0);
            refundContext.RefundAttempts.Add(refund);
            await refundContext.SaveChangesAsync(timeout.Token);
            return refund.Id;
        }
    }

    [Test]
    public async Task ConcurrentPublicServiceCallsFenceBeforeCredentialGenerationAndConverge()
    {
        await fixture.ResetAsync();
        SeededAssignment seed = await SeedAssignmentAsync("public-service-race");
        RegistrationFinalizationEffect effect = RegistrationFinalizationEffect.Create(seed.Order, UtcNow);
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            setup.RegistrationFinalizationEffects.Add(effect);
            await setup.SaveChangesAsync();
        }

        var secondLock = new FinalizationLockCommandSignal();
        await using ExploreDbContext firstContext = TenantContext(seed.TenantId);
        await using ExploreDbContext secondContext = TenantContext(seed.TenantId, secondLock);
        var gatedDigest = new GatedAdmissionDigestService(
            new AdmissionCredentialDigestService(
                new AdmissionSecretResolver(),
                Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 7 })));
        var dataProtection = new EphemeralDataProtectionProvider();
        var request = new AdmissionIssuanceRequest(seed.TenantId, seed.OrderId, effect.Id, "ConfirmedFreeOrder");
        AdmissionIssuanceService firstService = CreateService(firstContext);
        AdmissionIssuanceService secondService = CreateService(secondContext);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Task<AdmissionIssuanceResult> first = firstService.IssueConfirmedAsync(request, timeout.Token);
        await gatedDigest.FirstGenerationEntered.WaitAsync(timeout.Token);
        Task<AdmissionIssuanceResult> second = secondService.IssueConfirmedAsync(request, timeout.Token);
        await secondLock.CommandStarted.WaitAsync(timeout.Token);
        gatedDigest.ReleaseFirstGeneration();
        AdmissionIssuanceResult[] outcomes = await Task.WhenAll(first, second);

        await Assert.That(gatedDigest.GenerationCount).IsEqualTo(1);
        await Assert.That(outcomes.Count(result => result.Outcome == AdmissionIssuanceOutcome.Issued)).IsEqualTo(1);
        await Assert.That(outcomes.Count(result => result.Outcome == AdmissionIssuanceOutcome.AlreadyIssued)).IsEqualTo(1);
        Guid[] winnerIds = outcomes.Single(result => result.Outcome == AdmissionIssuanceOutcome.Issued).IssuedTicketIds.ToArray();
        await Assert.That(outcomes.Single(result => result.Outcome == AdmissionIssuanceOutcome.AlreadyIssued).ExistingTicketIds)
            .IsEquivalentTo(winnerIds);
        await Assert.That(outcomes.Sum(result => result.OneTimeCredentials.Count)).IsEqualTo(1);
        await using ExploreDbContext verification = TenantContext(seed.TenantId);
        await Assert.That(await verification.AdmissionTickets.CountAsync()).IsEqualTo(1);
        await Assert.That(await verification.AdmissionTicketCredentials.CountAsync()).IsEqualTo(1);
        await Assert.That(await verification.AdmissionDeliveryIntents.CountAsync()).IsEqualTo(1);

        AdmissionIssuanceService CreateService(ExploreDbContext context) => new(
            new AdmissionIssuanceRepository(
                context,
                ReadyParticipantAdmissionReadinessAuthority.Instance),
            gatedDigest,
            new AdmissionDeliveryEnvelopeProtector(dataProtection),
            new RecordingAdmissionDispatcher(context),
            new EfCoreUnitOfWork(context),
            new FixedAdmissionTimeProvider(UtcNow));
    }

    [Test]
    public async Task SameTenantSameAssignmentDistinctDigestsHaveOneTicketWinnerAtMappedTicketInsert()
    {
        AdmissionPersistenceSurface surface = AdmissionPersistenceSurface.RequirePublicSurface();
        await fixture.ResetAsync();
        SeededAssignment seed = await SeedAssignmentAsync("assignment-collision");
        TicketGraph firstGraph = surface.IssueTicketGraph(
            seed, Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 23, Digest(23));
        TicketGraph secondGraph = surface.IssueTicketGraph(
            seed, Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 23, Digest(24));

        await AssertPostgreSqlCollisionAsync(
            seed.TenantId, firstGraph, secondGraph, CollisionBarrierTarget.Ticket);
    }

    [Test]
    public async Task ConcurrentRecoveryConsumeHasExactlyOneWinner()
    {
        await fixture.ResetAsync();
        (Guid tenantId, AdmissionRecoveryCapability state) =
            await SeedRecoveryCapabilityAsync("recovery-consume");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<bool>[] attempts =
        [
            ConsumeAsync(),
            ConsumeAsync()
        ];
        start.SetResult();
        bool[] results = await Task.WhenAll(attempts);

        await Assert.That(results.Count(result => result)).IsEqualTo(1);
        await Assert.That(results.Count(result => !result)).IsEqualTo(1);

        async Task<bool> ConsumeAsync()
        {
            await start.Task.WaitAsync(timeout.Token);
            await using ExploreDbContext context = TenantContext(tenantId);
            var repository = new AdmissionRecoveryRepository(context);
            return await repository.TryConsumeAsync(
                state.TenantId,
                state.Id,
                state.LookupKeyVersion,
                state.LookupDigest,
                state.ConcurrencyStamp,
                UtcNow,
                timeout.Token);
        }
    }

    [Test]
    public async Task TicketWideRecoveryUniquenessRejectsSecondRequestLineage()
    {
        await fixture.ResetAsync();
        (Guid tenantId, AdmissionRecoveryCapability state) =
            await SeedRecoveryCapabilityAsync("recovery-lineage");
        await using ExploreDbContext context = TenantContext(tenantId);
        var repository = new AdmissionRecoveryRepository(context);

        Exception collision = await Assert.ThrowsAsync<Exception>(async () =>
            await repository.AddAsync(
                AdmissionRecoveryCapability.Create(
                    Guid.CreateVersion7(),
                    tenantId,
                    Guid.CreateVersion7(),
                    state.AdmissionTicketId,
                    AdmissionRecoveryPurpose.TicketRecovery.ToString(),
                    1,
                    1,
                    Digest(0x51),
                    UtcNow.AddMinutes(15),
                    UtcNow,
                    Digest(0x52)),
                CancellationToken.None));

        await Assert.That(collision.GetBaseException()).IsTypeOf<PostgresException>();
        await Assert.That(((PostgresException)collision.GetBaseException()).SqlState)
            .IsEqualTo(PostgresErrorCodes.UniqueViolation);
    }

    [Test]
    public async Task ConcurrentRecoveryRotationHasExactlyOneReplacementGeneration()
    {
        await fixture.ResetAsync();
        (Guid tenantId, AdmissionRecoveryCapability state) =
            await SeedRecoveryCapabilityAsync("recovery-rotate");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AdmissionRecoveryCapability first = Rotation(0x21);
        AdmissionRecoveryCapability second = Rotation(0x31);

        Task<bool>[] attempts =
        [
            RotateAsync(first),
            RotateAsync(second)
        ];
        start.SetResult();
        bool[] results = await Task.WhenAll(attempts);

        await Assert.That(results.Count(result => result)).IsEqualTo(1);
        await Assert.That(results.Count(result => !result)).IsEqualTo(1);
        await using ExploreDbContext verification = TenantContext(tenantId);
        AdmissionRecoveryCapability[] rows = await verification.AdmissionRecoveryCapabilities
            .AsNoTracking()
            .OrderBy(value => value.CapabilityVersion)
            .ToArrayAsync(timeout.Token);
        await Assert.That(rows.Length).IsEqualTo(2);
        await Assert.That(rows.Count(value =>
            value.ConsumedAt is null && value.RotatedAt is null)).IsEqualTo(1);
        await Assert.That(rows.Single(value =>
            value.ConsumedAt is null && value.RotatedAt is null).CapabilityVersion).IsEqualTo(2);

        AdmissionRecoveryCapability Rotation(byte fill) =>
            AdmissionRecoveryCapability.Create(
            Guid.CreateVersion7(),
            state.TenantId,
            state.RecoveryRequestId,
            state.AdmissionTicketId,
            state.Purpose,
            state.CapabilityVersion + 1,
            1,
            Digest(fill),
            UtcNow.AddMinutes(15),
            UtcNow,
            Digest((byte)(fill + 1)));

        async Task<bool> RotateAsync(AdmissionRecoveryCapability replacement)
        {
            await start.Task.WaitAsync(timeout.Token);
            await using ExploreDbContext context = TenantContext(tenantId);
            var repository = new AdmissionRecoveryRepository(context);
            return await repository.TryRotateAsync(
                state,
                replacement,
                UtcNow,
                timeout.Token);
        }
    }

    [Test]
    public async Task RecoveryDeliveryStagesOnlyCiphertextAndIdentifierOutboxPointer()
    {
        await fixture.ResetAsync();
        AdmissionPersistenceSurface surface = AdmissionPersistenceSurface.RequirePublicSurface();
        SeededAssignment seed = await SeedAssignmentAsync("recovery-delivery");
        TicketGraph graph = surface.IssueTicketGraph(
            seed,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            1,
            Digest(0x41));
        await PersistTicketAsync(seed.TenantId, graph, CancellationToken.None);
        const string recipient = "RECOVERY-DELIVERY@EXAMPLE.TEST";
        const string capability = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        Guid requestId = Guid.CreateVersion7();
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        RegistrationOrderPii pii = RegistrationOrderPii.CreateFromVerifiedContact(
            seed.OrderId,
            seed.TenantId,
            "Recovery Delivery",
            recipient,
            null,
            null,
            recipient,
            (int)RegistrationRetentionPolicyEnum.StandardOperational,
            UtcNow);
        context.RegistrationOrderPii.Add(pii);
        await context.SaveChangesAsync();
        var protector = new AdmissionRecoveryDeliveryEnvelopeProtector(
            new EphemeralDataProtectionProvider());
        var service = new AdmissionRecoveryProtectedDeliveryService(
            context,
            protector,
            new FixedAdmissionTimeProvider(UtcNow));

        AdmissionRecoveryDeliveryResult result = await service.StageAsync(
            new AdmissionRecoveryDeliveryRequest(
                seed.TenantId,
                requestId,
                graph.TicketId,
                AdmissionRecoveryPurpose.TicketRecovery,
                capability),
            CancellationToken.None);
        context.ChangeTracker.Clear();
        AdmissionRecoveryDeliveryIntent intent =
            await context.AdmissionRecoveryDeliveryIntents.SingleAsync();
        OutboxMessage outbox = await context.OutboxMessages.SingleAsync(message =>
            message.Id == intent.Id);
        AdmissionRecoveryDeliveryEnvelope restored = protector.Unprotect(
            intent.ProtectedMaterial,
            intent.ProtectionVersion);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionRecoveryDeliveryOutcome.Accepted);
        await Assert.That(intent.ProtectedMaterial).DoesNotContain(recipient);
        await Assert.That(intent.ProtectedMaterial).DoesNotContain(capability);
        await Assert.That(outbox.Payload).DoesNotContain(recipient);
        await Assert.That(outbox.Payload).DoesNotContain(capability);
        await Assert.That(outbox.EventType)
            .IsEqualTo(AdmissionRecoveryDeliveryEvents.RecoveryDeliveryRequested);
        await Assert.That(restored).IsEqualTo(
            new AdmissionRecoveryDeliveryEnvelope(recipient, requestId, capability));
    }

    [Test]
    public async Task RecoveryRequestStagingIsPresenceAgnosticAndPlaintextFree()
    {
        await fixture.ResetAsync();
        SeededAssignment seed = await SeedAssignmentAsync("recovery-request-staging");
        const string presentIdentity = "PRESENT1@EXAMPLE.TEST";
        const string absentIdentity = "ABSENT01@EXAMPLE.TEST";
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        var protector = new AdmissionRecoveryRequestEnvelopeProtector(
            new EphemeralDataProtectionProvider());
        var stager = new AdmissionRecoveryRequestStager(
            context,
            protector,
            new FixedAdmissionTimeProvider(UtcNow));

        await stager.StageAsync(
            seed.TenantId,
            new AdmissionRecoveryRequestEnvelope(
                presentIdentity,
                AdmissionRecoveryPurpose.TicketRecovery),
            CancellationToken.None);
        await stager.StageAsync(
            seed.TenantId,
            new AdmissionRecoveryRequestEnvelope(
                absentIdentity,
                AdmissionRecoveryPurpose.TicketRecovery),
            CancellationToken.None);
        context.ChangeTracker.Clear();
        AdmissionRecoveryRequestIntent[] intents =
            await context.AdmissionRecoveryRequestIntents
                .OrderBy(value => value.Id)
                .ToArrayAsync();
        OutboxMessage[] outbox = await context.OutboxMessages
            .Where(message =>
                message.EventType ==
                AdmissionRecoveryDeliveryEvents.RecoveryRequestProcessingRequested)
            .OrderBy(message => message.Id)
            .ToArrayAsync();

        await Assert.That(intents.Length).IsEqualTo(2);
        await Assert.That(outbox.Length).IsEqualTo(2);
        await Assert.That(intents.All(intent =>
            !intent.ProtectedIdentity.Contains(presentIdentity, StringComparison.Ordinal) &&
            !intent.ProtectedIdentity.Contains(absentIdentity, StringComparison.Ordinal))).IsTrue();
        await Assert.That(outbox.All(message =>
            !message.Payload!.Contains(presentIdentity, StringComparison.Ordinal) &&
            !message.Payload.Contains(absentIdentity, StringComparison.Ordinal))).IsTrue();
        await Assert.That(intents.Select(intent => intent.ProtectedIdentity.Length).Distinct().Count())
            .IsEqualTo(1);
    }

    [Test]
    public async Task RecoveryRequestOutboxHandlerClearsProtectedAbsentIdentityAfterProcessing()
    {
        await fixture.ResetAsync();
        SeededAssignment seed = await SeedAssignmentAsync("recovery-request-handler");
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        var protector = new AdmissionRecoveryRequestEnvelopeProtector(
            new EphemeralDataProtectionProvider());
        var requestStager = new AdmissionRecoveryRequestStager(
            context,
            protector,
            new FixedAdmissionTimeProvider(UtcNow));
        await requestStager.StageAsync(
            seed.TenantId,
            new AdmissionRecoveryRequestEnvelope(
                "ABSENT@EXAMPLE.TEST",
                AdmissionRecoveryPurpose.TicketRecovery),
            CancellationToken.None);
        OutboxMessage outbox = await context.OutboxMessages.SingleAsync(message =>
            message.EventType ==
            AdmissionRecoveryDeliveryEvents.RecoveryRequestProcessingRequested);
        IAdmissionRecoveryCapabilityService capabilityService =
            Substitute.For<IAdmissionRecoveryCapabilityService>();
        var unitOfWork = new EfCoreUnitOfWork(context);
        var service = new AdmissionRecoveryService(
            new AdmissionRecoveryRepository(context),
            new AdmissionRecoveryIdentityResolver(context),
            capabilityService,
            unitOfWork,
            new FixedAdmissionTimeProvider(UtcNow),
            Substitute.For<IAdmissionRecoveryDeliveryStager>(),
            Substitute.For<IAdmissionRecoveryAuditService>(),
            Substitute.For<IAdmissionRecoveryRateLimiter>(),
            requestStager,
            NullLogger<AdmissionRecoveryService>.Instance);
        var handler = new AdmissionRecoveryRequestOutboxHandler(
            context,
            protector,
            service,
            unitOfWork,
            new FixedAdmissionTimeProvider(UtcNow.AddSeconds(1)));

        await handler.HandleAsync(outbox, CancellationToken.None);
        context.ChangeTracker.Clear();
        AdmissionRecoveryRequestIntent completed =
            await context.AdmissionRecoveryRequestIntents.SingleAsync();

        await Assert.That(completed.ProcessedAt).IsEqualTo(UtcNow.AddSeconds(1));
        await Assert.That(completed.ProtectedIdentity).IsEmpty();
        await capabilityService.DidNotReceiveWithAnyArgs()
            .IssueAsync(default!, default);
    }

    [Test]
    public async Task RecoveryRequestProcessingRollsBackEffectsUntilIntentCanComplete()
    {
        await fixture.ResetAsync();
        AdmissionPersistenceSurface surface = AdmissionPersistenceSurface.RequirePublicSurface();
        SeededAssignment seed = await SeedAssignmentAsync("recovery-request-atomic");
        TicketGraph graph = surface.IssueTicketGraph(
            seed,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            1,
            Digest(0x81));
        await PersistTicketAsync(seed.TenantId, graph, CancellationToken.None);
        const string recipient = "RECOVERY-ATOMIC@EXAMPLE.TEST";
        const string capability = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        context.RegistrationOrderPii.Add(RegistrationOrderPii.CreateFromVerifiedContact(
            seed.OrderId,
            seed.TenantId,
            "Recovery Atomic",
            recipient,
            null,
            null,
            recipient,
            (int)RegistrationRetentionPolicyEnum.StandardOperational,
            UtcNow));
        await context.SaveChangesAsync();
        var requestProtector = new AdmissionRecoveryRequestEnvelopeProtector(
            new EphemeralDataProtectionProvider());
        var requestStager = new AdmissionRecoveryRequestStager(
            context,
            requestProtector,
            new FixedAdmissionTimeProvider(UtcNow));
        await requestStager.StageAsync(
            seed.TenantId,
            new AdmissionRecoveryRequestEnvelope(
                recipient,
                AdmissionRecoveryPurpose.TicketRecovery),
            CancellationToken.None);
        OutboxMessage outbox = await context.OutboxMessages.SingleAsync(message =>
            message.EventType ==
            AdmissionRecoveryDeliveryEvents.RecoveryRequestProcessingRequested);
        IAdmissionRecoveryCapabilityService capabilityService =
            Substitute.For<IAdmissionRecoveryCapabilityService>();
        capabilityService.IssueAsync(
                Arg.Any<AdmissionRecoveryCapabilityIssueRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdmissionRecoveryCapabilityMaterial(
                capability,
                Digest(0x82),
                1,
                AdmissionRecoveryPurpose.TicketRecovery,
                UtcNow.AddMinutes(15),
                Digest(0x83)));
        var unitOfWork = new EfCoreUnitOfWork(context);
        var deliveryStager = new AdmissionRecoveryProtectedDeliveryService(
            context,
            new AdmissionRecoveryDeliveryEnvelopeProtector(
                new EphemeralDataProtectionProvider()),
            new FixedAdmissionTimeProvider(UtcNow));
        var service = new AdmissionRecoveryService(
            new AdmissionRecoveryRepository(context),
            new AdmissionRecoveryIdentityResolver(context),
            capabilityService,
            unitOfWork,
            new FixedAdmissionTimeProvider(UtcNow),
            deliveryStager,
            Substitute.For<IAdmissionRecoveryAuditService>(),
            Substitute.For<IAdmissionRecoveryRateLimiter>(),
            requestStager,
            NullLogger<AdmissionRecoveryService>.Instance);
        var failingHandler = new AdmissionRecoveryRequestOutboxHandler(
            context,
            requestProtector,
            service,
            unitOfWork,
            new FixedAdmissionTimeProvider(UtcNow.AddMinutes(-1)));

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            failingHandler.HandleAsync(outbox, CancellationToken.None));

        context.ChangeTracker.Clear();
        await Assert.That(await context.AdmissionRecoveryCapabilities.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AdmissionRecoveryDeliveryIntents.CountAsync()).IsEqualTo(0);
        AdmissionRecoveryRequestIntent pending =
            await context.AdmissionRecoveryRequestIntents.SingleAsync();
        await Assert.That(pending.ProcessedAt).IsNull();
        await Assert.That(pending.ProtectedIdentity).IsNotEmpty();

        var completingHandler = new AdmissionRecoveryRequestOutboxHandler(
            context,
            requestProtector,
            service,
            unitOfWork,
            new FixedAdmissionTimeProvider(UtcNow.AddSeconds(1)));
        await completingHandler.HandleAsync(outbox, CancellationToken.None);
        await completingHandler.HandleAsync(outbox, CancellationToken.None);

        context.ChangeTracker.Clear();
        await Assert.That(await context.AdmissionRecoveryCapabilities.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.AdmissionRecoveryDeliveryIntents.CountAsync()).IsEqualTo(1);
        AdmissionRecoveryRequestIntent completed =
            await context.AdmissionRecoveryRequestIntents.SingleAsync();
        await Assert.That(completed.ProcessedAt).IsEqualTo(UtcNow.AddSeconds(1));
        await Assert.That(completed.ProtectedIdentity).IsEmpty();
    }

    [Test]
    public async Task RecoveryDeliveryRejectsStaleLifecycleWriter()
    {
        await fixture.ResetAsync();
        AdmissionPersistenceSurface surface = AdmissionPersistenceSurface.RequirePublicSurface();
        SeededAssignment seed = await SeedAssignmentAsync("recovery-delivery-concurrency");
        TicketGraph graph = surface.IssueTicketGraph(
            seed,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            1,
            Digest(0x71));
        await PersistTicketAsync(seed.TenantId, graph, CancellationToken.None);
        Guid intentId = Guid.CreateVersion7();
        await using (ExploreDbContext seedContext = TenantContext(seed.TenantId))
        {
            seedContext.AdmissionRecoveryDeliveryIntents.Add(
                new AdmissionRecoveryDeliveryIntent(
                    intentId,
                    seed.TenantId,
                    Guid.CreateVersion7(),
                    graph.TicketId,
                    AdmissionRecoveryPurpose.TicketRecovery.ToString(),
                    1,
                    "protected-test-material",
                    1,
                    UtcNow));
            await seedContext.SaveChangesAsync();
        }

        await using ExploreDbContext firstContext = TenantContext(seed.TenantId);
        await using ExploreDbContext staleContext = TenantContext(seed.TenantId);
        AdmissionRecoveryDeliveryIntent first =
            await firstContext.AdmissionRecoveryDeliveryIntents.SingleAsync(value =>
                value.Id == intentId);
        AdmissionRecoveryDeliveryIntent stale =
            await staleContext.AdmissionRecoveryDeliveryIntents.SingleAsync(value =>
                value.Id == intentId);
        Guid initialStamp = first.ConcurrencyStamp;

        first.MarkRouted(UtcNow.AddSeconds(1));
        await firstContext.SaveChangesAsync();
        stale.MarkRouted(UtcNow.AddSeconds(2));
        Exception collision = await Assert.ThrowsAsync<Exception>(
            () => staleContext.SaveChangesAsync());

        await Assert.That(first.ConcurrencyStamp).IsNotEqualTo(initialStamp);
        await Assert.That(collision).IsTypeOf<DbUpdateConcurrencyException>();
    }

    [Test]
    public async Task AccountTicketListUsesOrderAccountAuthorityAndExcludesRevoked()
    {
        await fixture.ResetAsync();
        AdmissionPersistenceSurface surface = AdmissionPersistenceSurface.RequirePublicSurface();
        SeededAssignment seed = await SeedAssignmentAsync("account-authority");
        TicketGraph graph = surface.IssueTicketGraph(
            seed,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            1,
            Digest(0x61));
        await PersistTicketAsync(seed.TenantId, graph, CancellationToken.None);
        Guid accountUserId = seed.Order.AccountUserId
            ?? throw new InvalidOperationException("Seeded account order has no account authority.");
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        context.RegistrationParticipantPii.Add(RegistrationParticipantPii.Create(
            seed.ParticipantId,
            seed.TenantId,
            "Account ticket holder",
            "holder@example.test",
            null));
        await context.SaveChangesAsync();
        var repository = new AdmissionTicketAccountRepository(context);
        var presentationResolver = new AdmissionTicketPresentationResolver(context);

        IReadOnlyList<AdmissionTicket> authorized = await repository.ListCurrentAsync(
            seed.TenantId,
            accountUserId,
            CancellationToken.None);
        IReadOnlyList<AdmissionTicket> wrongAccount = await repository.ListCurrentAsync(
            seed.TenantId,
            Guid.CreateVersion7(),
            CancellationToken.None);
        IReadOnlyList<AdmissionTicket> wrongTenant = await repository.ListCurrentAsync(
            Guid.CreateVersion7(),
            accountUserId,
            CancellationToken.None);
        var presentation = await presentationResolver.ResolveAsync(
            seed.TenantId,
            [graph.TicketId],
            CancellationToken.None);
        await context.AdmissionTickets
            .Where(ticket => ticket.Id == graph.TicketId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                ticket => ticket.AdmissionTicketStatusId,
                (int)AdmissionTicketStatusEnum.Revoked));
        IReadOnlyList<AdmissionTicket> afterRevocation = await repository.ListCurrentAsync(
            seed.TenantId,
            accountUserId,
            CancellationToken.None);

        await Assert.That(authorized.Select(ticket => ticket.Id)).IsEquivalentTo([graph.TicketId]);
        await Assert.That(wrongAccount).IsEmpty();
        await Assert.That(wrongTenant).IsEmpty();
        await Assert.That(afterRevocation).IsEmpty();
        await Assert.That(presentation[graph.TicketId].HolderDisplayName)
            .IsEqualTo("Account ticket holder");
        await Assert.That(presentation[graph.TicketId].TicketTypeName)
            .IsEqualTo("Admission");
        await Assert.That(presentation[graph.TicketId].Entitlements.Count).IsEqualTo(1);
        await Assert.That(presentation[graph.TicketId].Entitlements[0].EventTitle)
            .StartsWith("Admission account-authority");
    }

    [Test]
    public async Task SameTenantDistinctAssignmentsSameDigestHaveOneCredentialWinnerAtMappedCredentialInsert()
    {
        AdmissionPersistenceSurface surface = AdmissionPersistenceSurface.RequirePublicSurface();
        await fixture.ResetAsync();
        SeededAssignment[] assignments = await SeedAssignmentsAsync("credential-collision", assignmentCount: 2);
        const int keyVersion = 29;
        string sharedDigest = Digest(29);
        TicketGraph firstGraph = surface.IssueTicketGraph(
            assignments[0], Guid.CreateVersion7(), Guid.CreateVersion7(), 1, keyVersion, sharedDigest);
        TicketGraph secondGraph = surface.IssueTicketGraph(
            assignments[1], Guid.CreateVersion7(), Guid.CreateVersion7(), 1, keyVersion, sharedDigest);

        await Assert.That(assignments[0].TenantId).IsEqualTo(assignments[1].TenantId);
        await Assert.That(assignments[0].AssignmentId).IsNotEqualTo(assignments[1].AssignmentId);
        await AssertPostgreSqlCollisionAsync(
            assignments[0].TenantId, firstGraph, secondGraph, CollisionBarrierTarget.Credential);
    }

    private async Task AssertPostgreSqlCollisionAsync(
        Guid tenantId,
        TicketGraph firstGraph,
        TicketGraph secondGraph,
        CollisionBarrierTarget barrierTarget)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using ExploreDbContext metadataContext = fixture.CreateDbContext();
        (IEntityType ticketModel, IEntityType credentialModel) =
            AdmissionPersistenceSurface.RequireAdmissionEntities(metadataContext.Model);
        string ticketTableIdentifier =
            AdmissionPersistenceSurface.DelimitedTableIdentifier(metadataContext, ticketModel);
        string credentialTableIdentifier =
            AdmissionPersistenceSurface.DelimitedTableIdentifier(metadataContext, credentialModel);
        string barrierTableIdentifier = barrierTarget == CollisionBarrierTarget.Ticket
            ? ticketTableIdentifier
            : credentialTableIdentifier;
        var barrier = new AdmissionInsertBarrier();
        Guid firstWriter = Guid.CreateVersion7();
        Guid secondWriter = Guid.CreateVersion7();

        Task<Exception?> first = PersistCompetingTicketAsync(firstWriter, firstGraph);
        Task<Exception?> second = PersistCompetingTicketAsync(secondWriter, secondGraph);
        await barrier.AllArrived.WaitAsync(timeout.Token);
        await Assert.That(barrier.Arrivals).IsEqualTo(2);
        await Assert.That(barrier.HasArrived(firstWriter)).IsTrue();
        await Assert.That(barrier.HasArrived(secondWriter)).IsTrue();
        barrier.Release();
        Exception?[] outcomes = await Task.WhenAll(first, second);

        await Assert.That(outcomes.Count(outcome => outcome is null)).IsEqualTo(1);
        await Assert.That(outcomes.Count(outcome => outcome is not null)).IsEqualTo(1);
        Exception collision = outcomes.Single(outcome => outcome is not null)!;
        await Assert.That(collision.GetBaseException()).IsTypeOf<PostgresException>();
        await Assert.That(((PostgresException)collision.GetBaseException()).SqlState)
            .IsEqualTo(PostgresErrorCodes.UniqueViolation);
        await using ExploreDbContext verification = TenantContext(tenantId);
        await Assert.That(await verification.AdmissionTickets.CountAsync(timeout.Token)).IsEqualTo(1);
        await Assert.That(await verification.AdmissionTicketCredentials.CountAsync(timeout.Token)).IsEqualTo(1);

        async Task<Exception?> PersistCompetingTicketAsync(Guid writerId, TicketGraph graph)
        {
            var interceptor = new AdmissionInsertBarrierInterceptor(barrier, writerId, barrierTableIdentifier);
            await using ExploreDbContext context = TenantContext(tenantId, interceptor);
            var repository = new AdmissionTicketRepository(context);
            try
            {
                await repository.AddAsync(graph.Ticket, timeout.Token);
                await repository.SaveChangesAsync(timeout.Token);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }

    private static string Digest(byte fill) =>
        Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray());

    private async Task<(Guid TenantId, AdmissionRecoveryCapability State)>
        SeedRecoveryCapabilityAsync(string suffix)
    {
        AdmissionPersistenceSurface surface = AdmissionPersistenceSurface.RequirePublicSurface();
        SeededAssignment seed = await SeedAssignmentAsync(suffix);
        TicketGraph graph = surface.IssueTicketGraph(
            seed,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            1,
            Digest(0x11));
        await PersistTicketAsync(seed.TenantId, graph, CancellationToken.None);
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        var repository = new AdmissionRecoveryRepository(context);
        Guid requestId = Guid.CreateVersion7();
        string lookupDigest = Digest(0x12);
        AdmissionRecoveryCapability capability = AdmissionRecoveryCapability.Create(
                Guid.CreateVersion7(),
                seed.TenantId,
                requestId,
                graph.TicketId,
                AdmissionRecoveryPurpose.TicketRecovery.ToString(),
                1,
                1,
                lookupDigest,
                UtcNow.AddMinutes(15),
                UtcNow,
                Digest(0x13));
        await repository.AddAsync(capability, CancellationToken.None);
        AdmissionRecoveryCapability? state = await repository.FindByProofDigestAsync(
                seed.TenantId,
                requestId,
                graph.TicketId,
                AdmissionRecoveryPurpose.TicketRecovery,
                1,
                lookupDigest,
            CancellationToken.None);
        return (seed.TenantId, state!);
    }

    private async Task PersistTicketAsync(Guid tenantId, TicketGraph graph, CancellationToken cancellationToken)
    {
        await using ExploreDbContext context = TenantContext(tenantId);
        var repository = new AdmissionTicketRepository(context);
        await repository.AddAsync(graph.Ticket, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private ExploreDbContext TenantContext(Guid tenantId, params IInterceptor[] interceptors)
    {
        ExploreDbContext context = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        if (interceptors.Length == 0)
            return context;

        context.Dispose();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptors)
            .Options;
        return new ExploreDbContext(options) { TenantContext = new TestTenantContext(tenantId) };
    }

    private async Task IssueSeedAsync(SeededAssignment seed)
    {
        await using ExploreDbContext context = TenantContext(seed.TenantId);
        RegistrationFinalizationEffect effect = RegistrationFinalizationEffect.Create(seed.Order, UtcNow);
        context.RegistrationFinalizationEffects.Add(effect);
        await context.SaveChangesAsync();
        var service = new AdmissionIssuanceService(
            new AdmissionIssuanceRepository(
                context,
                ReadyParticipantAdmissionReadinessAuthority.Instance),
            new AdmissionCredentialDigestService(
                new AdmissionSecretResolver(),
                Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 7 })),
            new AdmissionDeliveryEnvelopeProtector(new EphemeralDataProtectionProvider()),
            new RecordingAdmissionDispatcher(context),
            new EfCoreUnitOfWork(context),
            new FixedAdmissionTimeProvider(UtcNow));
        AdmissionIssuanceResult result = await service.IssueConfirmedAsync(
            new AdmissionIssuanceRequest(
                seed.TenantId,
                seed.OrderId,
                effect.Id,
                AdmissionIssuanceAuthority.ConfirmedFreeOrder),
            CancellationToken.None);
        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
    }

    private AdmissionIssuanceService PaidIssuanceService(ExploreDbContext context) =>
        new(
            new AdmissionIssuanceRepository(
                context,
                ReadyParticipantAdmissionReadinessAuthority.Instance),
            new AdmissionCredentialDigestService(
                new AdmissionSecretResolver(),
                Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 7 })),
            new AdmissionDeliveryEnvelopeProtector(new EphemeralDataProtectionProvider()),
            new RecordingAdmissionDispatcher(context),
            new EfCoreUnitOfWork(context),
            new FixedAdmissionTimeProvider(UtcNow.AddMinutes(3)));

    private async Task<SeededAssignment> SeedAssignmentAsync(string suffix) =>
        (await SeedAssignmentsAsync(suffix, assignmentCount: 1)).Single();

    private async Task<SeededAssignment> SeedPaidAssignmentAsync(string suffix) =>
        (await SeedAssignmentsAsync(suffix, assignmentCount: 1, paid: true)).Single();

    private async Task<SeededAssignment[]> SeedAssignmentsAsync(
        string suffix,
        int assignmentCount,
        bool paid = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assignmentCount);

        await using ExploreDbContext context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            FullName = $"Admission {suffix}",
            Slug = $"admission-{suffix}-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"admission-{suffix}-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Admission",
                LastName = suffix
            }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = $"Admission {suffix}" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        var eventEntity = new DomainEvent(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = $"Admission {suffix}",
            Subtitle = "",
            Description = "",
            FirstSessionDate = DateOnly.FromDateTime(UtcNow.AddDays(1)),
            LastSessionDate = DateOnly.FromDateTime(UtcNow.AddDays(1)),
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            TotalViews = 0
        };
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenant.Id, eventEntity.Id, "EUR", 1);
        EventTicketType ticketType = EventTicketType.Create(
            Guid.CreateVersion7(), tenant.Id, catalog.Id, "Admission", "EUR",
            paid ? TicketPricingModeEnum.Fixed : TicketPricingModeEnum.Free,
            paid ? Money.Create(500, "EUR") : null,
            null, null, ParticipantDataCollectionModeEnum.None, null, null, null,
            false, false, null, null, null, null);
        catalog.AddTicketType(ticketType, null);
        catalog.AddEntitlement(ticketType, TicketTypeEntitlement.CreateForEvent(ticketType.Id, tenant.Id, eventEntity.Id, 1));
        if (paid)
        {
            catalog.UpdateCommercialDisclosures(
                "Merchant disclosure",
                "Refund policy",
                "support@example.test");
        }
        catalog.Publish();
        RegistrationOrder order = RegistrationOrder.Create(
            tenant.Id, eventEntity.Id, user.Id, actor.Id, BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
            null, null, "EUR", UtcNow, null);
        RegistrationOrderLine line = RegistrationOrderLine.Create(
            catalog, ticketType, order.Id, assignmentCount, null, null);
        order.AddLine(line);
        RegistrationParticipant[] participants = Enumerable.Range(0, assignmentCount)
            .Select(_ => RegistrationParticipant.Create(
                tenant.Id, order.Id, user.Id, ParticipantTypeEnum.Adult, guardian: null))
            .ToArray();
        RegistrationTicketAssignment[] assignments = participants
            .Select((participant, index) => RegistrationTicketAssignment.CreateAssigned(
                Guid.CreateVersion7(), line.Id, index + 1, participant, UtcNow))
            .ToArray();
        for (int index = 0; index < participants.Length; index++)
        {
            order.AddParticipant(participants[index]);
            order.AddAssignment(line, assignments[index], participants[index]);
        }
        long organizerMinor = paid ? checked(assignmentCount * 500L) : 0;
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create(
            "EUR", organizerMinor, 0, organizerMinor, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, UtcNow);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, UtcNow);
        if (paid)
        {
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingPayment, UtcNow);
            order.TransitionTo(RegistrationOrderStatusEnum.NeedsReconciliation, UtcNow);
        }
        order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, UtcNow);
        context.AddRange(eventEntity, catalog, order);
        if (paid)
        {
            Guid organizerPaymentProviderConnectionId = Guid.CreateVersion7();
            PaidOrderAcceptanceSnapshot acceptance = CreatePaidAcceptance(
                tenant.Id,
                eventEntity.Id,
                order.Id,
                line.Id,
                assignmentCount,
                organizerMinor,
                organizerPaymentProviderConnectionId);
            OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
                tenant.Id,
                actor.Id,
                organizerPaymentProviderConnectionId,
                "stripe",
                "platform-test",
                "acct_admission_test",
                "BE",
                "EUR",
                Guid.CreateVersion7(),
                null,
                UtcNow);
            PaymentAttempt payment = PaymentAttempt.Create(
                Guid.CreateVersion7(),
                tenant.Id,
                order.Id,
                recipient,
                "OrganizerDirect",
                "2026-08-20.acacia",
                "phase20-paid-admission",
                Money.Create(organizerMinor, "EUR"),
                Money.Create(0, "EUR"),
                Money.Create(0, "EUR"),
                $"payment:{tenant.Id:N}:{order.Id:N}",
                UtcNow,
                UtcNow.AddMinutes(30));
            payment.AttachAcceptance(acceptance);
            if (!payment.MarkSucceededFromCheckout(
                    $"cs_{order.Id:N}",
                    $"pi_{order.Id:N}",
                    UtcNow.AddSeconds(1),
                    "req_phase20"))
            {
                throw new InvalidOperationException("Paid admission test payment did not reach succeeded.");
            }
            context.AddRange(
                payment,
                PaymentSucceededObservation.Create(
                    payment,
                    null,
                    $"cs_{order.Id:N}",
                    $"pi_{order.Id:N}",
                    "req_phase20",
                    UtcNow.AddSeconds(1)),
                RegistrationFinalizationEffect.Create(order, UtcNow.AddSeconds(2)));
        }
        await context.SaveChangesAsync();
        return assignments.Select((assignment, index) => new SeededAssignment(
            tenant.Id, eventEntity.Id, order.Id, line.Id, assignment.Id, participants[index].Id,
            order, line, assignment, participants[index], catalog, ticketType)).ToArray();
    }

    private static PaidOrderAcceptanceSnapshot CreatePaidAcceptance(
        Guid tenantId,
        Guid eventId,
        Guid orderId,
        Guid orderLineId,
        int quantity,
        long organizerMinor,
        Guid organizerPaymentProviderConnectionId) =>
        PaidOrderAcceptanceSnapshot.Create(
            Guid.CreateVersion7(),
            tenantId,
            tenantId,
            orderId,
            eventId,
            "phase20-paid-admission",
            "disclosure-1",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            Guid.CreateVersion7(),
            "Example Organizer",
            PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
                "registered_organization", "BE", null, "contact@example.test", "https://example.test/legal",
                "https://example.test/terms", "https://example.test/privacy"),
            PaidCheckoutOperatorDisclosure.Create(
                Guid.CreateVersion7(),
                "Example Operator",
                false,
                "https://events.example.test",
                "BE",
                "https://events.example.test",
                "https://events.example.test/legal",
                "https://events.example.test/terms",
                "https://events.example.test/privacy",
                "complaints@example.test",
                "Trust and Safety",
                "Payments Operations",
                "Dispute Operations",
                "Payment Reconciliation",
                "approved"),
            PaidOrderDeliverySnapshot.Create(
                new DateTimeOffset(2026, 9, 10, 17, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero),
                "Europe/Brussels"),
            "EUR",
            organizerMinor,
            0,
            0,
            organizerMinor,
            Guid.CreateVersion7(),
            7,
            "Refunds follow accepted policy v7.",
            "en-GB",
            "support@example.test",
            PaidCheckoutProviderDisclosure.Create(
                "stripe",
                "OrganizerDirect",
                "direct-charge",
                "EXAMPLE EVENT",
                "test",
                "instance-operator"),
            [PaidOrderAcceptanceLineFact.Create(
                orderLineId,
                "Admission",
                quantity,
                500,
                0,
                organizerMinor)],
            UtcNow,
            organizerPaymentProviderConnectionId: organizerPaymentProviderConnectionId,
            connectPlatformId: "platform-test",
            externalAccountId: "acct_admission_test",
            merchantCountryCode: "BE");

    private sealed class GatedAdmissionDigestService(IAdmissionCredentialDigestService inner)
        : IAdmissionCredentialDigestService
    {
        private readonly TaskCompletionSource firstGenerationEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirstGeneration =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int generationCount;

        internal Task FirstGenerationEntered => firstGenerationEntered.Task;
        internal int GenerationCount => Volatile.Read(ref generationCount);
        internal void ReleaseFirstGeneration() => releaseFirstGeneration.TrySetResult();

        public async Task<AdmissionCredentialMaterial> CreateAsync(
            AdmissionCredentialCreateRequest request,
            CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref generationCount);
            if (call == 1)
            {
                firstGenerationEntered.TrySetResult();
                await releaseFirstGeneration.Task.WaitAsync(cancellationToken);
            }
            return await inner.CreateAsync(request, cancellationToken);
        }

        public Task<AdmissionCredentialVerificationOutcome> VerifyAsync(
            AdmissionCredentialVerificationRequest request,
            CancellationToken cancellationToken) => inner.VerifyAsync(request, cancellationToken);
    }

    private sealed class FinalizationLockCommandSignal : DbCommandInterceptor
    {
        private readonly TaskCompletionSource commandStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task CommandStarted => commandStarted.Task;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("registration_finalization_effects", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                commandStarted.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "registration_finalization_effects",
                    StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                commandStarted.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RefundInsertCommandSignal : DbCommandInterceptor
    {
        private readonly TaskCompletionSource commandStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task CommandStarted => commandStarted.Task;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "refund_attempts",
                    StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                commandStarted.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitAcknowledgementLostUnitOfWork(IUnitOfWork inner) : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default) =>
            inner.ExecuteInTransactionAsync(operation, ct);

        public async Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            await inner.ExecuteInTransactionAsync(operation, ct);
            throw new TimeoutException("Simulated lost commit acknowledgement.");
        }

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) =>
            inner.ExecuteSerializableAsync(operation, ct);
    }

    private sealed class RecordingAdmissionDispatcher(ExploreDbContext context) : IAdmissionDeliveryDispatcher
    {
        internal int DispatchCount { get; private set; }

        public async Task<AdmissionDeliveryDispatchResult> DispatchAsync(
            AdmissionDeliveryDispatchRequest request,
            CancellationToken cancellationToken)
        {
            AdmissionDeliveryIntent? intent = await context.AdmissionDeliveryIntents.SingleOrDefaultAsync(
                intent => intent.Id == request.DeliveryIntentId, cancellationToken);
            if (intent is null)
            {
                throw new InvalidOperationException("Admission delivery intent was not committed before dispatch.");
            }
            intent.MarkRouted(UtcNow);
            intent.CompleteHandoff($"test:{intent.Id:N}", UtcNow);
            await context.SaveChangesAsync(cancellationToken);
            DispatchCount++;
            return new AdmissionDeliveryDispatchResult(AdmissionDeliveryOutcome.Delivered);
        }
    }

    private sealed class AdmissionSecretResolver : ISecretResolver
    {
        private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());

        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId,
            string qualifier, CancellationToken cancellationToken = default) => Task.FromResult(
                settingKey == Explore.Domain.Secrets.SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey &&
                qualifier == "v7"
                    ? SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, Key, SecretSourceType.EnvironmentVariable, scope, scopeId, UtcNow))
                    : SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedAdmissionTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private enum CollisionBarrierTarget
    {
        Ticket,
        Credential
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}

internal sealed class AdmissionPersistenceSurface
{
    private static readonly DateTime UtcNow = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    internal static AdmissionPersistenceSurface RequirePublicSurface() => new();

    internal TicketGraph IssueTicketGraph(
        SeededAssignment seed,
        Guid ticketId,
        Guid credentialId,
        int credentialVersion,
        int lookupKeyVersion,
        string lookupDigest)
    {
        AdmissionTicket ticket = AdmissionTicket.Issue(
            seed.Order,
            seed.Line,
            seed.Assignment,
            seed.Participant,
            seed.Catalog,
            seed.TicketType,
            ticketId,
            $"T-{ticketId:N}",
            credentialId,
            credentialVersion,
            lookupKeyVersion,
            lookupDigest,
            UtcNow);
        return new(ticket, ticketId, credentialId);
    }

    internal static (IEntityType Ticket, IEntityType Credential)
        RequireAdmissionEntities(IModel model) =>
        (
            model.FindEntityType(typeof(AdmissionTicket))
            ?? throw Missing(
                $"EF entity/configuration {typeof(AdmissionTicket).FullName}"),
            model.FindEntityType(typeof(AdmissionTicketCredential))
            ?? throw Missing(
                $"EF entity/configuration {typeof(AdmissionTicketCredential).FullName}")
        );

    internal static string DelimitedTableIdentifier(ExploreDbContext context, IEntityType entity)
    {
        string table = entity.GetTableName() ?? throw Missing($"table mapping for {entity.ClrType.FullName}");
        string? schema = entity.GetSchema();
        ISqlGenerationHelper sql = context.GetService<ISqlGenerationHelper>();
        return schema is null ? sql.DelimitIdentifier(table) : sql.DelimitIdentifier(table, schema);
    }

    internal static bool HasProperties(IReadOnlyList<IReadOnlyProperty> properties, params string[] names) =>
        properties.Select(property => property.Name).SequenceEqual(names);

    internal static ExploreDbContext CreateModelContext(string provider)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        switch (provider)
        {
            case "PostgreSql":
                builder.UseNpgsql("Host=localhost;Database=phase20_model;Username=unused");
                break;
            case "Sqlite":
                builder.UseSqlite("Data Source=:memory:");
                break;
            case "SqlServer":
                builder.UseSqlServer("Server=localhost;Database=phase20_model;Integrated Security=true;TrustServerCertificate=True");
                break;
            case "MariaDb":
                builder.UseMySql("Server=localhost;Database=phase20_model;User=unused", new MariaDbServerVersion(new Version(10, 11)));
                break;
            case "MySql":
                builder.UseMySql("Server=localhost;Database=phase20_model;User=unused", new MySqlServerVersion(new Version(8, 0)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
        return new ExploreDbContext(builder.UseSnakeCaseNamingConvention().Options);
    }

    private static InvalidOperationException Missing(string surface) =>
        new($"Phase 20 product RED: missing {surface}.");
}

internal sealed record SeededAssignment(
    Guid TenantId,
    Guid EventId,
    Guid OrderId,
    Guid LineId,
    Guid AssignmentId,
    Guid ParticipantId,
    RegistrationOrder Order,
    RegistrationOrderLine Line,
    RegistrationTicketAssignment Assignment,
    RegistrationParticipant Participant,
    EventTicketCatalogVersion Catalog,
    EventTicketType TicketType);

internal sealed record TicketGraph(AdmissionTicket Ticket, Guid TicketId, Guid CredentialId);

internal sealed class AdmissionInsertBarrier
{
    private readonly Lock sync = new();
    private readonly HashSet<Guid> arrivedWriters = [];
    private readonly TaskCompletionSource allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task AllArrived => allArrived.Task;
    internal int Arrivals { get { lock (sync) return arrivedWriters.Count; } }
    internal bool HasArrived(Guid writerId) { lock (sync) return arrivedWriters.Contains(writerId); }

    internal async ValueTask ArriveAsync(Guid writerId, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (!arrivedWriters.Add(writerId))
                throw new InvalidOperationException($"Admission writer {writerId:N} reached the insert barrier more than once.");
            if (arrivedWriters.Count == 2)
                allArrived.TrySetResult();
        }
        await release.Task.WaitAsync(cancellationToken);
    }

    internal void Release() => release.TrySetResult();
}

internal sealed class AdmissionInsertBarrierInterceptor(
    AdmissionInsertBarrier barrier,
    Guid writerId,
    string mappedTableIdentifier) : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await AwaitAdmissionInsertAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await AwaitAdmissionInsertAsync(command, cancellationToken);
        return result;
    }

    private ValueTask AwaitAdmissionInsertAsync(DbCommand command, CancellationToken cancellationToken) =>
        command.CommandText.Contains($"INSERT INTO {mappedTableIdentifier}", StringComparison.OrdinalIgnoreCase)
            ? barrier.ArriveAsync(writerId, cancellationToken)
            : ValueTask.CompletedTask;
}
