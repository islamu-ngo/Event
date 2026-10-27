using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task DeclaredAge_WithoutAPrivateDeclarationIsUnknownNotAssured()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, _) = await ConsentOriginalAsync(fixture, false, []);
        var option = (await AdmissionOwner(fixture, http).ReadOptionsAsync(fixture.SubjectId)).Single();
        await Assert.That(option.DeclaredAge!.Kind).IsEqualTo(ModerationAgeEvidenceKind.Unknown);
        await Assert.That(option.DeclaredAge.Outcome).IsEqualTo(ModerationDeclaredAgeOutcome.Unknown);
        await Assert.That(option.DeclaredAge.MinimumAge).IsEqualTo(18);
        await Assert.That(option.DeclaredAge.BirthDateRevision).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeclaredAge_UsesOnlyTheOriginalLiveReceiptAndNeverBecomesAssured(bool underThreshold)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var owner = AdmissionOwner(fixture, http);
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
            await owner.RecordBirthDateAsync(fixture.SubjectId,
                underThreshold ? new(2020, 2, 29) : new(2000, 2, 29), 0, token))).IsTrue();
        var receipt = await fixture.Context.Set<UserBirthDateCollectionReceipt>().AsNoTracking().SingleAsync();
        var option = (await owner.ReadOptionsAsync(fixture.SubjectId)).Single();
        var declared = option.DeclaredAge!;
        await Assert.That(declared.Kind).IsEqualTo(ModerationAgeEvidenceKind.Declared);
        await Assert.That(declared.Outcome).IsEqualTo(underThreshold
            ? ModerationDeclaredAgeOutcome.UnderThreshold : ModerationDeclaredAgeOutcome.AtLeastThreshold);
        await Assert.That(declared.MinimumAge).IsEqualTo(18);
        await Assert.That(declared.BirthDateRevision).IsEqualTo(1);
        await Assert.That(declared.ValidUntil <= receipt.ValidUntil && declared.ValidUntil <= option.ValidUntil).IsTrue();
        await Assert.That(declared.ValidUntil > declared.EvaluatedAt).IsTrue();
    }

    [Test]
    public async Task DeclaredAge_CorrectionAndClearingCannotLeaveAnOldThresholdResult()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var owner = AdmissionOwner(fixture, http);
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
            await owner.RecordBirthDateAsync(fixture.SubjectId, new(2000, 1, 1), 0, token))).IsTrue();
        var first = (await owner.ReadOptionsAsync(fixture.SubjectId)).Single().DeclaredAge!;
        await Assert.That(first.Outcome).IsEqualTo(ModerationDeclaredAgeOutcome.AtLeastThreshold);
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
            await owner.RecordBirthDateAsync(fixture.SubjectId, new(2020, 1, 1), 1, token))).IsTrue();
        var corrected = (await owner.ReadOptionsAsync(fixture.SubjectId)).Single().DeclaredAge!;
        await Assert.That(corrected.Outcome).IsEqualTo(ModerationDeclaredAgeOutcome.UnderThreshold);
        await Assert.That(corrected.BirthDateRevision).IsEqualTo(2);
        await owner.ClearBirthDateAsync(fixture.SubjectId, 2);
        var cleared = (await owner.ReadOptionsAsync(fixture.SubjectId)).Single().DeclaredAge!;
        await Assert.That(cleared.Kind).IsEqualTo(ModerationAgeEvidenceKind.Unknown);
        await Assert.That(cleared.Outcome).IsEqualTo(ModerationDeclaredAgeOutcome.Unknown);
        await Assert.That(cleared.BirthDateRevision).IsEqualTo(3);
        await Assert.That(await fixture.Context.Set<UserBirthDateCollectionReceipt>().CountAsync()).IsEqualTo(0);
        // An earlier response is immutable display metadata, never a current eligibility permit.
        await Assert.That(first.BirthDateRevision).IsEqualTo(1);
    }

    [Test]
    [Arguments("observation")]
    [Arguments("assessment")]
    [Arguments("adoption")]
    [Arguments("policy")]
    [Arguments("policy-revision")]
    [Arguments("publication")]
    [Arguments("profile")]
    [Arguments("consent")]
    [Arguments("expired")]
    public async Task DeclaredAge_CannotReuseAnotherOrExpiredCollectionOriginal(string substitution)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var owner = AdmissionOwner(fixture, http);
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
            await owner.RecordBirthDateAsync(fixture.SubjectId, new(2000, 1, 1), 0, token))).IsTrue();
        var receipts = fixture.Context.Set<UserBirthDateCollectionReceipt>().Where(row => row.UserId == fixture.SubjectId);
        var other = Guid.CreateVersion7();
        switch (substitution)
        {
            case "observation": await receipts.ExecuteUpdateAsync(set => set.SetProperty(row => row.ObservationId, other)); break;
            case "assessment": await receipts.ExecuteUpdateAsync(set => set.SetProperty(row => row.AssessmentId, other)); break;
            case "adoption": await receipts.ExecuteUpdateAsync(set => set.SetProperty(row => row.AssessmentAdoptionId, other)); break;
            case "policy": await receipts.ExecuteUpdateAsync(set => set.SetProperty(row => row.PolicyId, other)); break;
            case "policy-revision": await receipts.ExecuteUpdateAsync(set => set.SetProperty(row => row.PolicyRevision, 99L)); break;
            case "publication": await receipts.ExecuteUpdateAsync(set => set.SetProperty(row => row.PublicationId, other)); break;
            case "profile": await receipts.ExecuteUpdateAsync(set => set.SetProperty(row => row.ProfileId, other)); break;
            case "consent": await receipts.ExecuteUpdateAsync(set => set.SetProperty(row => row.ConsentId, (Guid?)other)); break;
            case "expired": await receipts.ExecuteUpdateAsync(set => set.SetProperty(row => row.ValidUntil, fixture.Clock.UtcNow.AddTicks(-1))); break;
        }
        var declared = (await owner.ReadOptionsAsync(fixture.SubjectId)).Single().DeclaredAge!;
        await Assert.That(declared.Kind).IsEqualTo(ModerationAgeEvidenceKind.Unknown);
        await Assert.That(declared.Outcome).IsEqualTo(ModerationDeclaredAgeOutcome.Unknown);
        await Assert.That(declared.BirthDateRevision).IsEqualTo(1);
        await Assert.That((await owner.ReadBirthDateStatusAsync(fixture.SubjectId)).HasDeclaration).IsTrue();
    }

    [Test]
    public async Task DeclaredAge_WithdrawnRequiredConsentCannotSupplyAQualifiedThreshold()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false,
            [ModerationCollectionPrerequisiteKind.SubjectConsent]);
        var consent = (await ConsentOwner(fixture, http).GrantAsync(nomination))!;
        var owner = AdmissionOwner(fixture, http);
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, consent.Id, async (_, token) =>
            await owner.RecordBirthDateAsync(fixture.SubjectId, new(2000, 1, 1), 0, token))).IsTrue();
        await ConsentOwner(fixture, http).WithdrawAsync(ConsentWithdrawal(consent));
        await Assert.That((await owner.ReadOptionsAsync(fixture.SubjectId)).Count).IsEqualTo(0);
        await Assert.That((await owner.ReadBirthDateStatusAsync(fixture.SubjectId)).HasDeclaration).IsTrue();
    }

    [Test]
    public async Task DeclaredAge_ExpiryDuringLaterAdmissionValidationCannotEscapeAsACurrentProjection()
    {
        var clock = new DeclaredProjectionClock();
        var checkpoint = new AfterDeclaredReceiptRead(clock);
        await using var fixture = await IndividualPolicyFixture.CreateAsync(checkpoint);
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var owner = AdmissionOwner(fixture, http);
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
            await owner.RecordBirthDateAsync(fixture.SubjectId, new(2000, 1, 1), 0, token))).IsTrue();
        clock.Now = fixture.Clock.UtcNow;
        clock.ExpiresAt = clock.Now.AddSeconds(10);
        await fixture.Context.Set<UserBirthDateCollectionReceipt>().Where(row => row.UserId == fixture.SubjectId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.ValidUntil, clock.ExpiresAt));
        checkpoint.Armed = true;
        var options = await AdmissionOwner(fixture, http, clock).ReadOptionsAsync(fixture.SubjectId);
        await Assert.That(clock.ReadsAfterReceipt > 1).IsTrue();
        await Assert.That(options.Single().ValidUntil > clock.GetUtcNow()).IsTrue();
        await Assert.That(options.Single().DeclaredAge).IsNull();
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    private sealed class DeclaredProjectionClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public bool ReceiptRead { get; set; }
        public int ReadsAfterReceipt { get; private set; }
        public override DateTimeOffset GetUtcNow() => ReceiptRead && ++ReadsAfterReceipt > 1 ? ExpiresAt : Now;
    }

    private sealed class AfterDeclaredReceiptRead(DeclaredProjectionClock clock)
        : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData eventData,
            System.Data.Common.DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is { } context)
            {
                var entity = context.Model.FindEntityType(typeof(UserBirthDateCollectionReceipt))!;
                var sql = context.GetService<ISqlGenerationHelper>();
                if (command.CommandText.Contains($"FROM {sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())}",
                    StringComparison.Ordinal))
                {
                    Armed = false;
                    clock.ReceiptRead = true;
                }
            }
            return ValueTask.FromResult(result);
        }
    }
}
