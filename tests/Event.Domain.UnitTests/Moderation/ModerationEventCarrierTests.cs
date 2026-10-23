using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationEventCarrierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CaseRetainsEventJurisdictionWithoutInventingResourceCoordinates()
    {
        var tenant = Tenant();
        var scope = AuthorityScope.Event(tenant, Guid.CreateVersion7());
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);

        allegation.ValidatePersistedState();
        await Assert.That(allegation.Scope).IsEqualTo(scope);
        await Assert.That(allegation.Subject).IsEqualTo(subject);
        await Assert.That(allegation.ScopeEventId).IsEqualTo(scope.Id);
        await Assert.That(allegation.SubjectOriginEventId).IsEqualTo(scope.Id);
        await Assert.That(allegation.ScopeResourceId).IsNull();
        await Assert.That(allegation.SubjectOriginResourceId).IsNull();
        await Assert.That(allegation.Target).IsNull();
    }

    [Test]
    public async Task SameIdentifierResourceCannotSubstituteForEventProtection()
    {
        var tenant = Tenant();
        var scope = AuthorityScope.Event(tenant, Guid.CreateVersion7());
        var resource = AuthorityScope.Resource(tenant, scope.Id);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var protector = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var measure = ModerationProtectiveMeasure.Create(Guid.CreateVersion7(), subject, protector, tenant,
            scope, [ModerationCapability.Participate], "Bounded event protection", Now, Now.AddHours(2), Now.AddHours(1));

        await Assert.That(() => measure.RecordIndependentReview(reviewer, resource, 1, Now.AddMinutes(1)))
            .Throws<InvalidOperationException>();
        measure.RecordIndependentReview(reviewer, scope, 1, Now.AddMinutes(1));
        measure.ValidatePersistedState();
        await Assert.That(measure.Scope).IsEqualTo(scope);
        await Assert.That(measure.Protector).IsEqualTo(protector);
        await Assert.That(measure.Reviewer).IsEqualTo(reviewer);
        await Assert.That(measure.ReviewerOriginEventId).IsEqualTo(scope.Id);
        await Assert.That(measure.ReviewerOriginResourceId).IsNull();
    }

    private static AuthorityScope Tenant() => AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
}
