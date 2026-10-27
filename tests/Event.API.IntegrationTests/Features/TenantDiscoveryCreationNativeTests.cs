using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Event.Persistence.IntegrationTests;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel]
public sealed partial class TenantDiscoveryCreationNativeTests(PostgreSqlContainerFixture database)
{
    private const string ObservationReason =
        "Independent native test observer verifies exact canonical tenant-discovery creation and absence of orphan effects.";
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    public async Task AuthorizedTenantCreationCommitsExactlyOneCanonicalDiscoveryRevision()
    {
        await database.ResetAsync();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(OpenMigrator, database);
        _ = await CreateAndVerifyAsync(authentication, Request());
    }

    [Test]
    public async Task RequestedAdministratorMembershipCommitsWithTheTenantDocumentsAndRevision()
    {
        await database.ResetAsync();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(OpenMigrator, database);
        _ = await CreateAndVerifyAsync(authentication, Request(assignAdministrator: true));
    }

    [Test]
    public async Task DuplicateTenantRequestPreservesTheCommittedTenantAndDiscoveryRevision()
    {
        await database.ResetAsync();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(OpenMigrator, database);
        CreateTenantDto request = Request();
        Guid tenantId = await CreateAndVerifyAsync(authentication, request);
        var before = await ReadRevisionsAsync();
        var effectsBefore = await ReadCreationEffectsAsync();
        await using var observer = OpenMigrator();
        Guid[] tenantIdsBefore = await observer.Tenants.AsNoTracking().Select(row => row.Id).ToArrayAsync(Token);

        using var rejected = await authentication.CreateTenantViaApiAsync(request, Token);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(rejected.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(Token));
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(400);

        await Assert.That(await ReadRevisionsAsync()).IsEquivalentTo(before);
        await AssertExistingEffectsUnchangedAsync(effectsBefore);
        Guid[] tenantIdsAfter = await observer.Tenants.AsNoTracking().Select(row => row.Id).ToArrayAsync(Token);
        await Assert.That(tenantIdsAfter).IsEquivalentTo(tenantIdsBefore);
        var original = await observer.Tenants.AsNoTracking().SingleAsync(row => row.Id == tenantId, Token);
        await Assert.That(original.Slug).IsEqualTo(request.Slug);
        await Assert.That(original.FullName).IsEqualTo(request.FullName);
        await Assert.That(original.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Provisioning);
    }

    [Test]
    public Task ReplacingRequestServicesAfterCreationSaveRejectsTheOriginalAttempt() =>
        AssertLateCreationRefusalAsync(CreationFault.ReplaceRequestServices);

    [Test]
    public Task AddingAnIdentityToTheSamePrincipalAfterCreationSaveRejectsTheOriginalAttempt() =>
        AssertLateCreationRefusalAsync(CreationFault.AddPrincipalIdentity);

    [Test]
    public Task FailureAfterCreationSaveRollsBackTheTenantDocumentsAndRevision() =>
        AssertLateCreationRefusalAsync(CreationFault.FailAfterSave);

    private async Task AssertLateCreationRefusalAsync(CreationFault fault)
    {
        await database.ResetAsync();
        CreateTenantDto request = Request();
        var control = new CreationSaveControl(request.Slug);
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, control);
        var revisionsBefore = await ReadRevisionsAsync();
        var effectsBefore = await ReadCreationEffectsAsync();
        var tenantsBefore = await ReadTenantsAsync();
        HttpContext? originalRequest = null;
        IServiceProvider? originalServices = null;
        control.AfterSave = () => authentication.ObserveCurrentTenantCreationRequest(context =>
        {
            originalRequest = context;
            switch (fault)
            {
                case CreationFault.ReplaceRequestServices:
                    originalServices = context.RequestServices;
                    context.RequestServices = new DelegatingRequestServices(originalServices);
                    break;
                case CreationFault.AddPrincipalIdentity:
                    // The principal object and existing credential claims stay intact.
                    // Even an additional empty identity changes original credential custody.
                    ClaimsPrincipal principal = context.User;
                    principal.AddIdentity(new ClaimsIdentity());
                    if (!ReferenceEquals(context.User, principal))
                        throw new InvalidOperationException("The identity attack must retain the original principal.");
                    break;
                case CreationFault.FailAfterSave:
                    throw new TenantCreationSaveFaultException();
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
        });
        control.Armed = true;
        try
        {
            using var response = await authentication.CreateTenantViaApiAsync(request, Token);
            await Assert.That(control.InjectionCount).IsEqualTo(1)
                .Because(authentication.TenantCreationFailureDescription);
            await Assert.That(response.StatusCode).IsEqualTo(fault == CreationFault.FailAfterSave
                ? HttpStatusCode.InternalServerError : HttpStatusCode.Unauthorized);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            await Assert.That(fault == CreationFault.FailAfterSave
                ? authentication.ObservedTenantCreationFailure<TenantCreationSaveFaultException>()
                : authentication.ObservedTenantCreationFailure<UnauthorizedAccessException>()).IsTrue()
                .Because(authentication.TenantCreationFailureDescription);
        }
        finally
        {
            if (originalRequest is not null && originalServices is not null)
                originalRequest.RequestServices = originalServices;
        }

        await Assert.That(await ReadTenantsAsync()).IsEquivalentTo(tenantsBefore);
        await Assert.That(await ReadRevisionsAsync()).IsEquivalentTo(revisionsBefore);
        await AssertExistingEffectsUnchangedAsync(effectsBefore);
        await using var observer = OpenMigrator();
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Id == control.CreatedTenantId || row.Slug == request.Slug, Token)).IsFalse();
        await Assert.That(await observer.TenantSettingsDocuments.IgnoreTenantFilter(ObservationReason)
            .AnyAsync(row => row.TenantId == control.CreatedTenantId, Token)).IsFalse();
        await Assert.That(await observer.Set<EventDiscoveryRevision>().IgnoreTenantFilter(ObservationReason)
            .AnyAsync(row => row.TenantId == control.CreatedTenantId, Token)).IsFalse();
    }

    private async Task<Guid> CreateAndVerifyAsync(AuthenticatedSettingsFixture authentication, CreateTenantDto request)
    {
        var before = await ReadRevisionsAsync();
        var effectsBefore = await ReadCreationEffectsAsync();
        using var response = await authentication.CreateTenantViaApiAsync(request, Token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(authentication.TenantCreationFailureDescription);
        var result = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>(cancellationToken: Token);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsSuccess).IsTrue();
        Guid tenantId = result.Id;
        await Assert.That(tenantId).IsNotEqualTo(Guid.Empty);
        await Assert.That(tenantId.Version).IsEqualTo(7);

        await using var observer = OpenMigrator();
        var persisted = await observer.Tenants.AsNoTracking().SingleAsync(row => row.Id == tenantId, Token);
        await Assert.That(persisted.Slug).IsEqualTo(request.Slug);
        await Assert.That(persisted.FullName).IsEqualTo(request.FullName);
        await Assert.That(persisted.CreatedBy).IsEqualTo(authentication.ActorId);
        await Assert.That(persisted.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Provisioning);
        await Assert.That(await observer.Tenants.CountAsync(row => row.Slug == request.Slug, Token)).IsEqualTo(1);
        var after = await ReadRevisionsAsync();
        RevisionSnapshot[] created = after.Where(row => row.TenantId == tenantId).ToArray();
        await Assert.That(created.Length).IsEqualTo(1);
        await Assert.That(created[0].Id.Version).IsEqualTo(7);
        await Assert.That(created[0].IdentityEpoch).IsEqualTo(0L);
        await Assert.That(created[0].DisclosureEpoch).IsEqualTo(1L);
        await Assert.That(after.Where(row => row.TenantId != tenantId)).IsEquivalentTo(before);
        await Assert.That(await observer.Set<EventDiscoveryRevision>().IgnoreTenantFilter(ObservationReason)
            .AnyAsync(revision => !observer.Tenants.Any(tenant => tenant.Id == revision.TenantId), Token)).IsFalse();
        await VerifyCreationEffectsAsync(authentication, request, tenantId);
        await AssertExistingEffectsUnchangedAsync(effectsBefore, tenantId);
        return tenantId;
    }

    private async Task VerifyCreationEffectsAsync(
        AuthenticatedSettingsFixture authentication, CreateTenantDto request, Guid tenantId)
    {
        await using var observer = OpenMigrator();
        var documents = await observer.TenantSettingsDocuments.IgnoreTenantFilter(ObservationReason).AsNoTracking()
            .Where(row => row.TenantId == tenantId).ToListAsync(Token);
        await Assert.That(documents.Select(row => row.DocumentKey)).IsEquivalentTo(new[]
        {
            SettingsDocumentKeys.Tenant.Branding,
            SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity
        });
        foreach (var document in documents)
        {
            await Assert.That(document.Id.Version).IsEqualTo(7);
            await Assert.That(document.SchemaVersion).IsEqualTo(1);
            await Assert.That(document.CreatedBy).IsEqualTo(authentication.ActorId);
            using var payload = JsonDocument.Parse(document.PayloadJson);
            string nameProperty = document.DocumentKey == SettingsDocumentKeys.Tenant.Branding
                ? "displayName" : "publicName";
            await Assert.That(payload.RootElement.GetProperty(nameProperty).GetString()).IsEqualTo(request.FullName);
        }

        var members = await observer.Set<TenantUser>().IgnoreTenantFilter(ObservationReason).AsNoTracking()
            .Where(row => row.TenantId == tenantId).ToListAsync(Token);
        var grants = await observer.Set<TenantUserRoleGrant>().IgnoreTenantFilter(ObservationReason).AsNoTracking()
            .Where(row => row.TenantId == tenantId).ToListAsync(Token);
        await Assert.That(members.Count).IsEqualTo(request.AssignCurrentUserAsTenantAdmin ? 1 : 0);
        await Assert.That(grants.Count).IsEqualTo(request.AssignCurrentUserAsTenantAdmin ? 1 : 0);
        if (request.AssignCurrentUserAsTenantAdmin)
        {
            var member = members.Single();
            var grant = grants.Single();
            await Assert.That(member.UserId).IsEqualTo(authentication.ActorId);
            await Assert.That(member.StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
            await Assert.That(member.IsDeleted).IsFalse();
            await Assert.That(grant.TenantUserId).IsEqualTo(member.Id);
            await Assert.That(grant.RoleScopeId).IsEqualTo((int)RoleScopeEnum.Tenant);
            await Assert.That(grant.GrantedBy).IsEqualTo(authentication.ActorId);
            await Assert.That(grant.RevokedAt).IsNull();
            await Assert.That(await observer.Roles.Where(row => row.Id == grant.RoleId)
                .Select(row => row.MasterCode).SingleAsync(Token)).IsEqualTo("tenant.admin");
        }
    }

    private async Task<CreationEffectsSnapshot> ReadCreationEffectsAsync()
    {
        await using var observer = OpenMigrator();
        return new(
            await observer.TenantSettingsDocuments.IgnoreTenantFilter(ObservationReason).AsNoTracking()
                .Select(row => new DocumentSnapshot(row.Id, row.TenantId, row.DocumentKey,
                    row.SchemaVersion, row.DefaultsVersion, row.PayloadJson, row.ConcurrencyStamp,
                    row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).ToArrayAsync(Token),
            await observer.Set<TenantUser>().IgnoreTenantFilter(ObservationReason).AsNoTracking()
                .Select(row => new MembershipSnapshot(row.Id, row.TenantId, row.UserId,
                    row.StatusId, row.IsDeleted, row.UpdatedAt)).ToArrayAsync(Token),
            await observer.Set<TenantUserRoleGrant>().IgnoreTenantFilter(ObservationReason).AsNoTracking()
                .Select(row => new GrantSnapshot(row.Id, row.TenantId, row.TenantUserId,
                    row.RoleId, row.RoleScopeId, row.GrantedBy, row.RevokedAt, row.UpdatedAt)).ToArrayAsync(Token));
    }

    private async Task AssertExistingEffectsUnchangedAsync(CreationEffectsSnapshot before, Guid? createdTenantId = null)
    {
        var after = await ReadCreationEffectsAsync();
        await Assert.That(after.Documents.Where(row => row.TenantId != createdTenantId)).IsEquivalentTo(before.Documents);
        await Assert.That(after.Memberships.Where(row => row.TenantId != createdTenantId)).IsEquivalentTo(before.Memberships);
        await Assert.That(after.Grants.Where(row => row.TenantId != createdTenantId)).IsEquivalentTo(before.Grants);
    }

    private async Task<List<RevisionSnapshot>> ReadRevisionsAsync()
    {
        await using var observer = OpenMigrator();
        return await observer.Set<EventDiscoveryRevision>().IgnoreTenantFilter(ObservationReason).AsNoTracking()
            .Select(row => new RevisionSnapshot(row.Id, row.TenantId, row.IdentityEpoch, row.DisclosureEpoch))
            .ToListAsync(Token);
    }

    private async Task<TenantSnapshot[]> ReadTenantsAsync()
    {
        await using var observer = OpenMigrator();
        return await observer.Tenants.AsNoTracking()
            .Select(row => new TenantSnapshot(row.Id, row.Slug, row.FullName, row.TenantStatusId,
                row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).ToArrayAsync(Token);
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, database.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private static CreateTenantDto Request(bool assignAdministrator = false) => new()
    {
        FullName = "Native discovery creation",
        Slug = $"native-discovery-{Guid.CreateVersion7():N}",
        IsActive = false,
        AssignCurrentUserAsTenantAdmin = assignAdministrator
    };

    private sealed record RevisionSnapshot(Guid Id, Guid TenantId, long IdentityEpoch, long DisclosureEpoch);
    private sealed record DocumentSnapshot(Guid Id, Guid TenantId, string Key, int SchemaVersion,
        string DefaultsVersion, string PayloadJson, Guid ConcurrencyStamp, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);
    private sealed record MembershipSnapshot(Guid Id, Guid TenantId, Guid UserId, int StatusId, bool IsDeleted, DateTime? UpdatedAt);
    private sealed record GrantSnapshot(Guid Id, Guid TenantId, Guid TenantUserId, int RoleId, int RoleScopeId,
        Guid? GrantedBy, DateTime? RevokedAt, DateTime? UpdatedAt);
    private sealed record CreationEffectsSnapshot(DocumentSnapshot[] Documents, MembershipSnapshot[] Memberships, GrantSnapshot[] Grants);
    private sealed record TenantSnapshot(Guid Id, string Slug, string FullName, int StatusId,
        DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);
    private enum CreationFault { ReplaceRequestServices, AddPrincipalIdentity, FailAfterSave }
    private sealed class TenantCreationSaveFaultException : Exception { }
    private sealed class DelegatingRequestServices(IServiceProvider original) : IServiceProvider
    {
        public object? GetService(Type serviceType) => original.GetService(serviceType);
    }

    private sealed class CreationSaveControl(string slug) : SaveChangesInterceptor
    {
        private ExploreDbContext? owner;
        private DbContextId ownerId;
        private DbConnection? connection;
        private DbTransaction? transaction;
        internal bool Armed { get; set; }
        internal Action? AfterSave { get; set; }
        internal int InjectionCount { get; private set; }
        internal Guid CreatedTenantId { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context
                || context.ChangeTracker.Entries<Tenant>().SingleOrDefault(entry =>
                    entry.State == EntityState.Added && entry.Entity.Slug == slug) is not { } tenant)
                return ValueTask.FromResult(result);
            if (owner is not null)
                throw new InvalidOperationException("The creation fault must observe exactly one original save.");
            owner = context;
            ownerId = context.ContextId;
            connection = context.Database.GetDbConnection();
            transaction = context.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("The creation save requires its existing owner transaction.");
            CreatedTenantId = tenant.Entity.Id;
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || owner is null || !ReferenceEquals(eventData.Context, owner))
                return result;
            await Assert.That(owner.ContextId).IsEqualTo(ownerId);
            await Assert.That(ReferenceEquals(owner.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(ReferenceEquals(owner.Database.CurrentTransaction?.GetDbTransaction(), transaction)).IsTrue();
            await Assert.That(ReferenceEquals(transaction!.Connection, connection)).IsTrue();
            await Assert.That(transaction.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
            await Assert.That(owner.TenantFilterTenantId).IsNull();
            await Assert.That(owner.IsTenantFilterBypassed).IsFalse();
            await Assert.That(owner.ChangeTracker.Entries<TenantSettingsDocument>().Count(entry =>
                entry.Entity.TenantId == CreatedTenantId && entry.State == EntityState.Unchanged)).IsEqualTo(2);
            await Assert.That(owner.ChangeTracker.Entries<EventDiscoveryRevision>().Count(entry =>
                entry.Entity.TenantId == CreatedTenantId && entry.State == EntityState.Unchanged)).IsEqualTo(1);

            // Use only the creator's approved Tenant identity columns to prove that
            // the row was written inside this transaction, before triggering refusal.
            var written = await owner.Tenants.AsNoTracking().Where(row => row.Id == CreatedTenantId)
                .Select(row => new { row.Id, row.Slug, row.TenantStatusId }).SingleAsync(cancellationToken);
            await Assert.That(written.Slug).IsEqualTo(slug);
            await Assert.That(written.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Provisioning);
            Armed = false;
            InjectionCount++;
            (AfterSave ?? throw new InvalidOperationException("The creation fault was not armed."))();
            return result;
        }
    }
}
