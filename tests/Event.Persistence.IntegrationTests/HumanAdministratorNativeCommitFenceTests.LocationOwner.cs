using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Http;

namespace Event.Persistence.IntegrationTests;

public sealed partial class HumanAdministratorNativeCommitFenceTests
{
    [Test]
    public async Task LocationNamedLeaseOwnerAdmitsCurrentHumanFenceOnOriginalRestrictedConnection()
    {
        var rows = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, rows.Tenant);
        var scope = await ScopeAsync(context, rows.User, HumanAdministratorCommitPurpose.RuntimeTenant);
        IHumanSubjectCommitAuthority authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
        var locks = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        await Assert.That(await locks.ExecuteAsync(
            GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress,
            token => authority.IsSubjectCurrentForCommitAsync(rows.User, token))).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }
}
