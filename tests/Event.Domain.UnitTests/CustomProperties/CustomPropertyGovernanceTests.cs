namespace Event.Domain.UnitTests.CustomProperties;

using Explore.Domain.Constants;

public class CustomPropertyGovernanceTests
{
    [Test]
    public async Task NormalizeNamespace_ShouldLowercaseAndDotSeparateSegments()
    {
        var normalized = CustomPropertyIdentity.NormalizeNamespace(" Platform Islamic Pack ");

        await Assert.That(normalized).IsEqualTo("platform.islamic.pack");
    }

    [Test]
    public async Task NormalizeKey_ShouldLowercaseAndUnderscoreSeparateSegments()
    {
        var normalized = CustomPropertyIdentity.NormalizeKey(" Skill Level ");

        await Assert.That(normalized).IsEqualTo("skill_level");
    }

    [Test]
    public async Task NormalizedNamespaceAndKey_ShouldTreatCaseAndWhitespaceAsSameMachineIdentity()
    {
        var firstIdentity = $"{CustomPropertyIdentity.NormalizeNamespace("Platform")}/{CustomPropertyIdentity.NormalizeKey("Foo")}";
        var secondIdentity = $"{CustomPropertyIdentity.NormalizeNamespace(" platform ")}/{CustomPropertyIdentity.NormalizeKey(" FOO ")}";

        await Assert.That(secondIdentity).IsEqualTo(firstIdentity);
    }

    [Test]
    public async Task IsReserved_ShouldRecognizeReservedRootsAndChildren()
    {
        await Assert.That(CustomPropertyNamespaces.IsReserved("platform")).IsTrue();
        await Assert.That(CustomPropertyNamespaces.IsReserved("sector.islamic")).IsTrue();
        await Assert.That(CustomPropertyNamespaces.IsReserved("pack.tech")).IsTrue();
        await Assert.That(CustomPropertyNamespaces.IsReserved("tenant.local")).IsFalse();
    }

    [Test]
    public async Task IsTenantOwned_ShouldRecognizeTenantNamespacesOnly()
    {
        await Assert.That(CustomPropertyNamespaces.IsTenantOwned("tenant")).IsTrue();
        await Assert.That(CustomPropertyNamespaces.IsTenantOwned("tenant.community")).IsTrue();
        await Assert.That(CustomPropertyNamespaces.IsTenantOwned("platform.community")).IsFalse();
    }

    [Test]
    public async Task IsReservedLayer2Semantic_ShouldMatchKnownTypedSectorSemantics()
    {
        var reserved = CustomPropertySemanticReservations.IsReservedLayer2Semantic("sector.islamic", "madhab_id");
        var tenantReserved = CustomPropertySemanticReservations.IsReservedLayer2Semantic("tenant.local", "madhab_id");
        var notReserved = CustomPropertySemanticReservations.IsReservedLayer2Semantic("tenant.local", "prayer_notes");

        await Assert.That(reserved).IsTrue();
        await Assert.That(tenantReserved).IsTrue();
        await Assert.That(notReserved).IsFalse();
    }
}
