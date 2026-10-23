using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FreshAdoptionInspectsActualMaterialWithoutSafeAttestationOrOriginalAllegation(bool file)
    {
        await using var fixture = await InspectionFixture.CreateAsync(file);
        await using var session = fixture.Session();
        var pending = await session.PendingAsync(fixture.Intent);
        var borrowed = new List<ReadOnlyMemory<byte>>();
        using var received = new MemoryStream();
        var allowed = await pending.DiscloseAsync(async (bytes, destination, token) =>
        {
            await Assert.That(destination).IsEqualTo(!file);
            await Assert.That(session.Context.Database.CurrentTransaction).IsNotNull();
            await Assert.That(session.Context.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(System.Data.IsolationLevel.Serializable);
            borrowed.Add(bytes);
            await received.WriteAsync(bytes, token);
        }, default);
        await Assert.That(allowed).IsTrue();
        await Assert.That(received.ToArray()).IsEquivalentTo(fixture.Expected);
        await Assert.That(borrowed.All(bytes => bytes.ToArray().All(value => value == 0))).IsTrue();
        await Assert.That(await pending.DiscloseAsync((_, _, _) =>
            throw new InvalidOperationException("A pending inspection is single-use."), default)).IsFalse();
        await Assert.That(session.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(session.Context.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(session.Context.ModerationTrustDomainId).IsNull();
        session.Context.ModerationTrustDomainId = fixture.Native.Database.Scope.TrustDomainId;
        await Assert.That(await session.Context.Set<EventResourceModerationQuarantineReview>().CountAsync()).IsEqualTo(2);
        await Assert.That(await session.Context.Set<EventResourceModerationQuarantineRelease>().CountAsync()).IsEqualTo(0);
        await Assert.That(await session.Context.ModerationFindings.AnyAsync(
            row => row.Id == fixture.Native.HistoricalFindingId)).IsTrue();
        await Assert.That((await session.Context.EventResources.SingleAsync(
            row => row.Id == fixture.Native.ResourceId)).ModerationQuarantineCount).IsEqualTo(2);
    }

    [Test]
    [Arguments("purpose")]
    [Arguments("expired")]
    [Arguments("reviewer")]
    [Arguments("controller")]
    [Arguments("role")]
    [Arguments("audience")]
    [Arguments("tenant-unbound")]
    [Arguments("material")]
    [Arguments("deleted")]
    [Arguments("primary-erasure")]
    [Arguments("retained-erasure")]
    public async Task PendingInspectionRequalifiesBeforeAnyPrivateMaterialOutput(string changed)
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        await using var session = fixture.Session();
        var pending = await session.PendingAsync(fixture.Intent);
        switch (changed)
        {
            case "purpose":
                await fixture.Native.AcceptAsync(fixture.Native.Author,
                    fixture.Native.Intent(fixture.Native.Author, ResourceSafetyReviewStep.WithdrawPurpose, fixture.PurposeId));
                break;
            case "expired":
                fixture.Native.Clock.Now = fixture.Native.Deadline;
                break;
            case "reviewer":
                await fixture.Native.SourceAsync(fixture.Native.Database.AdministratorId, new WithdrawModerationReviewerSource
                {
                    OperationId = Guid.CreateVersion7(), Target = fixture.Native.Scope,
                    ExpectedQualificationRevision = 1, AssignmentId = fixture.Intent.ReviewerAssignmentId,
                    ExpectedAssignmentRevision = 3
                });
                break;
            case "role":
                await using (var writer = fixture.Native.Context())
                    await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Native.Author).ExecuteDeleteAsync();
                break;
            case "controller":
                await fixture.Native.SourceAsync(fixture.Native.Database.AdministratorId, new QualifyModerationReviewerController
                {
                    OperationId = Guid.CreateVersion7(), Target = fixture.Native.Scope, ExpectedQualificationRevision = 1,
                    AssignmentId = fixture.Intent.ReviewerAssignmentId, ExpectedAssignmentRevision = 3,
                    ControllerHumanId = fixture.Native.Database.SubjectId, EvidenceId = Guid.CreateVersion7(),
                    Justification = "New independently established controller.", QualifiedUntil = fixture.Native.Clock.Now.AddMinutes(15)
                });
                break;
            case "audience":
                await using (var writer = fixture.Native.Context())
                {
                    var resource = await writer.EventResources.Include(row => row.AudienceRules)
                        .SingleAsync(row => row.Id == fixture.Native.ResourceId);
                    resource.ReplacePolicy(resource.Availability,
                        [EventResourceAudienceRule.Create(resource.TenantId, resource.EventId, resource.Id, EventResourceAudienceKindEnum.Public)],
                        resource.ConcurrencyStamp, fixture.Native.Author, fixture.Native.Clock.Now.UtcDateTime);
                    await writer.SaveChangesAsync();
                }
                break;
            case "tenant-unbound":
                session.Context.TenantContext = null;
                break;
            case "material":
                await using (var writer = fixture.Native.Context())
                    await writer.EventResources.Where(row => row.Id == fixture.Native.ResourceId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.MaterialVersionId, Guid.CreateVersion7()));
                break;
            case "deleted":
                await using (var writer = fixture.Native.Context())
                {
                    var resource = await writer.EventResources.SingleAsync(row => row.Id == fixture.Native.ResourceId);
                    resource.Delete(resource.ConcurrencyStamp, fixture.Native.Author, fixture.Native.Clock.Now.UtcDateTime);
                    await writer.SaveChangesAsync();
                }
                break;
            case "retained-erasure":
                await fixture.Retained().AppendAsync(PrivacyErasureRequest.Create(Guid.CreateVersion7(),
                    PrivacyErasureSubjectKind.User, fixture.Native.Database.SubjectId,
                    PrivacyErasureReasonCode.SubjectErasureRequest, 1));
                break;
            case "primary-erasure":
                await using (var writer = fixture.Native.Context())
                {
                    var now = fixture.Native.Clock.Now.UtcDateTime;
                    var fact = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
                        fixture.Native.Database.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
                    writer.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, 1,
                        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), now.AddMinutes(5), now));
                    await writer.SaveChangesAsync();
                }
                break;
        }
        var outputs = 0;
        var allowed = await pending.DiscloseAsync((_, _, _) =>
        {
            outputs++;
            return Task.CompletedTask;
        }, default);
        await Assert.That(allowed).IsFalse();
        await Assert.That(outputs).IsEqualTo(0);
    }

    [Test]
    [Arguments("cause")]
    [Arguments("material")]
    [Arguments("case")]
    [Arguments("adoption")]
    [Arguments("reviewer")]
    [Arguments("epoch")]
    [Arguments("other-human")]
    public async Task OpaquePurposeCoordinatesNeverTransferInspectionAuthority(string substitution)
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        var intent = substitution switch
        {
            "cause" => fixture.Intent with { QuarantineId = fixture.Native.OtherCauseId },
            "material" => fixture.Intent with { MaterialVersionId = Guid.CreateVersion7() },
            "case" => fixture.Intent with { CaseId = Guid.CreateVersion7() },
            "adoption" => fixture.Intent with { AdoptionId = fixture.PurposeId },
            "reviewer" => fixture.Intent with { ReviewerAssignmentId = fixture.Native.Assignments[fixture.Native.Approver] },
            "epoch" => fixture.Intent with { ExpectedQualificationRevision = fixture.Intent.ExpectedQualificationRevision + 1 },
            _ => fixture.Intent
        };
        await using var session = fixture.Session(substitution == "other-human" ? fixture.Native.Database.AdministratorId : null);
        var outputs = 0;
        var allowed = await (await session.PendingAsync(intent)).DiscloseAsync((_, _, _) =>
        {
            outputs++;
            return Task.CompletedTask;
        }, default);
        await Assert.That(allowed).IsFalse();
        await Assert.That(outputs).IsEqualTo(0);
    }

    [Test]
    public async Task ExpiryAtAwaitedOutputBarrierStopsRemainingNativeFileAndZerosBorrowedBytes()
    {
        await using var fixture = await InspectionFixture.CreateAsync(file: true);
        await using var session = fixture.Session();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ReadOnlyMemory<byte> borrowed = default;
        var outputs = 0;
        var pending = await session.PendingAsync(fixture.Intent);
        var disclosure = pending.DiscloseAsync(async (bytes, _, token) =>
        {
            outputs++;
            borrowed = bytes;
            entered.TrySetResult();
            await resume.Task.WaitAsync(token);
        }, timeout.Token);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            fixture.Native.Clock.Now = fixture.Native.Deadline;
        }
        finally { resume.TrySetResult(); }
        await Assert.That(async () => await disclosure).Throws<InvalidOperationException>();
        await Assert.That(outputs).IsEqualTo(1);
        await Assert.That(borrowed.Length).IsEqualTo(65_536);
        await Assert.That(borrowed.ToArray().All(value => value == 0)).IsTrue();
        await Assert.That(session.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task TerminalProviderShapedFailureIsNotRetriedAndClearsInspectedMaterial()
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        await using var session = fixture.Session();
        var attempts = 0;
        ReadOnlyMemory<byte> borrowed = default;
        var pending = await session.PendingAsync(fixture.Intent);
        await Assert.That(async () => await pending.DiscloseAsync((bytes, _, _) =>
        {
            attempts++;
            borrowed = bytes;
            throw new SqliteException("Terminal witness failure.", 5);
        }, default)).Throws<SqliteException>();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(borrowed.Length).IsGreaterThan(0);
        await Assert.That(borrowed.ToArray().All(value => value == 0)).IsTrue();
        await Assert.That(session.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualProviderMaterialCannotBeSubstitutedOrResurrected(bool discarded)
    {
        await using var fixture = await InspectionFixture.CreateAsync(file: true);
        await using var session = fixture.Session();
        var pending = await session.PendingAsync(fixture.Intent);
        if (discarded) await fixture.DiscardProviderBytesAsync();
        else await fixture.CorruptProviderBytesAsync();
        var outputs = 0;
        var allowed = await pending.DiscloseAsync((_, _, _) =>
        {
            outputs++;
            return Task.CompletedTask;
        }, default);
        await Assert.That(allowed).IsFalse();
        await Assert.That(outputs).IsEqualTo(0);
    }
}
