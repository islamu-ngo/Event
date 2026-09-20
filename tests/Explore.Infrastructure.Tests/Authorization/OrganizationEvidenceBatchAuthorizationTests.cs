using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Authorization;

public sealed class OrganizationEvidenceBatchAuthorizationTests
{
    private static readonly Guid TenantId = Guid.Parse("019d2f35-47d8-7b2d-96d3-570cc42f8c11");
    private static readonly Guid OrganizationId = Guid.Parse("019d2f35-47d8-7b2d-96d3-570cc42f8c12");
    private static readonly Guid ForeignTenantId = Guid.Parse("019d2f35-47d8-7b2d-96d3-570cc42f8c13");
    private static readonly Guid OtherOrganizationId = Guid.Parse("019d2f35-47d8-7b2d-96d3-570cc42f8c14");

    [Test]
    [Arguments("organization", true, true, false)]
    [Arguments("tenant", false, true, true)]
    [Arguments("combined", true, true, true)]
    [Arguments("foreign", false, false, false)]
    [Arguments("unrelated-organization", false, false, false)]
    [Arguments("none", false, false, false)]
    public async Task EvidenceDecisionsDoNotDependOnBatchSizeOrNeighboringCapabilities(
        string membership, bool canSubmit, bool canView, bool canReview)
    {
        IAuthorizationProvider provider = CreateProvider(membership);
        var facts = new OrganizationAuthorizationFacts(TenantId, OrganizationId);
        var scope = new AuthorizationScope(TenantId: TenantId.ToString("D"));
        AuthorizationRequest[] evidence =
        [
            new(ResourceKinds.Organization, OrganizationId.ToString("D"),
                AuthorizationActions.Organizations.SubmitEvidence, scope, facts),
            new(ResourceKinds.Organization, OrganizationId.ToString("D"),
                AuthorizationActions.Organizations.ViewEvidence, scope, facts),
            new(ResourceKinds.Organization, OrganizationId.ToString("D"),
                AuthorizationActions.Organizations.ReviewEvidence, scope, facts)
        ];
        bool[] expected = [canSubmit, canView, canReview];

        for (var index = 0; index < evidence.Length; index++)
        {
            var single = await provider.AuthorizeAsync(evidence[index]);
            await Assert.That(single.IsAllowed).IsEqualTo(expected[index]);
            var next = (index + 1) % evidence.Length;
            var pair = await provider.AuthorizeBatchAsync([evidence[index], evidence[next]]);
            await Assert.That(pair.Count).IsEqualTo(2);
            await Assert.That(pair[0].IsAllowed).IsEqualTo(expected[index]);
            await Assert.That(pair[1].IsAllowed).IsEqualTo(expected[next]);
        }

        var triple = await provider.AuthorizeBatchAsync(evidence);
        for (var index = 0; index < evidence.Length; index++)
            await Assert.That(triple[index].IsAllowed).IsEqualTo(expected[index]);

        // Non-evidence organization actions retain their existing profile semantics.
        var update = new AuthorizationRequest(ResourceKinds.Organization, OrganizationId.ToString("D"),
            AuthorizationActions.Organizations.Update, scope, facts);
        var unrelated = new AuthorizationRequest(ResourceKinds.InstanceSetting, "instance-setting",
            AuthorizationActions.InstanceSettings.Update, Facts: new InstanceScopedAuthorizationFacts());
        var mixed = await provider.AuthorizeBatchAsync(
            [unrelated, evidence[2], update, evidence[0], evidence[1], unrelated]);
        bool[] mixedExpected = [false, canReview, canView, canSubmit, canView, false];
        await Assert.That(mixed.Count).IsEqualTo(mixedExpected.Length);
        for (var index = 0; index < mixedExpected.Length; index++)
            await Assert.That(mixed[index].IsAllowed).IsEqualTo(mixedExpected[index]);

        foreach (var request in evidence)
        {
            await Assert.That(ReferenceEquals(request.Facts, facts)).IsTrue();
            await Assert.That(ReferenceEquals(request.Scope, scope)).IsTrue();
        }
        await Assert.That(facts.TenantId).IsEqualTo(TenantId);
        await Assert.That(facts.OrganizationId).IsEqualTo(OrganizationId);
    }

    [Test]
    public async Task SafeModeDeniesEvidenceInMixedBatches()
    {
        var provider = CreateProvider("combined");
        provider.ActivateSafeMode();
        var facts = new OrganizationAuthorizationFacts(TenantId, OrganizationId);
        var checks = new[]
        {
            AuthorizationActions.Organizations.SubmitEvidence,
            AuthorizationActions.Organizations.ViewEvidence,
            AuthorizationActions.Organizations.ReviewEvidence,
            AuthorizationActions.Organizations.Update
        }.Select(action => new AuthorizationRequest(
            ResourceKinds.Organization, OrganizationId.ToString("D"), action, Facts: facts)).ToArray();

        var decisions = await provider.AuthorizeBatchAsync(checks);
        await Assert.That(decisions.Count).IsEqualTo(4);
        await Assert.That(decisions.All(decision => !decision.IsAllowed)).IsTrue();
    }

    private static FallbackAuthorizationService CreateProvider(string membership)
    {
        // Supply authority facts, never authorization decisions. Both provider algorithms execute.
        var admin = Substitute.For<IAdminContext>();
        admin.UserId.Returns(Guid.Parse("019d2f35-47d8-7b2d-96d3-570cc42f8c15"));
        admin.IsTenantAdminAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(membership is "tenant" or "combined");
        admin.IsTenantAdminAsync(ForeignTenantId, Arg.Any<CancellationToken>())
            .Returns(membership == "foreign");
        admin.IsOrganizationAdminAsync(OrganizationId, Arg.Any<CancellationToken>())
            .Returns(membership is "organization" or "combined");
        admin.IsOrganizationAdminAsync(OtherOrganizationId, Arg.Any<CancellationToken>())
            .Returns(membership == "unrelated-organization");
        admin.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns(
            membership is "organization" or "combined" ? [OrganizationId]
            : membership == "unrelated-organization" ? [OtherOrganizationId] : []);
        admin.GetAdminGroupIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return new FallbackAuthorizationService(
            admin,
            Substitute.For<IMachinePrincipalAccessor>(),
            Substitute.For<IEventAuthoritySnapshotService>(),
            Substitute.For<IOrganizationMemberRepository>(),
            Substitute.For<IGroupMemberRepository>(),
            Substitute.For<IHierarchicalSettingsResolver>(),
            tenant,
            NullLogger<FallbackAuthorizationService>.Instance);
    }
}
