using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion, false)]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt, false)]
    [Arguments(ModerationCaseTargetKind.LocalIncident, true)]
    public async Task IndependentReporterAddsFactsWithoutKnowingCanonicalIdentityOrRevision(
        ModerationCaseTargetKind kind, bool closed)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        var issued = await database.IssueAsync();
        var firstCommand = Submission(issued.Receipt.Id, issued.Token, database.Request().Material);
        await using var firstContext = database.Context();
        var firstHandler = new SubmitModerationAllegationCommandHandler(Committer(firstContext, database));
        var first = await firstHandler.ExecuteAsync(firstCommand);
        await Assert.That(first.IsSuccess).IsTrue();

        var otherId = Guid.CreateVersion7();
        Guid roleId;
        Guid canonicalId;
        await using (var setup = database.Context())
        {
            var allegation = await setup.ModerationCases.Include(row => row.Evidence).SingleAsync();
            canonicalId = allegation.Id;
            if (closed) allegation.Close(allegation.Revision, database.Clock.Now);
            setup.Users.Add(new User
            {
                Id = otherId,
                Pii = new UserPii
                {
                    UserId = otherId,
                    Email = $"{otherId:N}@example.invalid",
                    FirstName = "Independent",
                    LastName = "Reporter"
                }
            });
            var role = EventRoleAssignment.Create(database.TenantId, database.EventId, otherId,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, database.Clock.Now.UtcDateTime.AddHours(-1),
                database.Clock.Now.UtcDateTime.AddHours(1), database.CallerId);
            roleId = role.Id;
            setup.EventRoleAssignments.Add(role);
            await setup.SaveChangesAsync();
        }
        var other = new NativeCaller(otherId);
        var facts = database.Request(material: "Independent additional observation.") with { RelationshipSourceId = roleId };
        await using var otherContext = database.Context();
        otherContext.CurrentUserService = other;
        var intake = IssuanceCommitter(otherContext, database, other);
        var secondReceipt = await intake.IssueSubmissionReceiptAsync(facts, default);
        await Assert.That(secondReceipt).IsNotNull();
        var secondCommand = Submission(secondReceipt!.Receipt.Id, secondReceipt.Token, facts.Material)
            with
        { OperationId = firstCommand.OperationId };
        var otherHandler = new SubmitModerationAllegationCommandHandler(intake);
        var second = await otherHandler.ExecuteAsync(secondCommand);
        await Assert.That(second.IsSuccess).IsTrue();
        await Assert.That(second.Id).IsNotEqualTo(first.Id);
        var foreignReplay = await otherHandler.ExecuteAsync(firstCommand);
        await Assert.That(foreignReplay.IsSuccess).IsFalse();
        await Assert.That(foreignReplay.AcceptedAt).IsNull();
        foreach (var response in new[] { first, second })
        {
            var serialized = JsonSerializer.SerializeToElement(response);
            await Assert.That(serialized.TryGetProperty("CaseId", out _)).IsFalse();
            await Assert.That(serialized.TryGetProperty("AcceptedCaseRevision", out _)).IsFalse();
            await Assert.That(JsonSerializer.Serialize(response).Contains(canonicalId.ToString(), StringComparison.OrdinalIgnoreCase))
                .IsFalse();
        }
        await Assert.That((await firstHandler.ExecuteAsync(firstCommand))).IsEqualTo(first);
        await Assert.That((await otherHandler.ExecuteAsync(secondCommand))).IsEqualTo(second);
        await using var verify = database.Context();
        var retained = await verify.ModerationCases.Include(row => row.Evidence).SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Id).IsEqualTo(canonicalId);
        await Assert.That(retained.Revision).IsEqualTo(closed ? 3L : 2L);
        await Assert.That(retained.Evidence.Count).IsEqualTo(2);
        await Assert.That(retained.State).IsEqualTo(closed ? ModerationCaseState.Closed : ModerationCaseState.Open);
        await Assert.That(retained.Subject).IsNull();
        await Assert.That(await verify.ModerationCaseNarratives.CountAsync()).IsEqualTo(2);
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(2);
        await Assert.That(await verify.ModerationCaseTargetReceipts.CountAsync(row => row.ConsumedCaseId == canonicalId))
            .IsEqualTo(2);
        await Assert.That(await verify.ModerationCaseReadGrants.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationSanctions.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task FailedNewFactCheckpointRollsBackAppendCustodyConsumptionAndAcceptance()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.HiddenContentReceipt);
        var original = await database.IssueAsync();
        await using (var context = database.Context())
            await Assert.That((await new SubmitModerationAllegationCommandHandler(Committer(context, database))
                .ExecuteAsync(Submission(original.Receipt.Id, original.Token, database.Request().Material))).IsSuccess).IsTrue();
        var facts = database.Request(material: "New evidence rejected at the final native checkpoint.");
        var next = await database.IssueAsync(material: "New evidence rejected at the final native checkpoint.");
        var checkpoint = new ReceiptSavedCheckpoint(async context =>
            await context.EventRoleAssignments.Where(row => row.Id == database.RoleId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Version, row => row.Version + 1)));
        await using (var context = database.Context(checkpoint))
        {
            var rejected = await new SubmitModerationAllegationCommandHandler(Committer(context, database))
                .ExecuteAsync(Submission(next.Receipt.Id, next.Token, facts.Material));
            await Assert.That(rejected.IsSuccess).IsFalse();
            await Assert.That(rejected.AcceptedAt).IsNull();
        }
        await Assert.That(checkpoint.ObservedReceiptSave).IsTrue();
        await using var verify = database.Context();
        var allegation = await verify.ModerationCases.Include(row => row.Evidence).SingleAsync();
        await Assert.That(allegation.Revision).IsEqualTo(1L);
        await Assert.That(allegation.Evidence.Count).IsEqualTo(1);
        await Assert.That(await verify.ModerationCaseNarratives.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
        await Assert.That((await verify.ModerationCaseTargetReceipts.SingleAsync(row => row.Id == next.Receipt.Id))
            .ConsumedCaseId).IsNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DirectSubmissionCannotSelectCanonicalCaseOrRevision(bool caseIdentity)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var receipt = await database.IssueAsync();
        await using var context = database.Context();
        var committer = Committer(context, database);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            caseIdentity ? Guid.CreateVersion7() : Guid.Empty, Guid.CreateVersion7(),
            caseIdentity ? 0 : 1, receipt.Receipt.Id, receipt.Token)
        { Narrative = database.Request().Material };
        await Assert.That(async () => await committer.SubmitAsync(request, default)).Throws<ArgumentException>();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.AnyAsync()).IsFalse();
        await Assert.That((await verify.ModerationCaseTargetReceipts.SingleAsync()).ConsumedAt).IsNull();
    }

    [Test]
    public async Task ReissuedCopyIsAcceptedWithoutRecreatingPurgedMaterialOrAdvancingCase()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.LocalIncident);
        var original = await database.IssueAsync();
        await using var context = database.Context();
        var handler = new SubmitModerationAllegationCommandHandler(Committer(context, database));
        var first = await handler.ExecuteAsync(Submission(original.Receipt.Id, original.Token, database.Request().Material));
        await Assert.That(first.IsSuccess).IsTrue();
        await using (var purge = database.Context())
        {
            // Exercise the permanent acceptance floor after physical custody removal.
            await purge.ModerationCaseNarratives.ExecuteDeleteAsync();
        }
        var copy = await database.IssueAsync();
        var command = Submission(copy.Receipt.Id, copy.Token, database.Request().Material);
        var second = await handler.ExecuteAsync(command);
        await Assert.That(second.IsSuccess).IsTrue();
        await Assert.That(second.Id).IsNotEqualTo(first.Id);
        await Assert.That(await handler.ExecuteAsync(command)).IsEqualTo(second);
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseNarratives.AnyAsync()).IsFalse();
        var allegation = await verify.ModerationCases.Include(row => row.Evidence).SingleAsync();
        await Assert.That(allegation.Revision).IsEqualTo(1L);
        await Assert.That(allegation.Evidence.Count).IsEqualTo(1);
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(2);
    }
}
