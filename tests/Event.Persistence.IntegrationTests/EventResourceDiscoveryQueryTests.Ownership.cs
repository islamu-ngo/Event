using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceDiscoveryQueryTests
{
    [Test]
    [Arguments(ExternalApiKeyOwnerType.Organization, 0)]
    [Arguments(ExternalApiKeyOwnerType.Organization, 1)]
    [Arguments(ExternalApiKeyOwnerType.Organization, 2)]
    [Arguments(ExternalApiKeyOwnerType.Group, 0)]
    [Arguments(ExternalApiKeyOwnerType.Group, 1)]
    [Arguments(ExternalApiKeyOwnerType.Group, 2)]
    public async Task MachineProjectionCannotDeliverAfterItsActorOwnerLosesCurrentAuthority(
        ExternalApiKeyOwnerType ownerType, int mutation)
    {
        var (scope, publisher) = await SeedAsync();
        var resource = Resource(scope, publisher, 0, EventResourceAudienceKindEnum.Public);
        await SaveAsync(resource);
        var ownerId = Guid.CreateVersion7();
        await SeedActorOwnerAsync(scope.TenantAId, ownerType, ownerId);
        var key = MachineKey(scope.TenantAId, ownerType, ownerId);
        await using (var setup = database.CreateContext())
        {
            setup.ExternalApiKeys.Add(key);
            await setup.SaveChangesAsync();
        }
        var principal = new ApiKeyPrincipalContext(key.KeyId, key.TenantId, key.OwnerType, key.OwnerId, [])
        {
            CredentialSource = ApiKeyCredentialSource.Persisted
        };
        var machine = Substitute.For<IMachinePrincipalAccessor>();
        machine.IsMachineCaller.Returns(true);
        machine.Current.Returns(principal);
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, null, machinePrincipal: machine);
        var accepted = await workflow.GetAsync(resource.Id, default);
        await Assert.That(accepted.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        bool delivered = false;
        var outcome = await accepted.Projection!.DiscloseAsync(async (value, _) =>
        {
            await using var writer = database.CreateContext();
            var changed = ownerType == ExternalApiKeyOwnerType.Organization
                ? mutation switch
                {
                    0 => await writer.Organizations.Where(row => row.Id == ownerId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true)),
                    1 => await writer.OrganizationTenants.Where(row =>
                            row.TenantId == scope.TenantAId && row.OrganizationId == ownerId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true)),
                    _ => await writer.OrganizationTenants.Where(row =>
                            row.TenantId == scope.TenantAId && row.OrganizationId == ownerId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsSuspended, true))
                }
                : mutation switch
                {
                    0 => await writer.Groups.Where(row => row.Id == ownerId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true)),
                    1 => await writer.GroupTenants.Where(row =>
                            row.TenantId == scope.TenantAId && row.GroupId == ownerId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true)),
                    _ => await writer.GroupTenants.Where(row =>
                            row.TenantId == scope.TenantAId && row.GroupId == ownerId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsSuspended, true))
                };
            await Assert.That(changed).IsEqualTo(1);
            var currentKey = await writer.ExternalApiKeys
                .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
                .SingleAsync(row => row.Id == key.Id);
            await Assert.That(currentKey.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
            return value;
        }, (_, _) =>
        {
            delivered = true;
            return Task.CompletedTask;
        });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    private async Task SeedActorOwnerAsync(Guid tenantId, ExternalApiKeyOwnerType ownerType, Guid ownerId)
    {
        await using var context = database.CreateContext();
        if (ownerType == ExternalApiKeyOwnerType.Organization)
        {
            var owner = new Organization
            {
                Id = ownerId, Pii = new OrganizationPii { FullName = "Independent machine owner" },
                CreatedAt = Now, ConcurrencyStamp = Guid.CreateVersion7()
            };
            context.OrganizationTenants.Add(new OrganizationTenant
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!,
                OrganizationId = owner.Id, Organization = owner,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!,
                CreatedAt = Now, ConcurrencyStamp = Guid.CreateVersion7()
            });
        }
        else
        {
            var owner = new Group
            {
                Id = ownerId, FullName = "Independent machine owner",
                CreatedAt = Now, ConcurrencyStamp = Guid.CreateVersion7()
            };
            context.GroupTenants.Add(new GroupTenant
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!,
                GroupId = owner.Id, Group = owner,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!,
                CreatedAt = Now, ConcurrencyStamp = Guid.CreateVersion7()
            });
        }
        await context.SaveChangesAsync();
    }
}
