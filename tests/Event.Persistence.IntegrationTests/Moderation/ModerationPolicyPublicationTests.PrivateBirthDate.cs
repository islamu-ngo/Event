using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task PrivateBirthDate_RequiresLiveAdmissionAndKeepsOnlyTheCurrentPiiOwnedReceipt()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var owner = AdmissionOwner(fixture, http);
        await Assert.That(async () => await owner.RecordBirthDateAsync(fixture.SubjectId, new(2000, 2, 29), 0))
            .Throws<Explore.Application.Exceptions.AuthorizationException>();
        foreach (var revision in new long[] { 0, 1 })
        {
            await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
                await owner.RecordBirthDateAsync(fixture.SubjectId, new(2000, 2, 29), revision, token))).IsTrue();
            var pii = await fixture.Context.Set<UserPii>().AsNoTracking().SingleAsync(row => row.UserId == fixture.SubjectId);
            await Assert.That(pii.ReadDeclaredBirthDate()).IsEqualTo(new DateOnly(2000, 2, 29));
            await Assert.That(pii.ReadBirthDateRevision()).IsEqualTo(revision + 1);
            var receipt = await fixture.Context.Set<UserBirthDateCollectionReceipt>().SingleAsync();
            await Assert.That(receipt.Id).IsEqualTo(pii.ReadBirthDateCollectionReceiptId());
            await Assert.That(receipt.UserId).IsEqualTo(fixture.SubjectId);
        }
    }

    [Test]
    public async Task PrivateBirthDate_CorrectionWithinOneAdmissionRetainsOnlyItsCurrentReceipt()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var owner = AdmissionOwner(fixture, http);
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
        {
            await owner.RecordBirthDateAsync(fixture.SubjectId, new(2000, 2, 29), 0, token);
            await owner.RecordBirthDateAsync(fixture.SubjectId, new(2001, 1, 1), 1, token);
        })).IsTrue();
        var pii = await fixture.Context.Set<UserPii>().AsNoTracking().SingleAsync(row => row.UserId == fixture.SubjectId);
        await Assert.That(pii.ReadDeclaredBirthDate()).IsEqualTo(new DateOnly(2001, 1, 1));
        await Assert.That(pii.ReadBirthDateRevision()).IsEqualTo(2);
        var receipt = await fixture.Context.Set<UserBirthDateCollectionReceipt>().SingleAsync();
        await Assert.That(receipt.Id).IsEqualTo(pii.ReadBirthDateCollectionReceiptId());
    }

    [Test]
    public async Task PrivateBirthDate_ClearDoesNotRequireLiveCollectionOrConsent()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var owner = AdmissionOwner(fixture, http);
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
            await owner.RecordBirthDateAsync(fixture.SubjectId, new(2001, 1, 1), 0, token))).IsTrue();
        fixture.Clock.UtcNow = nomination.ValidUntil;
        var status = await owner.ReadBirthDateStatusAsync(fixture.SubjectId);
        await Assert.That(status.Revision).IsEqualTo(1);
        await Assert.That(status.HasDeclaration).IsTrue();
        await owner.ClearBirthDateAsync(fixture.SubjectId, 1);
        await Assert.That((await owner.ReadBirthDateStatusAsync(fixture.SubjectId)).HasDeclaration).IsFalse();
        var pii = await fixture.Context.Set<UserPii>().AsNoTracking().SingleAsync(row => row.UserId == fixture.SubjectId);
        await Assert.That(pii.ReadDeclaredBirthDate()).IsNull();
        await Assert.That(pii.ReadBirthDateCollectionReceiptId()).IsNull();
        await Assert.That(pii.ReadBirthDateRevision()).IsEqualTo(2);
        await Assert.That(await fixture.Context.Set<UserBirthDateCollectionReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PrivateBirthDate_RetainedErasureDeniesStatusAndClearBeforePrimaryCleanup()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var owner = AdmissionOwner(fixture, http);
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
            await owner.RecordBirthDateAsync(fixture.SubjectId, new(2001, 1, 1), 0, token))).IsTrue();
        await fixture.Privacy.AppendAsync(Explore.Application.Contracts.PrivacyErasure.PrivacyErasureRequest.Create(
            Guid.CreateVersion7(), Explore.Application.Contracts.PrivacyErasure.PrivacyErasureSubjectKind.User,
            fixture.SubjectId, Explore.Application.Contracts.PrivacyErasure.PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        await Assert.That(async () => await owner.ReadBirthDateStatusAsync(fixture.SubjectId))
            .Throws<Explore.Application.Exceptions.AuthorizationException>();
        await Assert.That(async () => await owner.ClearBirthDateAsync(fixture.SubjectId, 1))
            .Throws<Explore.Application.Exceptions.AuthorizationException>();
        var pii = await fixture.Context.Set<UserPii>().AsNoTracking().SingleAsync(row => row.UserId == fixture.SubjectId);
        await Assert.That(pii.ReadBirthDateRevision()).IsEqualTo(1);
        await Assert.That(pii.ReadDeclaredBirthDate()).IsNotNull();
        await Assert.That(await fixture.Context.Set<UserBirthDateCollectionReceipt>().CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task PrivateBirthDate_PiiErasureDeletesTheCurrentReceiptWithoutDeletingTheUser()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var owner = AdmissionOwner(fixture, http);
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
            await owner.RecordBirthDateAsync(fixture.SubjectId, new(2000, 2, 29), 0, token))).IsTrue();
        var otherPiiCount = await fixture.Context.Set<UserPii>()
            .CountAsync(row => row.UserId != fixture.SubjectId);
        var users = new Explore.Persistence.Repositories.UserRepository(fixture.Context);

        await Assert.That(await users.ForgetPiiAsync(fixture.SubjectId)).IsEqualTo(1);
        await Assert.That(await fixture.Context.Set<UserPii>()
            .AnyAsync(row => row.UserId == fixture.SubjectId)).IsFalse();
        await Assert.That(await fixture.Context.Set<UserBirthDateCollectionReceipt>()
            .AnyAsync(row => row.UserId == fixture.SubjectId)).IsFalse();
        await Assert.That(await fixture.Context.Users
            .AnyAsync(row => row.Id == fixture.SubjectId)).IsTrue();
        await Assert.That(await fixture.Context.Set<UserPii>()
            .CountAsync(row => row.UserId != fixture.SubjectId)).IsEqualTo(otherPiiCount);
        await Assert.That(await users.ForgetPiiAsync(fixture.SubjectId)).IsEqualTo(0);
    }

    [Test]
    [Arguments("stale")]
    [Arguments("future")]
    public async Task PrivateBirthDate_InvalidCorrectionCannotRetainAReceiptOrDeclaration(string invalid)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var owner = AdmissionOwner(fixture, http);
        async Task WriteAsync() => await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
            await owner.RecordBirthDateAsync(fixture.SubjectId,
                invalid == "future" ? DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1) : new(2000, 1, 1),
                invalid == "stale" ? 1 : 0, token));
        if (invalid == "stale")
            await Assert.That(WriteAsync).Throws<Explore.Application.Exceptions.ConcurrencyConflictException>();
        else await Assert.That(WriteAsync).Throws<ArgumentException>();
        await Assert.That(await fixture.Context.Set<UserBirthDateCollectionReceipt>().CountAsync()).IsEqualTo(0);
        await Assert.That((await fixture.Context.Set<UserPii>().AsNoTracking()
            .SingleAsync(row => row.UserId == fixture.SubjectId)).ReadDeclaredBirthDate()).IsNull();
    }
}
