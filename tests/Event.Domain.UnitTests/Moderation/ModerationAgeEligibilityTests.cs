using System.Globalization;
using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationAgeEligibilityTests
{
    [Test]
    [Arguments("2008-10-14", "2026-10-13T23:59:59+00:00", ModerationDeclaredAgeOutcome.UnderThreshold)]
    [Arguments("2008-10-14", "2026-10-14T00:00:00+00:00", ModerationDeclaredAgeOutcome.AtLeastThreshold)]
    [Arguments("2008-10-14", "2026-10-14T14:00:00+14:00", ModerationDeclaredAgeOutcome.AtLeastThreshold)]
    [Arguments("2008-10-14", "2026-10-13T19:59:59-04:00", ModerationDeclaredAgeOutcome.UnderThreshold)]
    [Arguments("2008-02-29", "2026-02-28T23:59:59+00:00", ModerationDeclaredAgeOutcome.UnderThreshold)]
    [Arguments("2008-02-29", "2026-03-01T00:00:00+00:00", ModerationDeclaredAgeOutcome.AtLeastThreshold)]
    [Arguments("2004-02-29", "2024-02-28T23:59:59+00:00", ModerationDeclaredAgeOutcome.AtLeastThreshold)]
    [Arguments("2027-01-01", "2026-10-14T00:00:00+00:00", ModerationDeclaredAgeOutcome.Unknown)]
    public async Task Declaration_UsesAuthorityDateWithoutAssertingAssurance(
        string birth, string instant, ModerationDeclaredAgeOutcome expected)
    {
        var policy = Policy();
        var birthDate = DateOnly.ParseExact(birth, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var at = DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);
        await Assert.That(policy.EvaluateDeclaration(birthDate, 18, at)).IsEqualTo(expected);
        await Assert.That(policy.AuthorityTimeZone.Id).IsEqualTo(TimeZoneInfo.Utc.Id);
        await Assert.That(policy.LeapDayAnniversary).IsEqualTo(ModerationLeapDayAnniversary.March1);
    }

    [Test]
    [Arguments(ModerationLeapDayAnniversary.March1, ModerationDeclaredAgeOutcome.UnderThreshold)]
    [Arguments(ModerationLeapDayAnniversary.February28, ModerationDeclaredAgeOutcome.AtLeastThreshold)]
    public async Task NonLeapAnniversary_IsAnExplicitPolicyChoice(
        ModerationLeapDayAnniversary anniversary, ModerationDeclaredAgeOutcome expected)
    {
        var policy = Policy(TimeZoneInfo.Utc, anniversary);
        await Assert.That(policy.EvaluateDeclaration(new DateOnly(2008, 2, 29), 18,
            new DateTimeOffset(2026, 2, 28, 12, 0, 0, TimeSpan.Zero))).IsEqualTo(expected);
    }

    [Test]
    [Arguments(ModerationLeapDayAnniversary.March1)]
    [Arguments(ModerationLeapDayAnniversary.February28)]
    public async Task LeapYearThreshold_DoesNotMoveTheBirthdayToFebruary28(
        ModerationLeapDayAnniversary anniversary)
    {
        var policy = Policy(TimeZoneInfo.Utc, anniversary);
        await Assert.That(policy.EvaluateDeclaration(new DateOnly(2004, 2, 29), 20,
            new DateTimeOffset(2024, 2, 28, 23, 59, 59, TimeSpan.Zero)))
            .IsEqualTo(ModerationDeclaredAgeOutcome.UnderThreshold);
        await Assert.That(policy.EvaluateDeclaration(new DateOnly(2004, 2, 29), 20,
            new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)))
            .IsEqualTo(ModerationDeclaredAgeOutcome.AtLeastThreshold);
    }

    [Test]
    [Arguments(14, ModerationDeclaredAgeOutcome.AtLeastThreshold)]
    [Arguments(-12, ModerationDeclaredAgeOutcome.UnderThreshold)]
    public async Task CalendarDate_ComesFromThePolicyZoneRatherThanServerOrViewer(
        int offsetHours, ModerationDeclaredAgeOutcome expected)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("AuthorityCalendar",
            TimeSpan.FromHours(offsetHours), "Authority calendar", "Authority calendar");
        var policy = Policy(zone);
        await Assert.That(policy.EvaluateDeclaration(new DateOnly(2008, 10, 14), 18,
            new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero))).IsEqualTo(expected);
    }

    [Test]
    public async Task MissingOrCorrectedDeclaration_DoesNotRetainTheEarlierThreshold()
    {
        var policy = Policy();
        var at = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        await Assert.That(policy.EvaluateDeclaration(new DateOnly(2008, 10, 14), 18, at))
            .IsEqualTo(ModerationDeclaredAgeOutcome.AtLeastThreshold);
        await Assert.That(policy.EvaluateDeclaration(new DateOnly(2010, 10, 14), 18, at))
            .IsEqualTo(ModerationDeclaredAgeOutcome.UnderThreshold);
        await Assert.That(policy.EvaluateDeclaration(null, 18, at))
            .IsEqualTo(ModerationDeclaredAgeOutcome.Unknown);
    }

    [Test]
    public async Task VeryLargeThresholdAndLastCalendarYear_DoNotOverflowAnAnniversary()
    {
        var policy = Policy();
        var at = new DateTimeOffset(9999, 12, 31, 12, 0, 0, TimeSpan.Zero);
        await Assert.That(policy.EvaluateDeclaration(new DateOnly(9999, 1, 1), 18, at))
            .IsEqualTo(ModerationDeclaredAgeOutcome.UnderThreshold);
        await Assert.That(policy.EvaluateDeclaration(new DateOnly(2008, 10, 14), int.MaxValue, at))
            .IsEqualTo(ModerationDeclaredAgeOutcome.UnderThreshold);
    }

    [Test]
    public async Task InvalidPolicyCoordinatesOrThreshold_AreNotEvaluated()
    {
        var policy = Policy();
        await Assert.That(() => new ModerationAgePolicy(Guid.NewGuid(), 1, policy.Jurisdiction))
            .Throws<ArgumentException>();
        await Assert.That(() => new ModerationAgePolicy(Guid.CreateVersion7(), 0, policy.Jurisdiction))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ModerationAgePolicy(Guid.CreateVersion7(), 1, policy.Jurisdiction,
            TimeZoneInfo.Utc, (ModerationLeapDayAnniversary)99)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => policy.EvaluateDeclaration(new DateOnly(2008, 10, 14), -1,
            new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero)))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static ModerationAgePolicy Policy(
        TimeZoneInfo? zone = null,
        ModerationLeapDayAnniversary anniversary = ModerationLeapDayAnniversary.March1)
    {
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
        return zone is null
            ? new(Guid.CreateVersion7(), 1, scope)
            : new(Guid.CreateVersion7(), 1, scope, zone, anniversary);
    }
}
