// ABOUTME: Red real-provider matrix for bounded, deterministic, tenant-safe local address suggestions.
// ABOUTME: Requires authorization predicates before narrow exact projection with cancellation and no tracking.

using System.Data.Common;
using System.Reflection;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Queries;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Assertions.Enums;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class LocalAddressSuggestionQueryTests(PostgreSqlContainerFixture fixture)
{
    private const string CriteriaName = "Explore.Application.Contracts.Persistence.LocalAddressSuggestionCriteria";
    private const string ResultName = "Explore.Application.Contracts.Persistence.LocalAddressSuggestion";
    private const string InterfaceName = "Explore.Application.Contracts.Persistence.ILocalAddressSuggestionQuery";
    private const string SourceTypeName = "Explore.Domain.Enums.LocationAddressSourceEnum";
    private const string VisibilityTypeName = "Explore.Domain.Enums.LocationAddressVisibilityEnum";
    private static readonly Guid TenantId = Id(1);
    private static readonly Guid ForeignTenantId = Id(2);
    private static readonly Guid ActorId = Id(3);
    private static readonly Guid OtherActorId = Id(4);
    private static readonly Guid UserId = Id(26);
    private static readonly Guid OrganizationId = Id(5);
    private static readonly Guid OtherOrganizationId = Id(6);
    private static readonly Guid ApprovedId = Id(10);
    private static readonly Guid CreatorId = Id(11);
    private static readonly Guid OrganizationScopedId = Id(12);
    private static readonly Guid CrossTenantId = Id(13);
    private static readonly Guid OtherOrganizationIdCanary = Id(14);
    private static readonly Guid OtherCreatorIdCanary = Id(15);
    private static readonly Guid QuarantinedId = Id(16);
    private static readonly Guid ErasedId = Id(17);
    private static readonly Guid PrivateHomeId = Id(18);
    private static readonly Guid NonMatchingId = Id(19);
    private static readonly Guid NotProvidedWithPiiId = Id(20);
    private static readonly Guid PendingOrganizationIdCanary = Id(21);
    private static readonly Guid SuspendedOrganizationIdCanary = Id(22);
    private static readonly Guid RevokedOrganizationIdCanary = Id(23);
    private static readonly Guid DeletedOrganizationIdCanary = Id(24);
    private static readonly Guid SoftDeletedMembershipIdCanary = Id(27);
    private static readonly Guid GloballyDeletedOrganizationIdCanary = Id(28);
    private static readonly Guid CrossTenantMembershipIdCanary = Id(29);
    private static readonly Guid UnicodePrefixId = Id(101);
    private static readonly Guid UnicodeLongerPrefixId = Id(102);
    private static readonly Guid UnicodeBmpId = Id(103);
    private static readonly Guid UnicodeTieFirstId = Id(104);
    private static readonly Guid UnicodeTieSecondId = Id(105);
    private static readonly Guid ScalarBoundaryCanaryId = Id(106);
    private static readonly Guid ScalarBoundaryExactId = Id(107);
    private static readonly Guid UnicodeLegacyId = Id(108);

    [Test]
    public async Task PublicContractIsBoundedCancellableAndUsesGlobalOrganizationIdentity()
    {
        Type contract = RequiredApplicationType(InterfaceName);
        Type criteria = RequiredApplicationType(CriteriaName);
        Type result = RequiredApplicationType(ResultName);
        MethodInfo search = contract.GetMethod("SearchAsync", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Local suggestion contract requires SearchAsync.");

        await Assert.That(search.GetParameters().Select(parameter => parameter.ParameterType))
            .IsEquivalentTo([criteria, typeof(CancellationToken)]);
        await Assert.That(RequiredPublicProperty(criteria, "TenantId").PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(RequiredPublicProperty(criteria, "ActorId").PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(RequiredPublicProperty(criteria, "UserId").PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(RequiredPublicProperty(criteria, "OrganizationId").PropertyType).IsEqualTo(typeof(Guid?));
        await Assert.That(RequiredPublicProperty(criteria, "SearchText").PropertyType).IsEqualTo(typeof(string));
        await Assert.That(RequiredPublicProperty(criteria, "Limit").PropertyType).IsEqualTo(typeof(int));
        await Assert.That(RequiredPublicProperty(result, "LocationId").PropertyType).IsEqualTo(typeof(Guid));
        PropertyInfo display = result.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance)
            ?? result.GetProperty("FullName", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Local suggestion result requires one public display-name field.");
        await Assert.That(display.PropertyType).IsEqualTo(typeof(string));
        await Assert.That(RequiredPublicProperty(result, "Address").PropertyType).IsEqualTo(typeof(string));
        await Assert.That(RequiredPublicProperty(result, "Postcode").PropertyType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task PostgreSqlLaneUsesRealRelationalExecution()
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        int value = await context.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync();
        await Assert.That(context.Database.ProviderName ?? string.Empty).Contains("Npgsql");
        await Assert.That(value).IsEqualTo(1);
    }

    [Test]
    public async Task SqliteLaneUsesRealRelationalExecution()
    {
        string path = DatabasePath("control");
        DeleteSqliteFiles(path);
        try
        {
            await using ExploreDbContext context = CreateSqliteContext(path);
            await context.Database.EnsureCreatedAsync();
            int value = await context.Database.SqlQueryRaw<int>("SELECT 1 AS Value").SingleAsync();
            await Assert.That(context.Database.ProviderName ?? string.Empty).Contains("Sqlite");
            await Assert.That(value).IsEqualTo(1);
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    [Test]
    public async Task PostgreSqlSeededMatrixReturnsOnlyExactEligibleRowsInStableOrder()
    {
        _ = RequiredApplicationType(InterfaceName);
        await fixture.ResetAsync();
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            await SeedMatrixAsync(seed);
        }

        var interceptor = new SelectCaptureInterceptor();
        await using ExploreDbContext queryContext = CreatePostgreSqlContext(interceptor);
        await AssertMatrixAsync(queryContext, interceptor);
    }

    [Test]
    public async Task SqliteSeededMatrixReturnsOnlyExactEligibleRowsInStableOrder()
    {
        _ = RequiredApplicationType(InterfaceName);
        string path = DatabasePath("matrix");
        DeleteSqliteFiles(path);
        try
        {
            var interceptor = new SelectCaptureInterceptor();
            await using ExploreDbContext context = CreateSqliteContext(path, interceptor);
            await context.Database.EnsureCreatedAsync();
            context.EnableTenantFilterBypass("Seed deterministic local-address matrix.");
            await LookupTableSeeder.SeedAsync(context);
            await SeedMatrixAsync(context);
            context.TenantContext = new TestTenantContext(TenantId);
            interceptor.SelectCommands.Clear();

            await AssertMatrixAsync(context, interceptor);
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    [Test]
    public async Task PostgreSqlUnicodeCorpusUsesCanonicalLiteralBoundaryAndOrdinalOrdering()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        await SeedUnicodeCorpusAsync(context);
        await AssertUnicodeCorpusAsync(context);
    }


    [Test]
    public async Task TrustedActorAndUserIdentitiesMustBeNonemptyBeforeExecution()
    {
        _ = RequiredApplicationType(InterfaceName);
        var interceptor = new SelectCaptureInterceptor();
        await using ExploreDbContext context = CreatePostgreSqlContext(interceptor);

        await Assert.ThrowsAsync<ArgumentException>(() => InvokeSearchAsync(
            context,
            new SearchInput(TenantId, Guid.Empty, UserId, OrganizationId, "Synthetic Matrix Address", 2),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => InvokeSearchAsync(
            context,
            new SearchInput(TenantId, ActorId, Guid.Empty, OrganizationId, "Synthetic Matrix Address", 2),
            CancellationToken.None));
        await Assert.That(interceptor.SelectCommands).IsEmpty();
    }

    [Test]
    public async Task CancellationBeforeExecutionIssuesNoCommand()
    {
        _ = RequiredApplicationType(InterfaceName);
        var interceptor = new SelectCaptureInterceptor();
        await using ExploreDbContext context = CreatePostgreSqlContext(interceptor);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => InvokeSearchAsync(
            context,
            new SearchInput(TenantId, ActorId, UserId, OrganizationId, "Synthetic Matrix Address", 2),
            cancellation.Token));
        await Assert.That(interceptor.SelectCommands).IsEmpty();
    }

    private static async Task AssertMatrixAsync(ExploreDbContext context, SelectCaptureInterceptor interceptor)
    {
        interceptor.SelectCommands.Clear();
        IReadOnlyList<object> results = await InvokeSearchAsync(
            context,
            new SearchInput(TenantId, ActorId, UserId, OrganizationId, "Synthetic Matrix Address", 3),
            CancellationToken.None);

        Guid[] ids = results.Select(result => RequiredGuid(result, "LocationId")).ToArray();
        await Assert.That(ids).IsEquivalentTo([ApprovedId, CreatorId, OrganizationScopedId], CollectionOrdering.Matching);
        await Assert.That(results.All(result =>
            RequiredString(result, "Address") == "Synthetic Matrix Address")).IsTrue();
        await Assert.That(results.All(result =>
            RequiredString(result, "Postcode") == "0000")).IsTrue();
        await Assert.That(results.Select(RequiredDisplayName))
            .IsEquivalentTo(["Display Alpha", "Display Beta", "Display Gamma"], CollectionOrdering.Matching);

        Guid[] forbidden =
        [
            CrossTenantId,
            OtherOrganizationIdCanary,
            OtherCreatorIdCanary,
            QuarantinedId,
            ErasedId,
            PrivateHomeId,
            NonMatchingId,
            NotProvidedWithPiiId,
            PendingOrganizationIdCanary,
            SuspendedOrganizationIdCanary,
            RevokedOrganizationIdCanary,
            DeletedOrganizationIdCanary,
            SoftDeletedMembershipIdCanary,
            GloballyDeletedOrganizationIdCanary,
            CrossTenantMembershipIdCanary
        ];
        await Assert.That(ids.Intersect(forbidden)).IsEmpty();
        await Assert.That(interceptor.SelectCommands).HasSingleItem();
        string sql = interceptor.SelectCommands.Single();
        (string projection, string predicate) = SplitSelectAndWhere(sql);
        foreach (string authorityColumn in new[]
        {
            "tenant_id", "created_by", "address_organization_id", "user_id",
            "organization_tenant_id", "organization_id", "approval_status_id", "is_suspended", "is_deleted"
        })
        {
            await Assert.That(predicate).Contains(authorityColumn);
            await Assert.That(projection).DoesNotContain(authorityColumn);
        }
        foreach (string resultColumn in new[]
        {
            "id", "concurrency_stamp", "full_name", "address", "postcode",
            "address_source_id", "address_visibility_id", "country", "city", "timezone"
        })
        {
            await Assert.That(projection).Contains(resultColumn);
        }
        foreach (string forbiddenColumn in new[]
        {
            "latitude", "longitude", "owner_user_id", "pii_erased_at_utc",
            "location_kind_id", "location_privacy_state_id", "created_at", "updated_at",
            "address_substring_key", "address_substring_key_version", "display_sort_key", "display_sort_key_version"
        })
        {
            await Assert.That(projection).DoesNotContain(forbiddenColumn);
        }
        foreach (string keyColumn in new[]
        {
            "address_substring_key", "address_substring_key_version", "display_sort_key", "display_sort_key_version"
        })
        {
            await Assert.That(predicate + sql[sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase)..].ToLowerInvariant())
                .Contains(keyColumn);
        }
        await Assert.That(sql).Contains("location_pii");
        await Assert.That(sql).Contains("organization_members");
        await Assert.That(sql).Contains("organization_tenants");
        await Assert.That(sql).Contains("organizations");
        await Assert.That(sql).DoesNotContain("user_pii");
        await Assert.That(sql).DoesNotContain("organization_pii");
        await Assert.That(sql.ToUpperInvariant().Split("EXISTS").Length - 1).IsEqualTo(1);
        await Assert.That(sql).Contains("ORDER BY");
        await Assert.That(sql).Contains("LIMIT");
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();

        interceptor.SelectCommands.Clear();
        IReadOnlyList<object> limited = await InvokeSearchAsync(
            context,
            new SearchInput(TenantId, ActorId, UserId, OrganizationId, "Synthetic Matrix Address", 2),
            CancellationToken.None);
        await Assert.That(limited.Select(result => RequiredGuid(result, "LocationId")))
            .IsEquivalentTo([ApprovedId, CreatorId], CollectionOrdering.Matching);
        await Assert.That(interceptor.SelectCommands).HasSingleItem();

        (Guid OrganizationId, Guid ForbiddenLocationId)[] participationCanaries =
        [
            (Id(7), PendingOrganizationIdCanary),
            (Id(8), SuspendedOrganizationIdCanary),
            (Id(9), RevokedOrganizationIdCanary),
            (Id(25), DeletedOrganizationIdCanary)
        ];
        foreach ((Guid organizationId, Guid forbiddenLocationId) in participationCanaries)
        {
            interceptor.SelectCommands.Clear();
            IReadOnlyList<object> canaryResults = await InvokeSearchAsync(
                context,
                new SearchInput(TenantId, ActorId, UserId, organizationId, "Synthetic Matrix Address", 3),
                CancellationToken.None);

            await Assert.That(canaryResults.Select(result => RequiredGuid(result, "LocationId")))
                .DoesNotContain(forbiddenLocationId);
            await Assert.That(interceptor.SelectCommands).HasSingleItem();
        }

        (Guid OrganizationId, Guid ForbiddenLocationId)[] membershipCanaries =
        [
            (OtherOrganizationId, OtherOrganizationIdCanary),
            (Id(36), SoftDeletedMembershipIdCanary),
            (Id(37), GloballyDeletedOrganizationIdCanary),
            (Id(38), CrossTenantMembershipIdCanary)
        ];
        foreach ((Guid organizationId, Guid forbiddenLocationId) in membershipCanaries)
        {
            interceptor.SelectCommands.Clear();
            IReadOnlyList<object> canaryResults = await InvokeSearchAsync(
                context,
                new SearchInput(TenantId, ActorId, UserId, organizationId, "Synthetic Matrix Address", 3),
                CancellationToken.None);

            await Assert.That(canaryResults.Select(result => RequiredGuid(result, "LocationId")))
                .DoesNotContain(forbiddenLocationId);
            await Assert.That(interceptor.SelectCommands).HasSingleItem();
        }
    }

    private static async Task SeedUnicodeCorpusAsync(ExploreDbContext context)
    {
        TenantStatus status = await context.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = Tenant(TenantId, "unicode-tenant", status);
        context.Add(tenant);

        Location[] locations =
        [
            UnicodeLocation(UnicodePrefixId, "A", "Café 😀 %_\\ North"),
            UnicodeLocation(UnicodeLongerPrefixId, "AA", "Cafe\u0301 😀 %_\\ North"),
            UnicodeLocation(UnicodeBmpId, "\uE000", "CAFÉ 😀 %_\\ NORTH"),
            UnicodeLocation(UnicodeTieFirstId, "Tie", "Café tie corpus"),
            UnicodeLocation(UnicodeTieSecondId, "tie", "CAFE\u0301 tie corpus"),
            UnicodeLocation(ScalarBoundaryCanaryId, "Boundary A", "AB"),
            UnicodeLocation(ScalarBoundaryExactId, "Boundary B", char.ConvertFromUtf32(0x100004) + " exact"),
            UnicodeLocation(UnicodeLegacyId, "Legacy", "Legacy café address")
        ];
        foreach (Location location in locations)
        {
            location.ApplyAddressGovernance(
                ActorId,
                LocationAddressSourceEnum.Manual,
                location.Id == UnicodeLegacyId
                    ? LocationAddressVisibilityEnum.CreatorPrivate
                    : LocationAddressVisibilityEnum.TenantApproved,
                null);
        }
        Location legacy = locations.Single(location => location.Id == UnicodeLegacyId);
        SetPrivateProperty(legacy, nameof(Explore.Domain.Location.DisplaySortKey), string.Empty);
        SetPrivateProperty(legacy, nameof(Explore.Domain.Location.DisplaySortKeyVersion), (short)0);
        SetPrivateProperty(legacy.Pii!, nameof(LocationPii.AddressSubstringKey), string.Empty);
        SetPrivateProperty(legacy.Pii!, nameof(LocationPii.AddressSubstringKeyVersion), (short)0);
        context.AddRange(locations);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static Location UnicodeLocation(Guid id, string displayName, string address)
    {
        var location = new Location
        {
            Id = id,
            TenantId = TenantId,
            FullName = displayName,
            Country = "BE",
            City = "Brussels",
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Id(200 + id.ToByteArray()[15])
        };
        location.SetManualAddress(address, "1000");
        return location;
    }

    private static async Task AssertUnicodeCorpusAsync(ExploreDbContext context)
    {
        async Task<Guid[]> Search(string text, int limit = 20) => (await InvokeSearchAsync(
            context,
            new SearchInput(TenantId, ActorId, UserId, null, text, limit),
            CancellationToken.None)).Select(result => RequiredGuid(result, "LocationId")).ToArray();

        Guid[] composed = await Search("café 😀");
        Guid[] decomposed = await Search("CAFE\u0301 😀");
        await Assert.That(composed).IsEquivalentTo(
            [UnicodePrefixId, UnicodeLongerPrefixId, UnicodeBmpId], CollectionOrdering.Matching);
        await Assert.That(decomposed).IsEquivalentTo(composed, CollectionOrdering.Matching);
        await Assert.That(await Search("%_")).IsEquivalentTo(composed, CollectionOrdering.Matching);
        await Assert.That(await Search("😀 %")).IsEquivalentTo(composed, CollectionOrdering.Matching);
        await Assert.That(await Search(char.ConvertFromUtf32(0x100004)))
            .IsEquivalentTo([ScalarBoundaryExactId], CollectionOrdering.Matching);
        await Assert.That(await Search("café tie")).IsEquivalentTo(
            [UnicodeTieFirstId, UnicodeTieSecondId], CollectionOrdering.Matching);
        await Assert.That(await Search("legacy café")).IsEmpty();

        Location legacy = await context.Locations.SingleAsync(location => location.Id == UnicodeLegacyId);
        legacy.PromoteAddressToTenantApproved(ActorId, DateTime.UnixEpoch.AddDays(10));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Assert.That(await Search("legacy café"))
            .IsEquivalentTo([UnicodeLegacyId], CollectionOrdering.Matching);
    }

    private static void SetPrivateProperty(object target, string propertyName, object value) =>
        target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    private static async Task SeedMatrixAsync(ExploreDbContext context)
    {
        _ = RequiredDomainType(SourceTypeName);
        _ = RequiredDomainType(VisibilityTypeName);
        const LocationAddressSourceEnum manual = LocationAddressSourceEnum.Manual;
        const LocationAddressVisibilityEnum tenantApproved = LocationAddressVisibilityEnum.TenantApproved;
        const LocationAddressVisibilityEnum creatorPrivate = LocationAddressVisibilityEnum.CreatorPrivate;
        const LocationAddressVisibilityEnum organizationScoped = LocationAddressVisibilityEnum.OrganizationScoped;
        const LocationAddressVisibilityEnum quarantined = LocationAddressVisibilityEnum.Quarantined;
        TenantStatus status = await context.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        ApprovalStatus approvedStatus = await context.ApprovalStatuses.SingleAsync(
            item => item.Id == (int)ApprovalStatusEnum.Approved);
        ApprovalStatus pendingStatus = await context.ApprovalStatuses.SingleAsync(
            item => item.Id == (int)ApprovalStatusEnum.Pending);
        ApprovalStatus revokedStatus = await context.ApprovalStatuses.SingleAsync(
            item => item.Id == (int)ApprovalStatusEnum.Revoked);
        var tenant = Tenant(TenantId, "matrix-tenant", status);
        var foreignTenant = Tenant(ForeignTenantId, "foreign-matrix-tenant", status);
        var organization = Organization(OrganizationId, "Matrix organization");
        var otherOrganization = Organization(OtherOrganizationId, "Other matrix organization");
        var pendingOrganization = Organization(Id(7), "Pending matrix organization");
        var suspendedOrganization = Organization(Id(8), "Suspended matrix organization");
        var revokedOrganization = Organization(Id(9), "Revoked matrix organization");
        var deletedOrganization = Organization(Id(25), "Deleted matrix organization");
        var softDeletedMembershipOrganization = Organization(Id(36), "Soft-deleted membership organization");
        var globallyDeletedOrganization = Organization(Id(37), "Globally deleted organization");
        globallyDeletedOrganization.IsDeleted = true;
        globallyDeletedOrganization.DeletedAt = DateTime.UnixEpoch.AddDays(2);
        var crossTenantMembershipOrganization = Organization(Id(38), "Cross-tenant membership organization");
        var owner = new User
        {
            Id = ActorId,
            Pii = new UserPii
            {
                UserId = ActorId,
                Email = "address-owner@example.invalid",
                FirstName = "Address",
                LastName = "Owner"
            },
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Id(29)
        };
        owner.Pii.User = owner;
        var memberUser = new User
        {
            Id = UserId,
            Pii = new UserPii
            {
                UserId = UserId,
                Email = "address-member@example.invalid",
                FirstName = "Address",
                LastName = "Member"
            },
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Id(39)
        };
        memberUser.Pii.User = memberUser;
        OrganizationTenant participation = Participation(tenant, organization, approvedStatus, Id(30));
        organization.TenantParticipations.Add(participation);
        OrganizationTenant otherParticipation = Participation(tenant, otherOrganization, approvedStatus, Id(31));
        otherOrganization.TenantParticipations.Add(otherParticipation);
        OrganizationTenant pendingParticipation = Participation(tenant, pendingOrganization, pendingStatus, Id(32));
        pendingOrganization.TenantParticipations.Add(pendingParticipation);
        OrganizationTenant suspendedParticipation = Participation(
            tenant,
            suspendedOrganization,
            approvedStatus,
            Id(33));
        suspendedParticipation.IsSuspended = true;
        suspendedOrganization.TenantParticipations.Add(suspendedParticipation);
        OrganizationTenant revokedParticipation = Participation(tenant, revokedOrganization, revokedStatus, Id(34));
        revokedOrganization.TenantParticipations.Add(revokedParticipation);
        OrganizationTenant deletedParticipation = Participation(tenant, deletedOrganization, approvedStatus, Id(35));
        deletedParticipation.IsDeleted = true;
        deletedParticipation.DeletedAt = DateTime.UnixEpoch.AddDays(1);
        deletedOrganization.TenantParticipations.Add(deletedParticipation);
        OrganizationTenant softDeletedMembershipParticipation = Participation(
            tenant, softDeletedMembershipOrganization, approvedStatus, Id(40));
        softDeletedMembershipOrganization.TenantParticipations.Add(softDeletedMembershipParticipation);
        OrganizationTenant globallyDeletedOrganizationParticipation = Participation(
            tenant, globallyDeletedOrganization, approvedStatus, Id(41));
        globallyDeletedOrganization.TenantParticipations.Add(globallyDeletedOrganizationParticipation);
        OrganizationTenant crossTenantTargetParticipation = Participation(
            tenant, crossTenantMembershipOrganization, approvedStatus, Id(42));
        OrganizationTenant crossTenantMemberParticipation = Participation(
            foreignTenant, crossTenantMembershipOrganization, approvedStatus, Id(43));
        crossTenantMembershipOrganization.TenantParticipations.Add(crossTenantTargetParticipation);
        crossTenantMembershipOrganization.TenantParticipations.Add(crossTenantMemberParticipation);
        context.AddRange(
            tenant,
            foreignTenant,
            organization,
            otherOrganization,
            pendingOrganization,
            suspendedOrganization,
            revokedOrganization,
            deletedOrganization,
            softDeletedMembershipOrganization,
            globallyDeletedOrganization,
            crossTenantMembershipOrganization,
            owner,
            memberUser);

        Role memberRole = await context.Roles.SingleAsync(role => role.Id == (int)RoleEnum.OrgMember);
        OrganizationMember softDeletedMember = Membership(
            softDeletedMembershipParticipation, memberUser, memberRole, tenant, Id(45));
        softDeletedMember.IsDeleted = true;
        softDeletedMember.DeletedAt = DateTime.UnixEpoch.AddDays(3);
        context.OrganizationMembers.AddRange(
            Membership(participation, memberUser, memberRole, tenant, Id(44)),
            Membership(pendingParticipation, memberUser, memberRole, tenant, Id(46)),
            Membership(suspendedParticipation, memberUser, memberRole, tenant, Id(47)),
            Membership(revokedParticipation, memberUser, memberRole, tenant, Id(48)),
            Membership(deletedParticipation, memberUser, memberRole, tenant, Id(49)),
            softDeletedMember,
            Membership(globallyDeletedOrganizationParticipation, memberUser, memberRole, tenant, Id(50)),
            Membership(crossTenantMemberParticipation, memberUser, memberRole, foreignTenant, Id(51)));

        Location approved = Location(ApprovedId, tenant, "Display Alpha", "Synthetic Matrix Address", "0000");
        Location creator = Location(CreatorId, tenant, "Display Beta", "Synthetic Matrix Address", "0000");
        Location organizationRow = Location(OrganizationScopedId, tenant, "Display Gamma", "Synthetic Matrix Address", "0000");
        Location crossTenant = Location(CrossTenantId, foreignTenant, "Display Delta", "Synthetic Matrix Address", "0000");
        Location otherOrganizationRow = Location(OtherOrganizationIdCanary, tenant, "Display Epsilon", "Synthetic Matrix Address", "0000");
        Location otherCreator = Location(OtherCreatorIdCanary, tenant, "Display Zeta", "Synthetic Matrix Address", "0000");
        Location quarantine = Location(QuarantinedId, tenant, "Display Eta", "Synthetic Matrix Address", "0000");
        Location erased = Location(ErasedId, tenant, "Display Theta", "Synthetic Matrix Address", "0000");
        Location privateHome = Location(PrivateHomeId, tenant, "Display Iota", "Synthetic Matrix Address", "0000");
        Location nonMatching = Location(NonMatchingId, tenant, "Synthetic Matrix Address", "Different Canary Address", "9999");
        Location notProvidedWithPii = Location(NotProvidedWithPiiId, tenant, "Display Kappa", "Synthetic Matrix Address", "0000");
        Location pendingOrganizationRow = Location(PendingOrganizationIdCanary, tenant, "Display Lambda", "Synthetic Matrix Address", "0000");
        Location suspendedOrganizationRow = Location(SuspendedOrganizationIdCanary, tenant, "Display Mu", "Synthetic Matrix Address", "0000");
        Location revokedOrganizationRow = Location(RevokedOrganizationIdCanary, tenant, "Display Nu", "Synthetic Matrix Address", "0000");
        Location deletedOrganizationRow = Location(DeletedOrganizationIdCanary, tenant, "Display Xi", "Synthetic Matrix Address", "0000");
        Location softDeletedMembershipRow = Location(SoftDeletedMembershipIdCanary, tenant, "Display Omicron", "Synthetic Matrix Address", "0000");
        Location globallyDeletedOrganizationRow = Location(GloballyDeletedOrganizationIdCanary, tenant, "Display Pi", "Synthetic Matrix Address", "0000");
        Location crossTenantMembershipRow = Location(CrossTenantMembershipIdCanary, tenant, "Display Rho", "Synthetic Matrix Address", "0000");

        ApplyGovernance(approved, ActorId, manual, tenantApproved, null);
        ApplyGovernance(creator, ActorId, manual, creatorPrivate, null);
        ApplyGovernance(organizationRow, ActorId, manual, organizationScoped, OrganizationId);
        ApplyGovernance(crossTenant, ActorId, manual, tenantApproved, null);
        ApplyGovernance(otherOrganizationRow, ActorId, manual, organizationScoped, OtherOrganizationId);
        ApplyGovernance(otherCreator, OtherActorId, manual, creatorPrivate, null);
        ApplyGovernance(quarantine, ActorId, manual, quarantined, null);
        ApplyGovernance(erased, ActorId, manual, creatorPrivate, null);
        ApplyGovernance(privateHome, ActorId, manual, creatorPrivate, null);
        ApplyGovernance(nonMatching, ActorId, manual, tenantApproved, null);
        ApplyGovernance(notProvidedWithPii, ActorId, manual, tenantApproved, null);
        ApplyGovernance(pendingOrganizationRow, ActorId, manual, organizationScoped, Id(7));
        ApplyGovernance(suspendedOrganizationRow, ActorId, manual, organizationScoped, Id(8));
        ApplyGovernance(revokedOrganizationRow, ActorId, manual, organizationScoped, Id(9));
        ApplyGovernance(deletedOrganizationRow, ActorId, manual, organizationScoped, Id(25));
        ApplyGovernance(softDeletedMembershipRow, ActorId, manual, organizationScoped, Id(36));
        ApplyGovernance(globallyDeletedOrganizationRow, ActorId, manual, organizationScoped, Id(37));
        ApplyGovernance(crossTenantMembershipRow, ActorId, manual, organizationScoped, Id(38));
        erased.ClassifyAsPrivateHome(ActorId);
        erased.EraseOwnedPii(DateTime.UnixEpoch.AddDays(4), LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
        privateHome.ClassifyAsPrivateHome(ActorId);

        context.Locations.AddRange(
            approved,
            creator,
            organizationRow,
            crossTenant,
            otherOrganizationRow,
            otherCreator,
            quarantine,
            erased,
            privateHome,
            nonMatching,
            notProvidedWithPii,
            pendingOrganizationRow,
            suspendedOrganizationRow,
            revokedOrganizationRow,
            deletedOrganizationRow,
            softDeletedMembershipRow,
            globallyDeletedOrganizationRow,
            crossTenantMembershipRow);
        context.Entry(notProvidedWithPii)
            .Property(nameof(Explore.Domain.Location.LocationPrivacyStateId))
            .CurrentValue = (int)LocationPrivacyStateEnum.NotProvided;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static Location Location(
        Guid id,
        Tenant tenant,
        string displayName,
        string address,
        string postcode)
    {
        var location = new Location
        {
            Id = id,
            TenantId = tenant.Id,
            Tenant = tenant,
            FullName = displayName,
            Country = "BE",
            City = "Brussels",
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Id(90 + id.ToByteArray()[15])
        };
        location.SetManualAddress(address, postcode);
        return location;
    }

    private static Tenant Tenant(Guid id, string slug, TenantStatus status) => new()
    {
        Id = id,
        FullName = "Synthetic matrix tenant",
        Slug = slug,
        TenantStatusId = status.Id,
        TenantStatus = status,
        CreatedAt = DateTime.UnixEpoch
    };

    private static Organization Organization(Guid id, string name) => new()
    {
        Id = id,
        Pii = new OrganizationPii { FullName = name },
        CreatedAt = DateTime.UnixEpoch,
        ConcurrencyStamp = Id(70 + id.ToByteArray()[15])
    };

    private static OrganizationTenant Participation(
        Tenant tenant,
        Organization organization,
        ApprovalStatus status,
        Guid id) => new()
    {
        Id = id,
        TenantId = tenant.Id,
        Tenant = tenant,
        OrganizationId = organization.Id,
        Organization = organization,
        ApprovalStatusId = status.Id,
        ApprovalStatus = status,
        CreatedAt = DateTime.UnixEpoch,
        ConcurrencyStamp = Id(80 + id.ToByteArray()[15])
    };

    private static OrganizationMember Membership(
        OrganizationTenant participation,
        User user,
        Role role,
        Tenant tenant,
        Guid id) => new()
    {
        Id = id,
        OrganizationTenantId = participation.Id,
        OrganizationTenant = participation,
        UserId = user.Id,
        User = user,
        RoleId = role.Id,
        Role = role,
        TenantId = tenant.Id,
        Tenant = tenant,
        CreatedAt = DateTime.UnixEpoch
    };

    private static void ApplyGovernance(
        Location location,
        Guid actorId,
        LocationAddressSourceEnum source,
        LocationAddressVisibilityEnum visibility,
        Guid? organizationId) =>
        location.ApplyAddressGovernance(actorId, source, visibility, organizationId);

    private static async Task<IReadOnlyList<object>> InvokeSearchAsync(
        ExploreDbContext context,
        SearchInput input,
        CancellationToken cancellationToken)
    {
        ILocalAddressSuggestionQuery query = new LocalAddressSuggestionQuery(context);
        IReadOnlyList<LocalAddressSuggestion> results = await query.SearchAsync(
            new LocalAddressSuggestionCriteria(
                input.TenantId,
                input.ActorId,
                input.UserId,
                input.OrganizationId,
                input.SearchText,
                input.Limit),
            cancellationToken);
        return results.Cast<object>().ToArray();
    }

    private ExploreDbContext CreatePostgreSqlContext(DbCommandInterceptor interceptor)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;
        return new ExploreDbContext(options) { TenantContext = new TestTenantContext(TenantId) };
    }

    private static ExploreDbContext CreateSqliteContext(string path, DbCommandInterceptor? interceptor = null)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                ForeignKeys = true
            }.ToString())
            .UseSnakeCaseNamingConvention();
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }
        return new ExploreDbContext(builder.Options);
    }

    private static (string Projection, string Predicate) SplitSelectAndWhere(string commandText)
    {
        string normalized = commandText.ToLowerInvariant();
        int selectIndex = normalized.IndexOf("select", StringComparison.Ordinal);
        int fromIndex = normalized.IndexOf("\nfrom ", selectIndex, StringComparison.Ordinal);
        int whereIndex = normalized.IndexOf("\nwhere ", fromIndex, StringComparison.Ordinal);
        if (selectIndex < 0 || fromIndex <= selectIndex || whereIndex <= fromIndex)
        {
            throw new InvalidOperationException("Local suggestion SQL must contain one SELECT projection and database WHERE predicate.");
        }
        int predicateEnd = new[]
            {
                normalized.IndexOf(" order by ", whereIndex, StringComparison.Ordinal),
                normalized.IndexOf(" limit ", whereIndex, StringComparison.Ordinal),
                normalized.Length
            }
            .Where(index => index > whereIndex)
            .Min();
        return (normalized[selectIndex..fromIndex], normalized[(whereIndex + 7)..predicateEnd]);
    }

    private static Guid RequiredGuid(object instance, string name) =>
        RequiredValue(instance, name) is Guid value
            ? value
            : throw new InvalidOperationException($"{instance.GetType().FullName}.{name} must be Guid.");

    private static string RequiredDisplayName(object instance)
    {
        PropertyInfo property = instance.GetType().GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance)
            ?? instance.GetType().GetProperty("FullName", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Local suggestion result requires one public display-name field.");
        return property.GetValue(instance) as string
            ?? throw new InvalidOperationException("Local suggestion display name must be String.");
    }

    private static string RequiredString(object instance, string name) =>
        RequiredValue(instance, name) as string
        ?? throw new InvalidOperationException($"{instance.GetType().FullName}.{name} must be String.");

    private static object RequiredValue(object instance, string name) =>
        RequiredPublicProperty(instance.GetType(), name).GetValue(instance)
        ?? throw new InvalidOperationException($"{instance.GetType().FullName}.{name} must not be null.");

    private static PropertyInfo RequiredPublicProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"{type.FullName} is missing public {name}.");

    private static Type RequiredApplicationType(string name) => typeof(ITenantContext).Assembly.GetType(name, throwOnError: false)
        ?? throw new InvalidOperationException($"Application contract {name} is missing.");

    private static Type RequiredDomainType(string name) => typeof(Location).Assembly.GetType(name, throwOnError: false)
        ?? throw new InvalidOperationException($"Domain contract {name} is missing.");

    private static Guid Id(int suffix) => Guid.Parse($"019b0000-0001-7000-8000-{suffix:000000000000}");

    private static string DatabasePath(string suffix) => Path.Combine(Path.GetTempPath(), $"local-address-{suffix}.db");

    private static void DeleteSqliteFiles(string path)
    {
        File.Delete(path);
        File.Delete(path + "-shm");
        File.Delete(path + "-wal");
    }

    private sealed record SearchInput(
        Guid TenantId,
        Guid ActorId,
        Guid UserId,
        Guid? OrganizationId,
        string SearchText,
        int Limit);
    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed class SelectCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> SelectCommands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Capture(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }

        private void Capture(DbCommand command)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                SelectCommands.Add(command.CommandText);
            }
        }
    }
}
