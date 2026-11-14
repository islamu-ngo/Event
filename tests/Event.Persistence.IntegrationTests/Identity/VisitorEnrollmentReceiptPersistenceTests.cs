using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Fixture = Event.Persistence.IntegrationTests.Identity.LocalCredentialFirstUseTests.Fixture;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class VisitorEnrollmentReceiptPersistenceTests
{
    private static readonly DateTime Started = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private const string IntentDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task ForeignTenantCannotObserveOrRetargetTheOriginalReceipt()
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.Colocated);
        var options = fixture.Provider.GetRequiredService<DbContextOptions<ExploreDbContext>>();
        Guid tenant = Guid.CreateVersion7();
        Guid operation = await SeedPreparedReceiptAsync(fixture, options, tenant);
        await using (var foreign = Open(options, Guid.CreateVersion7()))
        {
            await Assert.That(await foreign.Set<VisitorEnrollmentOperation>()
                .AnyAsync(row => row.Id == operation, fixture.CancellationToken)).IsFalse();
        }

        await using var owner = Open(options, tenant);
        var receipt = await owner.Set<VisitorEnrollmentOperation>()
            .SingleAsync(row => row.Id == operation, fixture.CancellationToken);
        await Assert.That(() => { ((ITenantEntity)receipt).TenantId = Guid.CreateVersion7(); })
            .Throws<InvalidOperationException>();
        await Assert.That(receipt.TenantId).IsEqualTo(tenant);
        await Assert.That(receipt.Stage).IsEqualTo(VisitorEnrollmentStage.IdentityPrepared);
        await Assert.That(receipt.Generation).IsEqualTo(2L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdmissionAndCancellationCannotBothCommitTheSamePreparedGeneration(bool admissionWins)
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.Colocated);
        var options = fixture.Provider.GetRequiredService<DbContextOptions<ExploreDbContext>>();
        Guid tenant = Guid.CreateVersion7();
        Guid operation = await SeedPreparedReceiptAsync(fixture, options, tenant);
        Guid membership = Guid.CreateVersion7();
        await using var admitting = Open(options, tenant);
        await using var canceling = Open(options, tenant);
        var admitted = await admitting.Set<VisitorEnrollmentOperation>()
            .SingleAsync(row => row.Id == operation, fixture.CancellationToken);
        var canceled = await canceling.Set<VisitorEnrollmentOperation>()
            .SingleAsync(row => row.Id == operation, fixture.CancellationToken);

        admitted.RecordPrimaryAdmission(2, admitted.CredentialOperationId, membership, Started.AddSeconds(2));
        admitting.Set<TenantUser>().Add(new TenantUser
        {
            Id = membership,
            TenantId = tenant,
            Tenant = null!,
            UserId = fixture.Receipt.LocalSubjectId,
            User = null!,
            ActorId = fixture.Receipt.PersonalActorId,
            StatusId = (int)TenantUserStatusEnum.Active,
            CreatedAt = Started.AddSeconds(2),
            JoinedAt = Started.AddSeconds(2)
        });
        canceled.Cancel(2, canceled.CredentialOperationId, Started.AddSeconds(2));

        if (admissionWins)
        {
            await admitting.SaveChangesAsync(fixture.CancellationToken);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => canceling.SaveChangesAsync(fixture.CancellationToken));
        }
        else
        {
            await canceling.SaveChangesAsync(fixture.CancellationToken);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => admitting.SaveChangesAsync(fixture.CancellationToken));
        }

        await using var inspect = Open(options, tenant);
        var persisted = await inspect.Set<VisitorEnrollmentOperation>().AsNoTracking()
            .SingleAsync(row => row.Id == operation, fixture.CancellationToken);
        await Assert.That(persisted.Generation).IsEqualTo(3L);
        await Assert.That(persisted.Stage).IsEqualTo(admissionWins
            ? VisitorEnrollmentStage.PrimaryAdmitted : VisitorEnrollmentStage.Canceled);
        await Assert.That(persisted.TenantUserId).IsEqualTo(admissionWins ? (Guid?)membership : null);
        await Assert.That(await inspect.Set<TenantUser>().CountAsync(
            row => row.Id == membership, fixture.CancellationToken)).IsEqualTo(admissionWins ? 1 : 0);
    }

    private static async Task<Guid> SeedPreparedReceiptAsync(
        Fixture fixture, DbContextOptions<ExploreDbContext> options, Guid tenant)
    {
        await using var seed = Open(options, tenant);
        seed.Tenants.Add(new Tenant
        {
            Id = tenant,
            FullName = "Enrollment receipt tenant",
            Slug = $"enrollment-{tenant:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = Started
        });
        // This storage witness starts at prepared evidence and does not execute native credential preparation.
        var receipt = VisitorEnrollmentOperation.Reserve(Guid.CreateVersion7(), tenant,
            fixture.Receipt.LocalSubjectId, fixture.Receipt.PersonalActorId, fixture.Receipt.ExternalLoginId,
            Guid.CreateVersion7(), IntentDigest, Started);
        receipt.RecordIdentityPrepared(1, receipt.CredentialOperationId, 1, Started.AddSeconds(1));
        seed.Set<VisitorEnrollmentOperation>().Add(receipt);
        await seed.SaveChangesAsync(fixture.CancellationToken);
        return receipt.Id;
    }

    private static ExploreDbContext Open(DbContextOptions<ExploreDbContext> options, Guid tenant)
    {
        var context = new ExploreDbContext(options) { TenantContext = new SelectedTenant(tenant) };
        context.BindTenantLease();
        return context;
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
