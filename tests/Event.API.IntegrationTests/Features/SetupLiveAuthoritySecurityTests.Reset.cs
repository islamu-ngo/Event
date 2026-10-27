namespace Event.Api.IntegrationTests.Features;

using System.Net;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

public sealed partial class SetupLiveAuthoritySecurityTests
{
    [Test]
    public async Task ResetRecreatesDistinctPrincipalsAndCommittedMembershipsWithCurrentLookupScalars()
    {
        await fixture.ResetAsync();
        await AssertResetState();
        Guid[] previousUsers = [fixture.Primary.UserId, fixture.Secondary.UserId];
        Guid[] previousActors = [fixture.Primary.ActorId, fixture.Secondary.ActorId];
        Guid bindingId = await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        using HttpRequestMessage write = SecretWriteRequest(enrollment, "setup.signing", NewLongToken());
        using HttpResponseMessage response = await fixture.Client.SendAsync(write);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted)
            .Because(fixture.Capture.FailureMetadata);
        await AssertEntityCount(EnrollmentEntityName, 1);
        await AssertEntityCount(IssuanceClaimEntityName, 1);
        await AssertEntityCount(OperationEntityName, 1);

        await fixture.ResetAsync();
        await AssertResetState();
        await using ExploreDbContext db = fixture.Factory.CreateMigratorContext();
        await Assert.That(await db.Users.AnyAsync(user => previousUsers.Contains(user.Id))).IsFalse();
        await Assert.That(await db.Set<UserPii>().AnyAsync(pii => previousUsers.Contains(pii.UserId))).IsFalse();
        await Assert.That(await db.Actors.AnyAsync(actor => previousActors.Contains(actor.Id))).IsFalse();
        await Assert.That(await db.Set<ActorPii>().AnyAsync(pii => previousActors.Contains(pii.ActorId))).IsFalse();
        await Assert.That(await db.TenantUsers.AnyAsync(member => previousUsers.Contains(member.UserId))).IsFalse();
        await Assert.That(await db.SecretBindings.AnyAsync(binding => binding.Id == bindingId)).IsFalse();
        await AssertEntityCount(EnrollmentEntityName, 0);
        await AssertEntityCount(IssuanceClaimEntityName, 0);
        await AssertEntityCount(OperationEntityName, 0);
    }

    private async Task AssertResetState()
    {
        await using ExploreDbContext db = fixture.Factory.CreateMigratorContext();
        await Assert.That(fixture.Primary.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(fixture.Secondary.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(fixture.Primary.UserId == fixture.Secondary.UserId).IsFalse();
        await Assert.That(fixture.Primary.ActorId == fixture.Secondary.ActorId).IsFalse();
        await Assert.That(await db.Tenants.CountAsync()).IsEqualTo(1);
        await Assert.That(await db.Tenants.AnyAsync(tenant => tenant.Id == PlatformDefaults.DefaultTenantId
            && tenant.TenantStatusId == (int)TenantStatusEnum.Active)).IsTrue();

        User[] users = await db.Users.Include(user => user.Pii).AsNoTracking().ToArrayAsync();
        Actor[] actors = await db.Actors.Include(actor => actor.Pii).AsNoTracking().ToArrayAsync();
        TenantUser[] members = await db.TenantUsers.AsNoTracking().ToArrayAsync();
        await Assert.That(users.Select(user => user.Id))
            .IsEquivalentTo(new[] { fixture.Primary.UserId, fixture.Secondary.UserId });
        await Assert.That(actors.Select(actor => actor.Id))
            .IsEquivalentTo(new[] { fixture.Primary.ActorId, fixture.Secondary.ActorId });
        await Assert.That(await db.Set<UserPii>().CountAsync()).IsEqualTo(2);
        await Assert.That(await db.Set<ActorPii>().CountAsync()).IsEqualTo(2);
        await Assert.That(users.Select(user => user.Pii.Email).Distinct().Count()).IsEqualTo(2);
        await Assert.That(members.Length).IsEqualTo(2);
        await Assert.That(members.Select(member => member.Id).Distinct().Count()).IsEqualTo(2);
        foreach (var scenario in new[] { fixture.Primary, fixture.Secondary })
        {
            User user = users.Single(value => value.Id == scenario.UserId);
            Actor actor = actors.Single(value => value.Id == scenario.ActorId);
            TenantUser member = members.Single(value => value.UserId == scenario.UserId);
            await Assert.That(user.Pii.UserId).IsEqualTo(user.Id);
            await Assert.That(string.IsNullOrWhiteSpace(user.Pii.Email)).IsFalse();
            await Assert.That(user.Pii.FirstName).IsEqualTo("Test");
            await Assert.That(user.Pii.LastName).IsEqualTo("User");
            await Assert.That(user.IsDeleted).IsFalse();
            await Assert.That(actor.Pii.ActorId).IsEqualTo(actor.Id);
            await Assert.That(actor.Pii.DisplayName).IsEqualTo("Test Actor");
            await Assert.That(actor.UserId).IsEqualTo((Guid?)user.Id);
            await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.User);
            await Assert.That(actor.IsDeleted || actor.IsSuspended).IsFalse();
            await Assert.That(member.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
            await Assert.That(member.ActorId).IsEqualTo((Guid?)actor.Id);
            await Assert.That(member.StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
            await Assert.That(member.JoinedAt).IsNotNull();
            await Assert.That(member.IsDeleted).IsFalse();
        }

        // Tenant creation emits 1; separately committed memberships must each advance disclosure.
        EventDiscoveryRevision revision = await db.Set<EventDiscoveryRevision>().AsNoTracking().SingleAsync();
        await Assert.That(revision.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(0L);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(3L);

        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        ILookupDataCache cache = scope.ServiceProvider.GetRequiredService<ILookupDataCache>();
        await AssertCachedLookupScalars<EventType>(db, cache);
        await AssertCachedLookupScalars<EventFormat>(db, cache);
        await AssertCachedLookupScalars<EventStatus>(db, cache);
        await AssertCachedLookupScalars<AudienceAge>(db, cache);
        await AssertCachedLookupScalars<AudienceGender>(db, cache);
        await AssertCachedLookupScalars<Madhab>(db, cache);
        await AssertCachedLookupScalars<Language>(db, cache);
        await AssertCachedLookupScalars<VisibilityType>(db, cache);
        await AssertCachedLookupScalars<ApprovalStatus>(db, cache);
        await AssertCachedLookupScalars<RegistrationMode>(db, cache);
        await AssertCachedLookupScalars<Role>(db, cache);
        await AssertCachedLookupScalars<OrganizationPosition>(db, cache);
        await AssertCachedLookupScalars<ActorType>(db, cache);
        await AssertCachedLookupScalars<DidCustodyType>(db, cache);
        await AssertCachedLookupScalars<FileType>(db, cache);
        await AssertCachedLookupScalars<TagType>(db, cache);
        await AssertCachedLookupScalars<OwnerType>(db, cache);
        await Assert.That(await db.Set<OwnerType>().AnyAsync()).IsFalse();
        await Assert.That(cache.Get<OwnerType>().Count).IsEqualTo(0);
    }

    private static async Task AssertCachedLookupScalars<T>(ExploreDbContext db, ILookupDataCache cache)
        where T : class
    {
        T[] rows = await db.Set<T>().ToArrayAsync();
        var cached = cache.Get<T>();
        await Assert.That(cached.Keys).IsEquivalentTo(rows.Select(row => db.Entry(row).Property<int>("Id").CurrentValue));
        foreach (T row in rows)
        {
            PropertyValues persisted = db.Entry(row).CurrentValues;
            PropertyValues snapshot = db.Entry(cached[(int)persisted["Id"]!]).CurrentValues;
            foreach (IProperty property in persisted.Properties)
            {
                bool equal = property.GetValueComparer()?.Equals(persisted[property], snapshot[property])
                    ?? Equals(persisted[property], snapshot[property]);
                await Assert.That(equal).IsTrue().Because($"{typeof(T).Name}.{property.Name}");
            }
        }
    }
}
