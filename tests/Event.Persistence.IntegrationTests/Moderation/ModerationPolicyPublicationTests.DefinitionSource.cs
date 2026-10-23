using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task SelectedDefinition_RetainsCompleteGraphWithoutConstructingPublicationEvidenceOwners()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var request = database.Publication(1);
        await Assert.That((await database.PublishAsync(database.AdministratorId, request)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        var (admin, machine) = DeploymentIdentity(context, database.AdministratorId);
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, database.Inventory, new FixedClock());
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var authority = await new ModerationQualificationAuthority(context, admin, machine).AcquireAsync(database.Scope, default);
        await Assert.That(authority).IsNotNull();
        var selected = await definitions.AcquireSelectedAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, false);
        await Assert.That(selected).IsNotNull();
        await Assert.That(selected!.Classes.Single().Meaning).IsEqualTo("Native source classification.");
        await Assert.That(selected.Reasons.Single().Meaning).IsEqualTo("Current source authority and protected response.");
        var restored = ModerationPolicyPublicationProjection.RestoreHistory(selected);
        await Assert.That(restored).IsNotNull();
        await Assert.That(ModerationPolicyPublicationProjection.ProposedDigest(restored!.Proposed))
            .IsEqualTo(ModerationPolicyPublicationProjection.ProposedDigest(request.Proposed));
        var definition = await definitions.ReadDefinitionAsync(authority!, transaction.TransactionId,
            database.Scope, request.Proposed.PolicyId, request.Proposed.PublicationId, Now, default);
        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.DefinitionId).IsEqualTo(request.Proposed.PublicationId);
        await Assert.That(definition.ValidUntil).IsEqualTo(DateTimeOffset.MaxValue);
        await Assert.That(await definitions.AcquireSelectedAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, true)).IsNull();
        await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, transaction)).IsTrue();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task CandidateDefinition_RequiresOriginalTransactionCoordinatesAndPreparationLease(int change)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var request = database.Publication(1);
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using var context = database.Context();
        var (admin, machine) = DeploymentIdentity(context, database.AdministratorId);
        var clock = new DeploymentClock();
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        var prepared = await publications.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        if (change == 7)
        {
            var replacement = request with
            {
                OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = 2,
                Proposed = request.Proposed with
                {
                    PublicationId = Guid.CreateVersion7(), RequestedState = ModerationPolicyRequestedState.Draft
                }
            };
            await Assert.That((await publications.PublishAsync(replacement)).Outcome)
                .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        }
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, database.Inventory, clock);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var authority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
        await Assert.That(authority).IsNotNull();
        if (change == 1) await transaction.RollbackAsync();
        if (change == 4) context.ModerationTrustDomainId = Guid.CreateVersion7();
        if (change == 5) clock.UtcNow = Now.AddMinutes(10);
        if (change == 6)
        {
            await context.PlatformUserRoles.Where(row => row.UserId == database.AdministratorId).ExecuteDeleteAsync();
            context.ChangeTracker.Clear();
        }
        var candidate = await definitions.AcquireCandidateAsync(
            change == 2 ? Guid.CreateVersion7() : transaction.TransactionId,
            change == 3 ? Guid.CreateVersion7() : database.HostId, database.Scope,
            request.Proposed.PolicyId, request.Proposed.PublicationId, Now);
        if (change != 0)
        {
            await Assert.That(candidate).IsNull();
            return;
        }
        await Assert.That(candidate).IsNotNull();
        await Assert.That(candidate!.DefinitionId).IsEqualTo(prepared.Candidate!.DefinitionId);
        var definition = await definitions.ReadDefinitionAsync(authority!, transaction.TransactionId,
            database.Scope, request.Proposed.PolicyId, request.Proposed.PublicationId, Now, default);
        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.ValidUntil).IsEqualTo(Now.AddMinutes(10));
        await Assert.That(definition.DefinitionId).IsEqualTo(prepared.Candidate.DefinitionId);
        await Assert.That(definition.Policy.Proposed.PublicationId).IsEqualTo(request.Proposed.PublicationId);
        await Assert.That(definition.Policy.Proposed.RequestedState).IsEqualTo(ModerationPolicyRequestedState.Active);
        await Assert.That(await definitions.AcquireSelectedAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, false)).IsNull();
        await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, transaction)).IsTrue();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }
}
