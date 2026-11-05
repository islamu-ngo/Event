using Explore.Domain.Enums;
using Explore.Domain.Services;

namespace Event.Domain.UnitTests.Settings;

public sealed class VisitorReadPolicyTests
{
    [Test]
    [Arguments(VisitorAccessMode.FullRegistrationAndAuth, false, true)]
    [Arguments(VisitorAccessMode.FullRegistrationAndAuth, true, true)]
    [Arguments(VisitorAccessMode.AnonymousOnly, false, true)]
    [Arguments(VisitorAccessMode.AnonymousOnly, true, true)]
    [Arguments(VisitorAccessMode.DirectoryListingOnly, false, true)]
    [Arguments(VisitorAccessMode.DirectoryListingOnly, true, true)]
    [Arguments(VisitorAccessMode.AuthenticatedOnly, false, false)]
    [Arguments(VisitorAccessMode.AuthenticatedOnly, true, true)]
    public async Task ReadAdmissionDependsOnModeAndValidatedAuthentication(
        VisitorAccessMode mode, bool authenticated, bool expected)
    {
        await Assert.That(VisitorReadPolicy.AllowsRead(mode, authenticated)).IsEqualTo(expected);
    }

    [Test]
    public async Task SuccessfulAbsenceUsesThePublicDefault()
    {
        await Assert.That(VisitorReadPolicy.ResolveMode(null, false, null))
            .IsEqualTo(VisitorAccessMode.FullRegistrationAndAuth);
    }

    [Test]
    [Arguments("null")]
    [Arguments("\"authenticatedonly\"")]
    [Arguments("\"AuthenticatedOnly \"")]
    [Arguments("\"3\"")]
    [Arguments("3")]
    [Arguments("\"unknown\"")]
    [Arguments("{")]
    public async Task MalformedPersistedModeCannotGrantPublicRead(string raw)
    {
        await Assert.That(() => VisitorReadPolicy.ResolveMode(raw, false, null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LockedInstancePrivacyCannotBeWeakenedByTenantOverride()
    {
        var mode = VisitorReadPolicy.ResolveMode("\"AuthenticatedOnly\"", true, "\"FullRegistrationAndAuth\"");

        await Assert.That(VisitorReadPolicy.AllowsRead(mode, false)).IsFalse();
    }

    [Test]
    public async Task UnlockedTenantPrivacyOverridesPublicInstanceDefault()
    {
        var mode = VisitorReadPolicy.ResolveMode("\"FullRegistrationAndAuth\"", false, "\"AuthenticatedOnly\"");

        await Assert.That(VisitorReadPolicy.AllowsRead(mode, false)).IsFalse();
    }

    [Test]
    public async Task LockedMalformedAuthorityCannotFallBackToValidTenantValue()
    {
        await Assert.That(() => VisitorReadPolicy.ResolveMode("\"unknown\"", true, "\"FullRegistrationAndAuth\""))
            .Throws<InvalidOperationException>();
    }
}
