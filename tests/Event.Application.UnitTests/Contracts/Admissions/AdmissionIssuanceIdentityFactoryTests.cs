using Explore.Application.Services.Registration;

namespace ApplicationUnitTests.Contracts.Admissions;

public sealed class AdmissionIssuanceIdentityFactoryTests
{
    [Test]
    public async Task SameLineageIsStableWhileTenantAndPurposeDifferAndUuidBitsRemainValid()
    {
        Guid tenant = Guid.CreateVersion7();
        Guid otherTenant = Guid.CreateVersion7();
        Guid effect = Guid.CreateVersion7();
        Guid assignment = Guid.CreateVersion7();

        Guid first = AdmissionIssuanceIdentityFactory.Create(tenant, effect, assignment, "ticket");
        Guid retry = AdmissionIssuanceIdentityFactory.Create(tenant, effect, assignment, "ticket");
        Guid otherTenantValue = AdmissionIssuanceIdentityFactory.Create(otherTenant, effect, assignment, "ticket");
        Guid otherPurpose = AdmissionIssuanceIdentityFactory.Create(tenant, effect, assignment, "credential");

        await Assert.That(retry).IsEqualTo(first);
        await Assert.That(otherTenantValue).IsNotEqualTo(first);
        await Assert.That(otherPurpose).IsNotEqualTo(first);
        await Assert.That(first.Version).IsEqualTo(7);
        await Assert.That((first.ToByteArray()[8] & 0xc0)).IsEqualTo(0x80);
    }

    [Test]
    public async Task MalformedLineageRejects()
    {
        await Assert.That(() => AdmissionIssuanceIdentityFactory.Create(
            Guid.Empty, Guid.CreateVersion7(), Guid.CreateVersion7(), "ticket")).Throws<ArgumentException>();
        await Assert.That(() => AdmissionIssuanceIdentityFactory.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), " ")).Throws<ArgumentException>();
    }
}
