// ABOUTME: Specifies Phase 21 admission-target, append-only check-in, and scanner-capability persistence.
// ABOUTME: Proves portable model parity, tenant isolation, one-query lookup, and deterministic PostgreSQL races.

using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using Event.Persistence.IntegrationTests;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.QueryFilters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using TUnit.Core;

namespace EventPersistence.IntegrationTests;

[Category("Phase21AdmissionCheckInPersistenceRed")]
public sealed class AdmissionCheckInPersistenceRedTests
{
    [Test]
    public async Task TenantQualifiedModelMapsTargetsPoliciesEventsActiveStateAndScannerCapabilities()
    {
        await using ExploreDbContext context = Phase21PersistenceSurface.CreateModelContext("PostgreSql");
        Phase21Entities entities = Phase21PersistenceSurface.RequireEntities(
            context.GetService<IDesignTimeModel>().Model);

        foreach (IEntityType entity in entities.TenantEntities)
        {
            await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
            await Assert.That(entity.GetKeys().Any(key => Phase21PersistenceSurface.HasProperties(
                key.Properties, nameof(AdmissionTarget.TenantId), nameof(AdmissionTarget.Id)))).IsTrue();
            await Assert.That(entity.FindProperty(nameof(AdmissionTarget.Id))!.ValueGenerated).IsEqualTo(ValueGenerated.Never);
        }

        await Assert.That(entities.Target.FindProperty(nameof(AdmissionTarget.AdmissionTargetTypeId))).IsNotNull();
        await Assert.That(entities.Target.FindProperty(nameof(AdmissionTarget.AdmissionOperationalStatusId))).IsNotNull();
        await Assert.That(entities.Target.FindProperty(nameof(AdmissionTarget.EventDayId))).IsNotNull();
        await Assert.That(entities.Target.FindProperty(nameof(AdmissionTarget.EventSessionId))).IsNotNull();
        await Assert.That(entities.Target.FindProperty("TargetKind")).IsNull();
        await Assert.That(entities.Target.GetIndexes().Any(index => index.IsUnique &&
            Phase21PersistenceSurface.IsCanonicalTargetIdentity(index))).IsTrue();
        IProperty scopeId = entities.Target.FindProperty(nameof(AdmissionTarget.ScopeId))!;
        await Assert.That(scopeId).IsNotNull();
        await Assert.That(scopeId.IsNullable).IsFalse();
        await Assert.That(scopeId.ClrType).IsEqualTo(typeof(Guid));
        await Assert.That(entities.Target.GetIndexes().Any(index =>
            index.Properties.Contains(scopeId))).IsTrue();
        await Assert.That(entities.Target.GetKeys().Any(key => key.Properties.Contains(scopeId))).IsFalse();
        await Assert.That(entities.Target.GetForeignKeys().Any(foreignKey =>
            foreignKey.Properties.Contains(scopeId))).IsFalse();
        await Assert.That(entities.Target.FindProperty(nameof(AdmissionTarget.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();
        await Assert.That(entities.Target.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_admission_targets_scope_shape");
        await Assert.That(entities.Target.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Explore.Domain.Event) &&
            Phase21PersistenceSurface.HasProperties(foreignKey.Properties, nameof(AdmissionTarget.TenantId), nameof(AdmissionTarget.EventId)) &&
            Phase21PersistenceSurface.HasProperties(foreignKey.PrincipalKey.Properties, nameof(Explore.Domain.Event.TenantId), nameof(Explore.Domain.Event.Id)) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
        await AssertScopedTargetForeignKeyAsync(
            entities.Target,
            typeof(EventDay),
            nameof(AdmissionTarget.EventDayId));
        await AssertScopedTargetForeignKeyAsync(
            entities.Target,
            typeof(EventSession),
            nameof(AdmissionTarget.EventSessionId));
        await AssertTenantForeignKeyAsync(entities.Policy, entities.Target, nameof(AdmissionCheckInPolicy.TenantId), nameof(AdmissionCheckInPolicy.AdmissionTargetId));
        IProperty opensAtUtc = entities.Policy.FindProperty(nameof(AdmissionCheckInPolicy.OpensAtUtc))!;
        IProperty closesAtUtc = entities.Policy.FindProperty(nameof(AdmissionCheckInPolicy.ClosesAtUtc))!;
        IProperty maximumEntries = entities.Policy.FindProperty(nameof(AdmissionCheckInPolicy.MaximumEntries))!;
        await Assert.That(opensAtUtc).IsNotNull();
        await Assert.That(closesAtUtc).IsNotNull();
        await Assert.That(opensAtUtc.ClrType).IsEqualTo(typeof(DateTime));
        await Assert.That(closesAtUtc.ClrType).IsEqualTo(typeof(DateTime));
        await Assert.That(opensAtUtc.IsNullable).IsFalse();
        await Assert.That(closesAtUtc.IsNullable).IsFalse();
        await Assert.That(opensAtUtc.GetColumnType()).IsEqualTo("timestamp with time zone");
        await Assert.That(closesAtUtc.GetColumnType()).IsEqualTo("timestamp with time zone");
        await Assert.That(maximumEntries).IsNotNull();
        await Assert.That(maximumEntries.ClrType).IsEqualTo(typeof(int));
        await Assert.That(maximumEntries.IsNullable).IsFalse();
        await Assert.That(entities.Policy.FindProperty(nameof(AdmissionCheckInPolicy.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();

        await AssertTenantForeignKeyAsync(entities.CheckInEvent, entities.Ticket, nameof(AdmissionCheckInEvent.TenantId), nameof(AdmissionCheckInEvent.AdmissionTicketId));
        await AssertTenantForeignKeyAsync(entities.CheckInEvent, entities.Target, nameof(AdmissionCheckInEvent.TenantId), nameof(AdmissionCheckInEvent.AdmissionTargetId));
        await AssertTenantForeignKeyAsync(entities.CheckInEvent, entities.Scanner, nameof(AdmissionCheckInEvent.TenantId), nameof(AdmissionCheckInEvent.ScannerCapabilityId));
        IProperty compensatedCheckInEventId = entities.CheckInEvent.FindProperty(nameof(AdmissionCheckInEvent.CompensatedCheckInEventId))!;
        await Assert.That(compensatedCheckInEventId).IsNotNull();
        await Assert.That(compensatedCheckInEventId.ClrType).IsEqualTo(typeof(Guid?));
        await Assert.That(entities.CheckInEvent.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType == entities.CheckInEvent &&
            Phase21PersistenceSurface.HasProperties(
                foreignKey.Properties,
                nameof(AdmissionCheckInEvent.TenantId),
                nameof(AdmissionCheckInEvent.AdmissionTicketId),
                nameof(AdmissionCheckInEvent.AdmissionTargetId),
                nameof(AdmissionCheckInEvent.CompensatedCheckInEventId)) &&
            Phase21PersistenceSurface.HasProperties(
                foreignKey.PrincipalKey.Properties,
                nameof(AdmissionCheckInEvent.TenantId),
                nameof(AdmissionCheckInEvent.AdmissionTicketId),
                nameof(AdmissionCheckInEvent.AdmissionTargetId),
                nameof(AdmissionCheckInEvent.Id)) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
        await Assert.That(entities.CheckInEvent.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_admission_check_in_events_fact_shape");
        await Assert.That(entities.CheckInEvent.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_admission_check_in_events_authority");
        await Assert.That(entities.CheckInEvent.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_admission_check_in_events_action");
        await AssertTenantForeignKeyAsync(entities.State, entities.Ticket, nameof(AdmissionCheckInState.TenantId), nameof(AdmissionCheckInState.AdmissionTicketId));
        await AssertTenantForeignKeyAsync(entities.State, entities.Target, nameof(AdmissionCheckInState.TenantId), nameof(AdmissionCheckInState.AdmissionTargetId));
        await Assert.That(entities.State.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType == entities.CheckInEvent &&
            Phase21PersistenceSurface.HasProperties(
                foreignKey.Properties,
                nameof(AdmissionCheckInState.TenantId),
                nameof(AdmissionCheckInState.AdmissionTicketId),
                nameof(AdmissionCheckInState.AdmissionTargetId),
                nameof(AdmissionCheckInState.ActiveCheckInEventId)) &&
            Phase21PersistenceSurface.HasProperties(
                foreignKey.PrincipalKey.Properties,
                nameof(AdmissionCheckInEvent.TenantId),
                nameof(AdmissionCheckInEvent.AdmissionTicketId),
                nameof(AdmissionCheckInEvent.AdmissionTargetId),
                nameof(AdmissionCheckInEvent.Id)) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
        await Assert.That(entities.Scanner.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType == entities.Target &&
            Phase21PersistenceSurface.HasProperties(
                foreignKey.Properties, nameof(AdmissionScannerCapability.TenantId), nameof(AdmissionScannerCapability.EventId), nameof(AdmissionScannerCapability.AdmissionTargetId)) &&
            Phase21PersistenceSurface.HasProperties(
                foreignKey.PrincipalKey.Properties, nameof(AdmissionTarget.TenantId), nameof(AdmissionTarget.EventId), nameof(AdmissionTarget.Id)) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
        await Assert.That(entities.Scanner.FindProperty(nameof(AdmissionScannerCapability.AdmissionTargetId))).IsNotNull();
        await Assert.That(entities.Scanner.GetIndexes().Any(index =>
            Phase21PersistenceSurface.HasProperties(index.Properties, nameof(AdmissionScannerCapability.TenantId), nameof(AdmissionScannerCapability.AdmissionTargetId))))
            .IsTrue();
        await Assert.That(context.Model.GetEntityTypes().Any(entity =>
            entity.ClrType.FullName == "Explore.Domain.AdmissionScannerCapabilityTarget")).IsFalse();
    }

    [Test]
    public async Task CheckInEventsAreAppendOnlyAndStateUsesOneConcurrencyFencedProjectionRow()
    {
        await using ExploreDbContext context = Phase21PersistenceSurface.CreateModelContext("PostgreSql");
        Phase21Entities entities = Phase21PersistenceSurface.RequireEntities(
            context.GetService<IDesignTimeModel>().Model);
        string[] eventProperties = entities.CheckInEvent.GetProperties().Select(property => property.Name).ToArray();

        await Assert.That(eventProperties).Contains(nameof(AdmissionCheckInEvent.AdmissionCheckInActionId));
        await Assert.That(eventProperties).Contains(nameof(AdmissionCheckInEvent.OccurredAtUtc));
        await Assert.That(eventProperties).Contains(nameof(AdmissionCheckInEvent.AdmissionTicketId));
        await Assert.That(eventProperties).Contains(nameof(AdmissionCheckInEvent.AdmissionTargetId));
        await Assert.That(eventProperties).Contains(nameof(AdmissionCheckInEvent.ActorId));
        await Assert.That(eventProperties).Contains(nameof(AdmissionCheckInEvent.ScannerCapabilityId));
        await Assert.That(eventProperties).Contains(nameof(AdmissionCheckInEvent.AdmissionCheckInUndoReasonCodeId));
        await Assert.That(eventProperties).Contains(nameof(AdmissionCheckInEvent.CompensatedCheckInEventId));
        await Assert.That(eventProperties.Intersect(
            [
                "Action", "OccurredAt", "ActorUserId", "AdmissionScannerCapabilityId",
                "IsActive", "IsCheckedIn", "CheckedInAt", "UndoneAt", "DeletedAt", "UpdatedAt"
            ],
            StringComparer.OrdinalIgnoreCase)).IsEmpty();
        await Assert.That(entities.CheckInEvent.GetForeignKeys().All(foreignKey =>
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();

        IIndex stateIdentity = entities.State.GetIndexes().Single(index => index.IsUnique &&
            Phase21PersistenceSurface.HasProperties(
                index.Properties,
                nameof(AdmissionCheckInState.TenantId),
                nameof(AdmissionCheckInState.AdmissionTicketId),
                nameof(AdmissionCheckInState.AdmissionTargetId)));
        await Assert.That(stateIdentity.GetFilter()).IsNull();
        await Assert.That(entities.State.FindProperty("ActiveUniquenessSlot")).IsNull();
        await Assert.That(entities.State.FindProperty(nameof(AdmissionCheckInState.ActiveCheckInEventId))!.ClrType)
            .IsEqualTo(typeof(Guid?));
        await Assert.That(entities.State.FindProperty(nameof(AdmissionCheckInState.EntryCount))!.ClrType).IsEqualTo(typeof(int));
        await Assert.That(entities.State.FindProperty(nameof(AdmissionCheckInState.EntryCount))!.IsNullable).IsFalse();
        IProperty lastSequence = entities.State.FindProperty(nameof(AdmissionCheckInState.LastSequence))!;
        await Assert.That(lastSequence).IsNotNull();
        await Assert.That(lastSequence.ClrType == typeof(int) || lastSequence.ClrType == typeof(long)).IsTrue();
        await Assert.That(lastSequence.IsNullable).IsFalse();
        await Assert.That(entities.State.FindProperty(nameof(AdmissionCheckInState.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();
        await Assert.That(entities.State.GetCheckConstraints().Select(constraint => constraint.Name))
            .DoesNotContain("ck_admission_check_in_states_active_slot");

        IEntityType entitlement = context.Model.FindEntityType(typeof(TicketTypeEntitlement))!;
        IProperty entitlementScopeId = entitlement.FindProperty(nameof(TicketTypeEntitlement.ScopeId))!;
        await Assert.That(entitlementScopeId).IsNotNull();
        await Assert.That(entitlementScopeId.IsNullable).IsFalse();
        await Assert.That(entitlement.GetIndexes().Any(index => index.IsUnique &&
            Phase21PersistenceSurface.HasProperties(
                index.Properties,
                nameof(TicketTypeEntitlement.TenantId),
                nameof(TicketTypeEntitlement.TicketTypeId),
                nameof(TicketTypeEntitlement.TargetEventId),
                nameof(TicketTypeEntitlement.EntitlementScopeTypeId),
                nameof(TicketTypeEntitlement.ScopeId)))).IsTrue();
    }

    [Test]
    [Arguments(EntityState.Modified)]
    [Arguments(EntityState.Deleted)]
    public async Task CheckInEventWriteGuardRejectsMutationAndDeletion(EntityState attemptedState)
    {
        await using ExploreDbContext context = Phase21PersistenceSurface.CreateModelContext("PostgreSql");
        var fact = new AdmissionCheckInEvent(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            AdmissionCheckInActionEnum.CheckIn,
            actorId: null,
            scannerCapabilityId: null,
            reasonCode: null,
            new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc),
            compensatedCheckInEventId: null);
        context.Attach(fact);
        context.Entry(fact).State = attemptedState;

        InvalidOperationException exception = (await Assert.That(() => context.SaveChangesAsync())
            .Throws<InvalidOperationException>())!;

        await Assert.That(exception.Message).Contains("append-only");
    }

    [Test]
    public async Task DigestIndexesMatchLookupPredicatesAndScannerStorageCannotRetainBearerMaterial()
    {
        await using ExploreDbContext context = Phase21PersistenceSurface.CreateModelContext("PostgreSql");
        Phase21Entities entities = Phase21PersistenceSurface.RequireEntities(
            context.GetService<IDesignTimeModel>().Model);

        IIndex credentialLookup = entities.Credential.GetIndexes().Single(index => index.IsUnique &&
            Phase21PersistenceSurface.HasProperties(
                index.Properties,
                nameof(AdmissionTicketCredential.TenantId),
                nameof(AdmissionTicketCredential.LookupKeyVersion),
                nameof(AdmissionTicketCredential.LookupDigest)));
        IIndex scannerLookup = entities.Scanner.GetIndexes().Single(index => index.IsUnique &&
            Phase21PersistenceSurface.HasProperties(
                index.Properties,
                nameof(AdmissionScannerCapability.TenantId),
                nameof(AdmissionScannerCapability.LookupKeyVersion),
                nameof(AdmissionScannerCapability.LookupDigest)));
        await Assert.That(credentialLookup.GetFilter()).IsNull();
        await Assert.That(scannerLookup.GetFilter()).IsNull();

        string[] persistedNames = entities.Scanner.GetProperties()
            .SelectMany(property => new[] { property.Name, property.GetColumnName() })
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        string[] forbidden = ["Plaintext", "Bearer", "RawToken", "Secret", "CapabilityToken"];
        await Assert.That(persistedNames.Any(name => forbidden.Any(fragment =>
            name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))).IsFalse();
        await Assert.That(entities.Scanner.FindProperty(nameof(AdmissionScannerCapability.LookupDigest))).IsNotNull();
        await Assert.That(entities.Scanner.FindProperty(nameof(AdmissionScannerCapability.LookupKeyVersion))).IsNotNull();
        await Assert.That(entities.Scanner.FindProperty(nameof(AdmissionScannerCapability.ExpiresAt))).IsNotNull();
        await Assert.That(entities.Scanner.FindProperty(nameof(AdmissionScannerCapability.RevokedAt))).IsNotNull();
        await Assert.That(entities.Scanner.FindProperty("RevealConsumedAt")).IsNull();
        IProperty issueRequestId = entities.Scanner.FindProperty(nameof(AdmissionScannerCapability.IssueRequestId))!;
        await Assert.That(issueRequestId).IsNotNull();
        await Assert.That(issueRequestId.ClrType).IsEqualTo(typeof(Guid));
        await Assert.That(issueRequestId.IsNullable).IsFalse();
        await Assert.That(entities.Scanner.GetIndexes().Any(index => index.IsUnique &&
            Phase21PersistenceSurface.HasProperties(
                index.Properties,
                nameof(AdmissionScannerCapability.TenantId),
                nameof(AdmissionScannerCapability.IssueRequestId))))
            .IsTrue();
        await Assert.That(entities.Scanner.FindProperty(nameof(AdmissionScannerCapability.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task EveryProviderMapsTheSameTenantKeysStateIdentityAndIssuanceFence(string provider)
    {
        await using ExploreDbContext context = Phase21PersistenceSurface.CreateModelContext(provider);
        Phase21Entities entities = Phase21PersistenceSurface.RequireEntities(
            context.GetService<IDesignTimeModel>().Model);

        IIndex stateIdentity = entities.State.GetIndexes().Single(index => index.IsUnique &&
            Phase21PersistenceSurface.HasProperties(
                index.Properties,
                nameof(AdmissionCheckInState.TenantId),
                nameof(AdmissionCheckInState.AdmissionTicketId),
                nameof(AdmissionCheckInState.AdmissionTargetId)));
        IIndex scannerDigest = entities.Scanner.GetIndexes().Single(index => index.IsUnique &&
            Phase21PersistenceSurface.HasProperties(
                index.Properties,
                nameof(AdmissionScannerCapability.TenantId),
                nameof(AdmissionScannerCapability.LookupKeyVersion),
                nameof(AdmissionScannerCapability.LookupDigest)));
        IIndex scannerIssuance = entities.Scanner.GetIndexes().Single(index => index.IsUnique &&
            Phase21PersistenceSurface.HasProperties(
                index.Properties,
                nameof(AdmissionScannerCapability.TenantId),
                nameof(AdmissionScannerCapability.IssueRequestId)));

        await Assert.That(stateIdentity.GetFilter()).IsNull();
        await Assert.That(scannerDigest.GetFilter()).IsNull();
        await Assert.That(scannerIssuance.GetFilter()).IsNull();
        await Assert.That(entities.TenantEntities.All(entity =>
            entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant) is not null)).IsTrue();
        await Assert.That(entities.CheckInEvent.GetForeignKeys().All(foreignKey =>
            foreignKey.Properties.First().Name ==
            nameof(AdmissionCheckInEvent.TenantId))).IsTrue();
        await Assert.That(entities.Scanner.GetForeignKeys().All(foreignKey =>
            foreignKey.Properties.First().Name ==
                nameof(AdmissionScannerCapability.TenantId) ||
            foreignKey.PrincipalEntityType.ClrType ==
                typeof(Actor))).IsTrue();
        await Assert.That(entities.TenantEntities.All(entity =>
            entity.FindProperty(nameof(AdmissionTarget.Id))!
                .GetDefaultValueSql() is null)).IsTrue();
        await Assert.That(entities.Target.GetCheckConstraints().Any(constraint =>
            constraint.Name == "ck_admission_targets_scope_shape")).IsTrue();
        await Assert.That(entities.CheckInEvent.GetCheckConstraints().Count(constraint =>
            constraint.Name is "ck_admission_check_in_events_action" or
                "ck_admission_check_in_events_authority" or
                "ck_admission_check_in_events_fact_shape")).IsEqualTo(3);
    }

    [Test]
    public async Task PerformanceFixtureDeclaresMachineConsumedPostgreSqlLoadThresholds()
    {
        AdmissionCheckInPerformanceFixture fixture = AdmissionCheckInPerformanceFixture.PostgreSqlCi;

        await Assert.That(fixture.Provider).IsEqualTo("PostgreSql");
        await Assert.That(fixture.WarmupRequests).IsEqualTo(1);
        await Assert.That(fixture.ConcurrentRequests).IsEqualTo(50);
        await Assert.That(fixture.P95MaximumMilliseconds).IsEqualTo(250);
        await Assert.That(fixture.P99MaximumMilliseconds).IsEqualTo(500);
        await Assert.That(fixture.ExecutesLiveLoad).IsTrue();
    }

    private static async Task AssertTenantForeignKeyAsync(
        IEntityType dependent,
        IEntityType principal,
        params string[] dependentProperties)
    {
        await Assert.That(dependent.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType == principal &&
            Phase21PersistenceSurface.HasProperties(foreignKey.Properties, dependentProperties) &&
            Phase21PersistenceSurface.HasProperties(
                foreignKey.PrincipalKey.Properties,
                nameof(AdmissionTarget.TenantId),
                nameof(AdmissionTarget.Id)) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
    }

    private static async Task AssertScopedTargetForeignKeyAsync(
        IEntityType target,
        Type principalType,
        string scopeProperty)
    {
        await Assert.That(target.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == principalType &&
            Phase21PersistenceSurface.HasProperties(
                foreignKey.Properties,
                nameof(AdmissionTarget.TenantId),
                nameof(AdmissionTarget.EventId),
                scopeProperty) &&
            Phase21PersistenceSurface.HasProperties(
                foreignKey.PrincipalKey.Properties,
                nameof(AdmissionTarget.TenantId),
                nameof(AdmissionTarget.EventId),
                nameof(AdmissionTarget.Id)) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
    }
}

[NotInParallel("PersistenceDb")]
[Category("Phase21AdmissionCheckInConstraintRuntime")]
public sealed class AdmissionCheckInConstraintRuntimeTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("TenantId")]
    [Arguments("AdmissionTicketId")]
    [Arguments("AdmissionTargetId")]
    public async Task ActiveCheckInFactMustMatchTheStatesTenantTicketAndTarget(string mismatchedProperty)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        Guid stateTenantId = Guid.CreateVersion7();
        Guid factTenantId = mismatchedProperty == "TenantId" ? Guid.CreateVersion7() : stateTenantId;
        Guid stateTicketId = Guid.CreateVersion7();
        Guid factTicketId = mismatchedProperty == "AdmissionTicketId" ? Guid.CreateVersion7() : stateTicketId;
        Guid stateTargetId = Guid.CreateVersion7();
        Guid factTargetId = mismatchedProperty == "AdmissionTargetId" ? Guid.CreateVersion7() : stateTargetId;
        Guid activeFactId = Guid.CreateVersion7();
        Guid targetEventId = Guid.CreateVersion7();
        await using var context = CreateContext(connection, stateTenantId);
        await context.Database.EnsureCreatedAsync();
        Phase21Entities entities = Phase21PersistenceSurface.RequireEntities(context.Model);
        await Phase21PersistenceSurface.InsertWithForeignKeysDisabledAsync(
            context,
            (entities.Ticket, new Dictionary<string, object?>
            {
                ["TenantId"] = stateTenantId,
                ["Id"] = stateTicketId,
                ["AdmissionTicketStatusId"] = 1
            }),
            (entities.Target, new Dictionary<string, object?>
            {
                ["TenantId"] = stateTenantId,
                ["Id"] = stateTargetId,
                ["EventId"] = targetEventId,
                ["AdmissionTargetTypeId"] = 1,
                ["ScopeId"] = targetEventId
            }),
            (entities.CheckInEvent, new Dictionary<string, object?>
            {
                ["TenantId"] = factTenantId,
                ["Id"] = activeFactId,
                ["AdmissionTicketId"] = factTicketId,
                ["AdmissionTargetId"] = factTargetId,
                ["Sequence"] = 1L,
                ["AdmissionCheckInActionId"] = 1,
                ["ActorId"] = Guid.CreateVersion7(),
                ["OccurredAtUtc"] = UtcNow
            }));
        object state = Phase21PersistenceSurface.CreateEntity(
            context,
            entities.State,
            new Dictionary<string, object?>
            {
                ["TenantId"] = stateTenantId,
                ["Id"] = Guid.CreateVersion7(),
                ["AdmissionTicketId"] = stateTicketId,
                ["AdmissionTargetId"] = stateTargetId,
                ["ActiveCheckInEventId"] = activeFactId,
                ["EntryCount"] = 1,
                ["LastSequence"] = 1L
            });
        context.Add(state);

        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    public async Task ScannerCapabilityMustMatchItsTargetsTenantEventAndIdentity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid targetEventId = Guid.CreateVersion7();
        Guid capabilityEventId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        await using var context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        Phase21Entities entities = Phase21PersistenceSurface.RequireEntities(context.Model);
        IEntityType tenant = context.Model.FindEntityType(typeof(Tenant))!;
        IEntityType @event = context.Model.FindEntityType(typeof(Explore.Domain.Event))!;
        IEntityType actor = context.Model.FindEntityType(typeof(Actor))!;
        await Phase21PersistenceSurface.InsertWithForeignKeysDisabledAsync(
            context,
            (tenant, new Dictionary<string, object?> { ["Id"] = tenantId }),
            (@event, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = capabilityEventId
            }),
            (actor, new Dictionary<string, object?> { ["Id"] = actorId }),
            (entities.Target, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = targetId,
                ["EventId"] = targetEventId,
                ["AdmissionTargetTypeId"] = 1,
                ["ScopeId"] = targetEventId
            }));
        AdmissionScannerCapability capability = AdmissionScannerCapability.Issue(
            Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(), capabilityEventId, targetId, 7,
            Phase21PersistenceSurface.Digest(0x67), "Wrong event scanner",
            AdmissionScannerCapabilityAction.CheckIn, UtcNow.AddHours(1), actorId, UtcNow);
        context.Add(capability);

        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    public async Task DuplicateCanonicalTicketTypeEntitlementsAreRejectedByTheDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid ticketTypeId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        await using var context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        IEntityType entitlement = context.Model.FindEntityType(typeof(TicketTypeEntitlement))!;
        Dictionary<string, object?> Values() => new()
        {
            ["TenantId"] = tenantId,
            ["Id"] = Guid.CreateVersion7(),
            ["TicketTypeId"] = ticketTypeId,
            ["TargetEventId"] = eventId,
            ["EntitlementScopeTypeId"] = 1,
            ["ScopeId"] = eventId,
            ["IncludedQuantity"] = 1,
            ["EntitlementSelectionRuleId"] = 1
        };
        await Phase21PersistenceSurface.InsertWithForeignKeysDisabledAsync(context, (entitlement, Values()));

        await Assert.That(() => Phase21PersistenceSurface.InsertWithForeignKeysDisabledAsync(
            context, (entitlement, Values()))).Throws<DbUpdateException>();
    }

    [Test]
    public async Task PortableChecksRejectMalformedTargetActionAuthorityAndCompensationShapes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using var context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");

        Guid eventId = Guid.CreateVersion7();
        await Assert.That(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admission_targets
                (id, tenant_id, event_id, admission_target_type_id, admission_operational_status_id,
                 scope_id, event_day_id, event_session_id, concurrency_stamp)
            VALUES
                ({Guid.CreateVersion7()}, {tenantId}, {eventId}, 2, 1,
                 {eventId}, NULL, NULL, {Guid.CreateVersion7()})
            """)).Throws<SqliteException>();

        await RejectFactAsync(99, Guid.CreateVersion7(), null, null, null);
        await RejectFactAsync(1, null, null, null, null);
        await RejectFactAsync(1, Guid.CreateVersion7(), null, 1, null);
        await RejectFactAsync(2, Guid.CreateVersion7(), null, 1, null);

        async Task RejectFactAsync(
            int action,
            Guid? actorId,
            Guid? scannerCapabilityId,
            int? reasonCode,
            Guid? compensatedCheckInEventId)
        {
            await Assert.That(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admission_check_in_events
                    (id, tenant_id, admission_ticket_id, admission_target_id, sequence,
                     admission_check_in_action_id, actor_id, scanner_capability_id,
                     admission_check_in_undo_reason_code_id,
                     occurred_at_utc, compensated_check_in_event_id)
                VALUES
                    ({Guid.CreateVersion7()}, {tenantId}, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, 1,
                     {action}, {actorId}, {scannerCapabilityId}, {reasonCode},
                     {UtcNow}, {compensatedCheckInEventId})
                """)).Throws<SqliteException>();
        }
    }

    private static ExploreDbContext CreateContext(SqliteConnection connection, Guid tenantId) => new(
        TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options)
    {
        TenantContext = new TestTenantContext(tenantId)
    };

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
[Category("Phase21AdmissionCheckInPostgreSqlRed")]
public sealed class AdmissionCheckInPostgreSqlRedTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task SameDigestAcrossTenantsResolvesOnlyInsideSelectedTenantWithGenericMisses()
    {
        await fixture.ResetAsync();
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x31);
        await SeedCredentialAsync(tenantA, digest);
        await SeedCredentialAsync(tenantB, digest);

        await using ExploreDbContext contextA = TenantContext(tenantA);
        var repositoryA = new AdmissionCheckInRepository(contextA);
        (int KeyVersion, string Digest)[] candidates = [(7, digest)];
        AdmissionTicket? foundA = await repositoryA.ResolveCredentialAsync(
            tenantA, candidates, CancellationToken.None);
        AdmissionTicket? blockedB = await repositoryA.ResolveCredentialAsync(
            tenantB, candidates, CancellationToken.None);
        AdmissionTicket? absent = await repositoryA.ResolveCredentialAsync(
            tenantA,
            [(6, Phase21PersistenceSurface.Digest(0x32)), (7, Phase21PersistenceSurface.Digest(0x33))],
            CancellationToken.None);

        await using ExploreDbContext contextB = TenantContext(tenantB);
        AdmissionTicket? foundB = await new AdmissionCheckInRepository(contextB)
            .ResolveCredentialAsync(tenantB, candidates, CancellationToken.None);

        await Assert.That(foundA).IsNotNull();
        await Assert.That(foundB).IsNotNull();
        await Assert.That(foundA!.TenantId).IsEqualTo(tenantA);
        await Assert.That(foundB!.TenantId).IsEqualTo(tenantB);
        await Assert.That(blockedB).IsNull();
        await Assert.That(absent).IsNull();
    }

    [Test]
    public async Task SingleScanUsesExactlyOneCredentialLookupQueryForBoundedCandidatePairs()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x41);
        await SeedCredentialAsync(tenantId, digest);
        var counter = new CredentialLookupCommandCounter();
        await using ExploreDbContext context = TenantContext(tenantId, counter);
        var repository = new AdmissionCheckInRepository(context);

        (int KeyVersion, string Digest)[] candidates =
        [
            (6, Phase21PersistenceSurface.Digest(0x40)),
            (7, digest)
        ];
        AdmissionTicket? result = await repository.ResolveCredentialAsync(
            tenantId, candidates, CancellationToken.None);

        await Assert.That(candidates.Length).IsLessThanOrEqualTo(4);
        await Assert.That(result).IsNotNull();
        await Assert.That(counter.CredentialLookupQueries).IsEqualTo(1);
        await Assert.That(counter.TotalReaderQueries).IsEqualTo(1);
    }

    [Test]
    public async Task DefaultCheckInCompositionRejectsMissingReadinessWithoutEffects()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x42);
        await SeedCheckInPrerequisitesAsync(
            tenantId,
            eventId,
            ticketId,
            targetId,
            digest);

        await using ExploreDbContext context = TenantContext(tenantId);
        AdmissionCheckInDecision? decision = await ExecuteCheckInAsync(
            context,
            tenantId,
            eventId,
            targetId,
            digest,
            AdmissionCheckInAction.CheckIn,
            reasonCode: null,
            UtcNow,
            CancellationToken.None,
            useDefaultReadiness: true);

        await Assert.That(decision).IsNull();
        await Assert.That(await context.AdmissionCheckInEvents.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AdmissionCheckInStates.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task DetailReadReturnsExactEventFactAndItsActiveEventOnly()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x45);
        await SeedCheckInPrerequisitesAsync(tenantId, eventId, ticketId, targetId, digest);
        await using ExploreDbContext writeContext = TenantContext(tenantId);
        AdmissionCheckInDecision? decision = await ExecuteCheckInAsync(
            writeContext,
            tenantId,
            eventId,
            targetId,
            digest,
            AdmissionCheckInAction.CheckIn,
            null,
            UtcNow,
            CancellationToken.None);
        Guid checkInId = decision!.Event!.Id;

        await using ExploreDbContext readContext = TenantContext(tenantId);
        var repository = new AdmissionCheckInReportingRepository(readContext);
        AdmissionCheckInEvent? fact = await repository.GetEventAsync(
            tenantId,
            eventId,
            checkInId,
            CancellationToken.None);
        AdmissionCheckInEvent? activeEvent = await repository.GetActiveEventAsync(
            tenantId,
            ticketId,
            targetId,
            CancellationToken.None);
        AdmissionCheckInEvent? wrongEvent = await repository.GetEventAsync(
            tenantId,
            Guid.CreateVersion7(),
            checkInId,
            CancellationToken.None);

        await Assert.That(fact?.Id).IsEqualTo(checkInId);
        await Assert.That(activeEvent?.Id).IsEqualTo(checkInId);
        await Assert.That(wrongEvent).IsNull();
    }

    [Test]
    public async Task SummaryUsesOneScalarRedactedQueryAndExactTenantEventTargetLineage()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x63);
        await SeedCheckInPrerequisitesAsync(tenantId, eventId, ticketId, targetId, digest);
        Guid checkInId;
        await using (ExploreDbContext checkInContext = TenantContext(tenantId))
        {
            AdmissionCheckInDecision? decision = await ExecuteCheckInAsync(
                checkInContext, tenantId, eventId, targetId, digest,
                AdmissionCheckInAction.CheckIn, null, UtcNow, CancellationToken.None);
            checkInId = decision!.Event!.Id;
        }
        await using (ExploreDbContext undoContext = TenantContext(tenantId))
        {
            _ = await ExecuteCheckInAsync(
                undoContext, tenantId, eventId, targetId, digest,
                AdmissionCheckInAction.Undo,
                AdmissionCheckInUndoReasonCodeEnum.OperatorCorrection,
                UtcNow.AddMinutes(1),
                CancellationToken.None, checkInId);
        }

        var counter = new CapturingReaderCommandCounter();
        await using ExploreDbContext context = TenantContext(tenantId, counter);
        var repository = new AdmissionCheckInReportingRepository(context);
        AdmissionCheckInSummaryProjection? summary = await repository.GetAsync(
            tenantId, eventId, targetId, CancellationToken.None);

        await Assert.That(summary).IsNotNull();
        await Assert.That(summary!.CheckedInCount).IsEqualTo(1);
        await Assert.That(summary.UndoneCount).IsEqualTo(1);
        await Assert.That(summary.ActiveStateCount).IsEqualTo(0);
        await Assert.That(summary.InactiveStateCount).IsEqualTo(1);
        await Assert.That(summary.LastActivityUtc).IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(counter.ReaderQueries).IsEqualTo(1);
        await Assert.That(counter.LastCommandText).Contains("admission_targets");
        await Assert.That(counter.LastCommandText).Contains("admission_check_in_events");
        await Assert.That(counter.LastCommandText).Contains("admission_check_in_states");
        await Assert.That(counter.LastCommandText).DoesNotContain("reason");
        await Assert.That(counter.LastCommandText).DoesNotContain("actor_id");
        await Assert.That(counter.LastCommandText).DoesNotContain("scanner_capability_id");
        await Assert.That(await repository.GetAsync(
            tenantId, eventId, Guid.CreateVersion7(), CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetAsync(
            Guid.CreateVersion7(), eventId, targetId, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task AuditPageIsBoundedDeterministicTenantQualifiedKeysetPagedAndExportShapeIsRedacted()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x64);
        await SeedCheckInPrerequisitesAsync(tenantId, eventId, ticketId, targetId, digest);
        Guid checkInId;
        await using (ExploreDbContext checkInContext = TenantContext(tenantId))
        {
            AdmissionCheckInDecision? decision = await ExecuteCheckInAsync(
                checkInContext, tenantId, eventId, targetId, digest,
                AdmissionCheckInAction.CheckIn, null, UtcNow, CancellationToken.None);
            checkInId = decision!.Event!.Id;
        }
        await using (ExploreDbContext undoContext = TenantContext(tenantId))
        {
            _ = await ExecuteCheckInAsync(
                undoContext, tenantId, eventId, targetId, digest,
                AdmissionCheckInAction.Undo,
                AdmissionCheckInUndoReasonCodeEnum.OperatorCorrection,
                UtcNow.AddMinutes(1),
                CancellationToken.None, checkInId);
        }

        var counter = new CapturingReaderCommandCounter();
        await using ExploreDbContext context = TenantContext(tenantId, counter);
        var repository = new AdmissionCheckInReportingRepository(context);
        IReadOnlyList<AdmissionCheckInEvent> page = await repository.ListEventAuditPageAsync(
            tenantId, eventId, null, 2, CancellationToken.None);
        IReadOnlyList<AdmissionTarget> targets = await repository.ListTargetsAsync(
            tenantId, eventId, [targetId], CancellationToken.None);
        await using ExploreDbContext isolationContext = TenantContext(tenantId);
        var isolationRepository = new AdmissionCheckInReportingRepository(isolationContext);
        IReadOnlyList<AdmissionCheckInEvent> next = await isolationRepository.ListEventAuditPageAsync(
            tenantId, eventId, page[0], 1, CancellationToken.None);
        IReadOnlyList<AdmissionCheckInEvent> wrongTenant = await isolationRepository.ListEventAuditPageAsync(
            Guid.CreateVersion7(), eventId, null, 2, CancellationToken.None);
        await Assert.That(counter.ReaderQueries).IsEqualTo(2);
        await Assert.That(page.Count).IsEqualTo(2);
        await Assert.That(page[0].OccurredAtUtc).IsGreaterThan(page[1].OccurredAtUtc);
        await Assert.That(next).HasSingleItem();
        await Assert.That(next[0].Id).IsEqualTo(page[1].Id);
        await Assert.That(wrongTenant).IsEmpty();
        await Assert.That(targets).HasSingleItem();
        await Assert.That(targets[0].Id).IsEqualTo(targetId);
        string[] exportProperties = typeof(AdmissionCheckInAuditItem).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        await Assert.That(exportProperties).IsEquivalentTo(
            ["Cursor", "Action", "Outcome", "TargetType", "OccurredAtTimeBucketUtc"]);
    }

    [Test]
    public async Task AuditKeysetDoesNotDuplicateOrOmitFactsWhenCheckInArrivesBetweenPages()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x65);
        await SeedCheckInPrerequisitesAsync(tenantId, eventId, ticketId, targetId, digest);
        Guid initialCheckInId;
        await using (ExploreDbContext checkInContext = TenantContext(tenantId))
        {
            AdmissionCheckInDecision? decision = await ExecuteCheckInAsync(
                checkInContext, tenantId, eventId, targetId, digest,
                AdmissionCheckInAction.CheckIn, null, UtcNow, CancellationToken.None);
            initialCheckInId = decision!.Event!.Id;
        }
        await using (ExploreDbContext undoContext = TenantContext(tenantId))
        {
            _ = await ExecuteCheckInAsync(
                undoContext, tenantId, eventId, targetId, digest,
                AdmissionCheckInAction.Undo,
                AdmissionCheckInUndoReasonCodeEnum.OperatorCorrection,
                UtcNow.AddMinutes(1),
                CancellationToken.None, initialCheckInId);
        }

        AdmissionCheckInEvent[] firstRead;
        await using (ExploreDbContext firstContext = TenantContext(tenantId))
        {
            var repository = new AdmissionCheckInReportingRepository(firstContext);
            firstRead = (await repository.ListEventAuditPageAsync(
                tenantId, eventId, null, 2, CancellationToken.None)).ToArray();
        }
        await Assert.That(firstRead).HasCount(2);
        AdmissionCheckInEvent firstVisible = firstRead[0];
        AdmissionCheckInEvent expectedSecondVisible = firstRead[1];

        await using (ExploreDbContext insertedContext = TenantContext(tenantId))
        {
            _ = await ExecuteCheckInAsync(
                insertedContext, tenantId, eventId, targetId, digest,
                AdmissionCheckInAction.CheckIn, null, UtcNow.AddMinutes(2),
                CancellationToken.None);
        }

        await using ExploreDbContext nextContext = TenantContext(tenantId);
        var nextRepository = new AdmissionCheckInReportingRepository(nextContext);
        IReadOnlyList<AdmissionCheckInEvent> secondPage = await nextRepository.ListEventAuditPageAsync(
            tenantId, eventId, firstVisible, 2, CancellationToken.None);

        await Assert.That(secondPage).HasSingleItem();
        await Assert.That(secondPage[0].Id).IsEqualTo(expectedSecondVisible.Id);
        await Assert.That(new[] { firstVisible.Id, secondPage[0].Id }.Distinct().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task CredentialRotatedAfterDigestResolutionCannotAuthorizeCheckIn()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x60);
        await SeedCheckInPrerequisitesAsync(tenantId, eventId, ticketId, targetId, digest);
        var resolution = new HeldCredentialResolutionInterceptor();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using ExploreDbContext scanContext = TenantContext(tenantId, resolution);

        Task<AdmissionCheckInDecision?> scan = ExecuteCheckInAsync(
            scanContext,
            tenantId,
            eventId,
            targetId,
            digest,
            AdmissionCheckInAction.CheckIn,
            null,
            UtcNow,
            timeout.Token);
        await resolution.Resolved.WaitAsync(timeout.Token);

        await using (ExploreDbContext rotationContext = TenantContext(tenantId))
        await using (IDbContextTransaction transaction =
                     await rotationContext.Database.BeginTransactionAsync(timeout.Token))
        {
            await rotationContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE admission_ticket_credentials
                SET admission_ticket_credential_status_id = 2, revoked_at = {UtcNow.AddSeconds(1)}
                WHERE tenant_id = {tenantId} AND admission_ticket_id = {ticketId}
                  AND admission_ticket_credential_status_id = 1
                """, timeout.Token);
            await rotationContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE admission_tickets
                SET concurrency_stamp = {Guid.CreateVersion7()}
                WHERE tenant_id = {tenantId} AND id = {ticketId}
                """, timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }

        resolution.Release();
        AdmissionCheckInDecision? outcome = await scan;

        await Assert.That(outcome).IsNull();
        await Assert.That(resolution.CredentialQueries).IsEqualTo(1);
        await using ExploreDbContext verification = TenantContext(tenantId);
        await Assert.That(await verification.AdmissionCheckInEvents.CountAsync())
            .IsEqualTo(0);
    }

    [Test]
    public async Task StopFencesTargetBeforeScanSoWaitingScanObservesStoppedState()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x5f);
        await SeedCheckInPrerequisitesAsync(tenantId, eventId, ticketId, targetId, digest);
        await using ExploreDbContext metadata = fixture.CreateDbContext();
        string targetTable = Phase21PersistenceSurface.DelimitedTableIdentifier(
            metadata,
            Phase21PersistenceSurface.RequireEntities(metadata.GetService<IDesignTimeModel>().Model).Target);
        var stopLock = new HeldStateLockInterceptor(targetTable);
        var scanAttempt = new StateLockAttemptInterceptor(targetTable);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Task stop = StopAsync();
        await stopLock.LockAcquired.WaitAsync(timeout.Token);
        Task<AdmissionCheckInDecision?> scan = ScanAsync();
        await scanAttempt.LockAttempted.WaitAsync(timeout.Token);
        stopLock.Release();
        await stop;
        AdmissionCheckInDecision? outcome = await scan;

        await Assert.That(outcome?.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.AdmissionStopped);
        await Assert.That(outcome?.Event).IsNull();
        await using ExploreDbContext verification = TenantContext(tenantId);
        await Assert.That(await verification.AdmissionCheckInEvents.CountAsync())
            .IsEqualTo(0);

        async Task StopAsync()
        {
            await using ExploreDbContext context = TenantContext(tenantId, stopLock);
            await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async token =>
            {
                var repository = new AdmissionTargetOperationsRepository(context);
                AdmissionTarget target = await repository.GetAsync(
                    tenantId, eventId, targetId, token) ?? throw new InvalidOperationException("Missing target.");
                target.Stop();
                await repository.UpdateAsync(target, token);
                await context.SaveChangesAsync(token);
            }, timeout.Token);
        }

        async Task<AdmissionCheckInDecision?> ScanAsync()
        {
            await using ExploreDbContext context = TenantContext(tenantId, scanAttempt);
            return await ExecuteCheckInAsync(
                context, tenantId, eventId, targetId, digest,
                AdmissionCheckInAction.CheckIn, null, UtcNow, timeout.Token);
        }
    }

    [Test]
    public async Task ConcurrentDuplicateCheckInsHaveOneCheckedInAndOneAlreadyCheckedInOutcome()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x61);
        await SeedCheckInPrerequisitesAsync(tenantId, eventId, ticketId, targetId, digest);
        await using ExploreDbContext metadata = fixture.CreateDbContext();
        string ticketTable = Phase21PersistenceSurface.DelimitedTableIdentifier(
            metadata,
            Phase21PersistenceSurface.RequireEntities(metadata.GetService<IDesignTimeModel>().Model).Ticket);
        var firstLock = new HeldStateLockInterceptor(ticketTable);
        var secondLock = new StateLockAttemptInterceptor(ticketTable);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Task<AdmissionCheckInDecision?> first = CheckInAsync(firstLock);
        await firstLock.LockAcquired.WaitAsync(timeout.Token);
        Task<AdmissionCheckInDecision?> second = CheckInAsync(secondLock);
        await secondLock.LockAttempted.WaitAsync(timeout.Token);
        await Assert.That(second.IsCompleted).IsFalse();
        firstLock.Release();
        AdmissionCheckInDecision?[] outcomes = await Task.WhenAll(first, second);

        await Assert.That(outcomes.Count(value => value?.ResultCode == AdmissionCheckInResultCodeEnum.CheckedIn))
            .IsEqualTo(1);
        await Assert.That(outcomes.Count(value => value?.ResultCode == AdmissionCheckInResultCodeEnum.AlreadyCheckedIn))
            .IsEqualTo(1);
        await using ExploreDbContext verification = TenantContext(tenantId);
        await Assert.That(await verification.AdmissionCheckInStates.CountAsync()).IsEqualTo(1);
        await Assert.That(await verification.AdmissionCheckInEvents.CountAsync()).IsEqualTo(1);

        async Task<AdmissionCheckInDecision?> CheckInAsync(IInterceptor interceptor)
        {
            await using ExploreDbContext context = TenantContext(tenantId, interceptor);
            return await ExecuteCheckInAsync(
                context,
                tenantId,
                eventId,
                targetId,
                digest,
                AdmissionCheckInAction.CheckIn,
                null,
                UtcNow,
                timeout.Token);
        }
    }

    [Test]
    public async Task UndoHoldingTheStateLockThenConcurrentCheckInDeterministicallyEndsActiveAndPreservesAllEvents()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x62);
        await SeedCheckInPrerequisitesAsync(tenantId, eventId, ticketId, targetId, digest);
        Guid initialCheckInId;
        await using (ExploreDbContext initialContext = TenantContext(tenantId))
        {
            AdmissionCheckInDecision? initial = await ExecuteCheckInAsync(
                initialContext,
                tenantId,
                eventId,
                targetId,
                digest,
                AdmissionCheckInAction.CheckIn,
                null,
                UtcNow,
                CancellationToken.None);
            await Assert.That(initial!.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.CheckedIn);
            initialCheckInId = initial!.Event!.Id;
        }

        await using ExploreDbContext metadata = fixture.CreateDbContext();
        Phase21Entities metadataEntities = Phase21PersistenceSurface.RequireEntities(
            metadata.GetService<IDesignTimeModel>().Model);
        string stateTable = Phase21PersistenceSurface.DelimitedTableIdentifier(metadata, metadataEntities.State);
        string ticketTable = Phase21PersistenceSurface.DelimitedTableIdentifier(metadata, metadataEntities.Ticket);
        var undoLock = new HeldStateLockInterceptor(stateTable);
        var checkInLock = new StateLockAttemptInterceptor(ticketTable);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<AdmissionCheckInDecision?> undo = UndoAsync();
        await undoLock.LockAcquired.WaitAsync(timeout.Token);
        Task<AdmissionCheckInDecision?> checkIn = CheckInAsync();
        await checkInLock.LockAttempted.WaitAsync(timeout.Token);
        await Assert.That(checkIn.IsCompleted).IsFalse();
        undoLock.Release();
        AdmissionCheckInDecision?[] outcomes = await Task.WhenAll(undo, checkIn);

        await Assert.That(outcomes[0]!.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.Undone);
        await Assert.That(outcomes[1]!.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.ReEntered);
        await using ExploreDbContext verification = TenantContext(tenantId);
        await Assert.That(await verification.AdmissionCheckInEvents.CountAsync()).IsEqualTo(3);
        await Assert.That(await verification.AdmissionCheckInStates.CountAsync()).IsEqualTo(1);

        async Task<AdmissionCheckInDecision?> UndoAsync()
        {
            await using ExploreDbContext context = TenantContext(tenantId, undoLock);
            return await ExecuteCheckInAsync(
                context,
                tenantId,
                eventId,
                targetId,
                digest,
                AdmissionCheckInAction.Undo,
                AdmissionCheckInUndoReasonCodeEnum.OperatorCorrection,
                UtcNow.AddSeconds(1),
                timeout.Token,
                initialCheckInId);
        }

        async Task<AdmissionCheckInDecision?> CheckInAsync()
        {
            await using ExploreDbContext context = TenantContext(tenantId, checkInLock);
            return await ExecuteCheckInAsync(
                context,
                tenantId,
                eventId,
                targetId,
                digest,
                AdmissionCheckInAction.CheckIn,
                null,
                UtcNow.AddSeconds(2),
                timeout.Token);
        }
    }

    [Test]
    public async Task StopFencesTargetBeforeScannerIssuanceSoWaitingIssueIsRejected()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        await SeedPlatformManagedTargetAsync(tenantId, eventId, targetId);
        await using ExploreDbContext metadata = fixture.CreateDbContext();
        Phase21Entities metadataEntities = Phase21PersistenceSurface.RequireEntities(
            metadata.GetService<IDesignTimeModel>().Model);
        string targetTable = Phase21PersistenceSurface.DelimitedTableIdentifier(metadata, metadataEntities.Target);
        var stopLock = new HeldStateLockInterceptor(targetTable);
        var issueAttempt = new StateLockAttemptInterceptor(targetTable);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        AdmissionScannerCapability capability = AdmissionScannerCapability.Issue(
            Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(), eventId, targetId, 7,
            Phase21PersistenceSurface.Digest(0x50), "Waiting scanner",
            AdmissionScannerCapabilityAction.CheckIn, UtcNow.AddHours(1), Guid.CreateVersion7(), UtcNow);

        Task stop = StopAsync();
        await stopLock.LockAcquired.WaitAsync(timeout.Token);
        Task<AdmissionScannerCapabilityStoreResult> issue = IssueAsync();
        await issueAttempt.LockAttempted.WaitAsync(timeout.Token);
        stopLock.Release();
        await stop;
        AdmissionScannerCapabilityStoreResult outcome = await issue;

        await Assert.That(outcome.Rejected).IsTrue();
        await Assert.That(outcome.Created).IsFalse();
        await using ExploreDbContext verification = TenantContext(tenantId);
        await Assert.That(await verification.AdmissionScannerCapabilities.CountAsync())
            .IsEqualTo(0);

        async Task StopAsync()
        {
            await using ExploreDbContext context = TenantContext(tenantId, stopLock);
            await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async token =>
            {
                var repository = new AdmissionTargetOperationsRepository(context);
                AdmissionTarget target = await repository.GetAsync(
                    tenantId, eventId, targetId, token) ?? throw new InvalidOperationException("Missing target.");
                target.Stop();
                await repository.UpdateAsync(target, token);
                await context.SaveChangesAsync(token);
            }, timeout.Token);
        }

        async Task<AdmissionScannerCapabilityStoreResult> IssueAsync()
        {
            await using ExploreDbContext context = TenantContext(tenantId, issueAttempt);
            return await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(
                token => new AdmissionScannerCapabilityRepository(context).StoreAsync(capability, token),
                timeout.Token);
        }
    }

    [Test]
    public async Task ScannerCapabilityExpiryIsReevaluatedAtThePersistenceFence()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        Guid scannerCapabilityId = Guid.CreateVersion7();
        string digest = Phase21PersistenceSurface.Digest(0x4f);
        await SeedCheckInPrerequisitesAsync(
            tenantId,
            eventId,
            ticketId,
            targetId,
            digest);
        await using (ExploreDbContext seed = TenantContext(tenantId))
        {
            Phase21Entities seedEntities = Phase21PersistenceSurface.RequireEntities(seed.Model);
            await Phase21PersistenceSurface.InsertWithForeignKeysDisabledAsync(
                seed,
                (seedEntities.Scanner, new Dictionary<string, object?>
                {
                    ["TenantId"] = tenantId,
                    ["Id"] = scannerCapabilityId,
                    ["IssueRequestId"] = Guid.CreateVersion7(),
                    ["EventId"] = eventId,
                    ["AdmissionTargetId"] = targetId,
                    ["LookupKeyVersion"] = 7,
                    ["LookupDigest"] = Phase21PersistenceSurface.Digest(0x5f),
                    ["DeviceLabel"] = "Expiry fence scanner",
                    ["AllowedActions"] = (int)AdmissionScannerCapabilityAction.CheckIn,
                    ["ExpiresAt"] = UtcNow.AddSeconds(1),
                    ["IssuedByActorId"] = Guid.CreateVersion7(),
                    ["IssuedAt"] = UtcNow
                }));
        }

        await using ExploreDbContext context = TenantContext(tenantId);
        AdmissionCheckInDecision? decision = await ExecuteCheckInAsync(
            context,
            tenantId,
            eventId,
            targetId,
            digest,
            AdmissionCheckInAction.CheckIn,
            null,
            UtcNow,
            CancellationToken.None,
            linearizedAtUtc: UtcNow.AddSeconds(2),
            scannerCapabilityId: scannerCapabilityId);

        await Assert.That(decision).IsNull();
        await Assert.That(await context.AdmissionCheckInEvents.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrentScannerCapabilityIssuanceReturnsPlaintextOnceAndPersistsOneDigestOnlyRow()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        Guid issueRequestId = Guid.CreateVersion7();
        await SeedPlatformManagedTargetAsync(tenantId, eventId, targetId);
        var barrier = new AsyncCommandBarrier(2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Task<ScannerIssuanceAttempt> first = IssueAsync(
            Guid.CreateVersion7(), "scanner-bearer-alpha", Phase21PersistenceSurface.Digest(0x51));
        Task<ScannerIssuanceAttempt> second = IssueAsync(
            Guid.CreateVersion7(), "scanner-bearer-bravo", Phase21PersistenceSurface.Digest(0x52));
        await barrier.AllArrived.WaitAsync(timeout.Token);
        barrier.Release();
        ScannerIssuanceAttempt[] outcomes = await Task.WhenAll(first, second);

        await Assert.That(outcomes.Count(outcome => outcome.Issued)).IsEqualTo(1);
        await Assert.That(outcomes.Count(outcome => !outcome.Issued)).IsEqualTo(1);
        await Assert.That(outcomes.Count(outcome => outcome.PlaintextCapability is not null)).IsEqualTo(1);
        ScannerIssuanceAttempt winner = outcomes.Single(outcome => outcome.Issued);
        ScannerIssuanceAttempt replay = outcomes.Single(outcome => !outcome.Issued);
        await Assert.That(winner.PlaintextCapability).IsNotNull();
        await Assert.That(replay.ReturnedCapabilityId).IsEqualTo(winner.ReturnedCapabilityId);
        await Assert.That(replay.ReturnedLookupDigest).IsEqualTo(winner.ReturnedLookupDigest);
        await using ExploreDbContext verification = TenantContext(tenantId);
        await Assert.That(await verification.AdmissionScannerCapabilities.CountAsync()).IsEqualTo(1);
        AdmissionScannerCapability persisted = await verification.AdmissionScannerCapabilities.SingleAsync();
        await Assert.That(persisted.IssueRequestId)
            .IsEqualTo(issueRequestId);
        await Assert.That(persisted.LookupDigest)
            .IsEqualTo(winner.LookupDigest);

        async Task<ScannerIssuanceAttempt> IssueAsync(
            Guid capabilityId,
            string plaintextCapability,
            string lookupDigest)
        {
            await barrier.ArriveAsync(timeout.Token);
            await using ExploreDbContext context = TenantContext(tenantId);
            await context.Database.OpenConnectionAsync(timeout.Token);
            await context.Database.ExecuteSqlRawAsync(
                "SET session_replication_role = replica;", timeout.Token);
            try
            {
                AdmissionScannerCapability capability = AdmissionScannerCapability.Issue(
                    capabilityId,
                    tenantId,
                    issueRequestId,
                    eventId,
                    targetId,
                    7,
                    lookupDigest,
                    "Door scanner",
                    AdmissionScannerCapabilityAction.CheckIn,
                    UtcNow.AddHours(8),
                    Guid.CreateVersion7(),
                    UtcNow);
                var repository = new AdmissionScannerCapabilityRepository(context);
                AdmissionScannerCapabilityStoreResult stored = await new EfCoreUnitOfWork(context)
                    .ExecuteInTransactionAsync(
                        token => repository.StoreAsync(capability, token),
                        timeout.Token);
                return new ScannerIssuanceAttempt(
                    stored.Created,
                    stored.Created ? plaintextCapability : null,
                    lookupDigest,
                    stored.Capability.Id,
                    stored.Capability.LookupDigest);
            }
            finally
            {
                await context.Database.ExecuteSqlRawAsync(
                    "SET session_replication_role = origin;", CancellationToken.None);
            }
        }
    }

    [Test]
    [NotInParallel]
    public async Task FiftyConcurrentSingleCheckInsMeetDeclaredPostgreSqlLatencyThresholds()
    {
        AdmissionCheckInPerformanceFixture performance = AdmissionCheckInPerformanceFixture.PostgreSqlCi;
        using var metadataCache = new MemoryCache(new MemoryCacheOptions());
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        var warmup = new AdmissionCheckInLoadCase(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Phase21PersistenceSurface.Digest(0x6f));
        await SeedCheckInPrerequisitesAsync(
            tenantId,
            warmup.EventId,
            warmup.TicketId,
            warmup.TargetId,
            warmup.Digest);
        await using (ExploreDbContext warmupContext = CreateMeasuredContext())
        {
            AdmissionCheckInResult warmupResult = await ExecuteApplicationCheckInAsync(
                warmupContext,
                tenantId,
                warmup.EventId,
                warmup.TargetId,
                warmup.Digest,
                CancellationToken.None);
            await Assert.That(warmupResult.Outcome).IsEqualTo(AdmissionCheckInOutcome.CheckedIn);
        }

        var cases = new List<AdmissionCheckInLoadCase>(performance.ConcurrentRequests);
        for (var index = 0; index < performance.ConcurrentRequests; index++)
        {
            var loadCase = new AdmissionCheckInLoadCase(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Phase21PersistenceSurface.Digest((byte)(0x70 + index)));
            cases.Add(loadCase);
            await SeedCheckInPrerequisitesAsync(
                tenantId,
                loadCase.EventId,
                loadCase.TicketId,
                loadCase.TargetId,
                loadCase.Digest,
                includeTenant: false);
        }

        var counters = cases.Select(_ => new CredentialLookupCommandCounter()).ToArray();
        var warmContexts = counters.Select(counter => CreateMeasuredContext(counter)).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            foreach (ExploreDbContext context in warmContexts)
            {
                _ = context.Model;
            }
            await Task.WhenAll(warmContexts.Select(context =>
                context.Database.OpenConnectionAsync(CancellationToken.None)));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Task<AdmissionCheckInPerformanceSample>[] requests = cases
                .Select((loadCase, index) => ExecuteMeasuredCheckInAsync(
                    loadCase, warmContexts[index], counters[index], timeout.Token))
                .ToArray();
            start.SetResult();
            AdmissionCheckInPerformanceSample[] samples = await Task.WhenAll(requests);
            double[] elapsedMilliseconds = samples
                .Select(sample => sample.Elapsed.TotalMilliseconds)
                .Order()
                .ToArray();
            double p95 = Percentile(elapsedMilliseconds, 0.95);
            double p99 = Percentile(elapsedMilliseconds, 0.99);
            Console.WriteLine(
                $"Phase 21 PostgreSQL latency: count={samples.Length}, p95={p95:F3}ms, p99={p99:F3}ms.");
            Console.WriteLine(
                "Phase 21 credential-query distribution: " +
                string.Join(", ", samples
                    .GroupBy(sample => sample.CredentialLookupQueries)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}={group.Count()}")) + ".");

            await Assert.That(samples).HasCount(performance.ConcurrentRequests);
            await Assert.That(samples.All(sample =>
                sample.Result.Outcome == AdmissionCheckInOutcome.CheckedIn)).IsTrue();
            await Assert.That(samples.All(sample => sample.CredentialLookupQueries == 1)).IsTrue();
            await Assert.That(p95).IsLessThanOrEqualTo(performance.P95MaximumMilliseconds);
            await Assert.That(p99).IsLessThanOrEqualTo(performance.P99MaximumMilliseconds);
        }
        finally
        {
            await Task.WhenAll(warmContexts.Select(context => context.DisposeAsync().AsTask()));
        }

        async Task<AdmissionCheckInPerformanceSample> ExecuteMeasuredCheckInAsync(
            AdmissionCheckInLoadCase loadCase,
            ExploreDbContext context,
            CredentialLookupCommandCounter counter,
            CancellationToken cancellationToken)
        {
            await start.Task.WaitAsync(cancellationToken);
            long startedAt = Stopwatch.GetTimestamp();
            AdmissionCheckInResult result = await ExecuteApplicationCheckInAsync(
                context,
                tenantId,
                loadCase.EventId,
                loadCase.TargetId,
                loadCase.Digest,
                cancellationToken);
            return new AdmissionCheckInPerformanceSample(
                result,
                Stopwatch.GetElapsedTime(startedAt),
                counter.CredentialLookupQueries);
        }

        ExploreDbContext CreateMeasuredContext(CredentialLookupCommandCounter? counter = null)
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .UseMemoryCache(metadataCache);
            if (counter is not null)
            {
                options.AddInterceptors(counter);
            }
            return new ExploreDbContext(options.Options)
            {
                TenantContext = new TestTenantContext(tenantId)
            };
        }
    }

    private static double Percentile(IReadOnlyList<double> orderedValues, double percentile)
    {
        int nearestRankIndex = (int)Math.Ceiling(percentile * orderedValues.Count) - 1;
        return orderedValues[Math.Clamp(nearestRankIndex, 0, orderedValues.Count - 1)];
    }

    private async Task SeedCredentialAsync(Guid tenantId, string digest)
    {
        await using ExploreDbContext context = TenantContext(tenantId);
        Phase21Entities entities = Phase21PersistenceSurface.RequireEntities(context.Model);
        Guid ticketId = Guid.CreateVersion7();
        await Phase21PersistenceSurface.InsertWithForeignKeysDisabledAsync(
            context,
            (entities.Ticket, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = ticketId,
                ["AdmissionTicketStatusId"] = 1
            }),
            (entities.Credential, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = Guid.CreateVersion7(),
                ["AdmissionTicketId"] = ticketId,
                ["AdmissionTicketCredentialStatusId"] = 1,
                ["CredentialVersion"] = 1,
                ["LookupKeyVersion"] = 7,
                ["LookupDigest"] = digest
            }));
    }

    private async Task SeedPlatformManagedTargetAsync(
        Guid tenantId,
        Guid eventId,
        Guid targetId)
    {
        await using ExploreDbContext context = TenantContext(tenantId);
        Phase21Entities entities = Phase21PersistenceSurface.RequireEntities(context.Model);
        IEntityType participation = context.Model.FindEntityType(typeof(EventParticipationConfiguration))!;
        await Phase21PersistenceSurface.InsertWithForeignKeysDisabledAsync(
            context,
            (entities.Target, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = targetId,
                ["EventId"] = eventId,
                ["AdmissionTargetTypeId"] = (int)AdmissionTargetTypeEnum.Event,
                ["AdmissionOperationalStatusId"] = (int)AdmissionOperationalStatusEnum.Active,
                ["ScopeId"] = eventId,
                ["EventDayId"] = null,
                ["EventSessionId"] = null
            }),
            (participation, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = eventId,
                ["ParticipationHandlingModeId"] = (int)ParticipationHandlingModeEnum.PlatformManaged,
                ["AdvanceRegistrationObligationId"] = (int)AdvanceRegistrationObligationEnum.Required,
                ["CreatedAt"] = UtcNow,
                ["IsDeleted"] = false
            }));
    }

    private async Task SeedCheckInPrerequisitesAsync(
        Guid tenantId,
        Guid eventId,
        Guid ticketId,
        Guid targetId,
        string digest,
        bool includeTenant = true)
    {
        await using ExploreDbContext context = TenantContext(tenantId);
        Phase21Entities entities = Phase21PersistenceSurface.RequireEntities(context.Model);
        IEntityType entitlement = context.Model.FindEntityType(typeof(TicketTypeEntitlement))!;
        IEntityType tenant = context.Model.FindEntityType(typeof(Tenant))!;
        Guid eventTicketTypeId = Guid.CreateVersion7();
        var rows = new List<(IEntityType Entity, Dictionary<string, object?> Values)>();
        if (includeTenant)
        {
            rows.Add((tenant, new Dictionary<string, object?>
            {
                ["Id"] = tenantId
            }));
        }

        rows.AddRange(
        [
            (entities.Ticket, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = ticketId,
                ["EventId"] = eventId,
                ["EventTicketTypeId"] = eventTicketTypeId,
                ["AdmissionTicketStatusId"] = 1
            }),
            (entities.Credential, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = Guid.CreateVersion7(),
                ["AdmissionTicketId"] = ticketId,
                ["AdmissionTicketCredentialStatusId"] = 1,
                ["CredentialVersion"] = 1,
                ["LookupKeyVersion"] = 7,
                ["LookupDigest"] = digest
            }),
            (entities.Target, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = targetId,
                ["EventId"] = eventId,
                ["AdmissionTargetTypeId"] = 1,
                ["ScopeId"] = eventId,
                ["EventDayId"] = null,
                ["EventSessionId"] = null
            }),
            (entities.Policy, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = Guid.CreateVersion7(),
                ["AdmissionTargetId"] = targetId,
                ["OpensAtUtc"] = UtcNow.AddHours(-1),
                ["ClosesAtUtc"] = UtcNow.AddHours(1),
                ["MaximumEntries"] = 5
            }),
            (entitlement, new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["Id"] = Guid.CreateVersion7(),
                ["TicketTypeId"] = eventTicketTypeId,
                ["TargetEventId"] = eventId,
                ["EntitlementScopeTypeId"] = 1,
                ["ScopeId"] = eventId,
                ["EventDayId"] = null,
                ["EventSessionId"] = null,
                ["IncludedQuantity"] = 1,
                ["EntitlementSelectionRuleId"] = 1
            })
        ]);
        await Phase21PersistenceSurface.InsertWithForeignKeysDisabledAsync(context, rows.ToArray());
    }

    private static Task<AdmissionCheckInDecision?> ExecuteCheckInAsync(
        ExploreDbContext context,
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        string digest,
        AdmissionCheckInAction action,
        AdmissionCheckInUndoReasonCodeEnum? reasonCode,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken,
        Guid? checkInId = null,
        DateTime? linearizedAtUtc = null,
        Guid? scannerCapabilityId = null,
        bool useDefaultReadiness = false)
    {
        var timeProvider = new FixedAdmissionTimeProvider(
            linearizedAtUtc ?? occurredAtUtc);
        AdmissionCheckInRepository repository = useDefaultReadiness
            ? new AdmissionCheckInRepository(context, timeProvider)
            : new AdmissionCheckInRepository(
                context,
                timeProvider,
                ReadyParticipantAdmissionReadinessAuthority.Instance);
        return new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(
            token => repository.ExecuteAsync(
            new AdmissionCheckInTransactionRequest(
                tenantId,
                eventId,
                targetId,
                [new AdmissionCheckInCredentialDigestCandidate(digest, 7)],
                action,
                reasonCode,
                scannerCapabilityId.HasValue ? null : Guid.CreateVersion7(),
                scannerCapabilityId,
                new DateTimeOffset(occurredAtUtc),
                checkInId),
            token),
            cancellationToken);
    }

    private static Task<AdmissionCheckInResult> ExecuteApplicationCheckInAsync(
        ExploreDbContext context,
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        string digest,
        CancellationToken cancellationToken)
    {
        var service = new AdmissionCheckInService(
            new AdmissionCheckInRepository(
                context,
                new FixedAdmissionTimeProvider(UtcNow),
                ReadyParticipantAdmissionReadinessAuthority.Instance),
            new FixedAdmissionCredentialDigestService(digest),
            new AllowAdmissionCheckInAuthority(),
            new NoOpAdmissionCheckInTelemetry(),
            new EfCoreUnitOfWork(context),
            new FixedAdmissionTimeProvider(UtcNow));
        return service.ProcessAsync(
            new AdmissionCheckInRequest(
                tenantId,
                eventId,
                targetId,
                "<load-credential>",
                AdmissionCheckInAction.CheckIn,
                null,
                Guid.CreateVersion7(),
                null),
            cancellationToken);
    }

    private ExploreDbContext TenantContext(Guid tenantId, params IInterceptor[] interceptors)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention();
        if (interceptors.Length > 0)
            options.AddInterceptors(interceptors);
        return new ExploreDbContext(options.Options) { TenantContext = new TestTenantContext(tenantId) };
    }

    private sealed record ScannerIssuanceAttempt(
        bool Issued,
        string? PlaintextCapability,
        string LookupDigest,
        Guid ReturnedCapabilityId,
        string ReturnedLookupDigest);

    private sealed record AdmissionCheckInLoadCase(
        Guid EventId,
        Guid TicketId,
        Guid TargetId,
        string Digest);

    private sealed record AdmissionCheckInPerformanceSample(
        AdmissionCheckInResult Result,
        TimeSpan Elapsed,
        int CredentialLookupQueries);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}

internal sealed class FixedAdmissionCredentialDigestService(string digest)
    : IAdmissionCheckInCredentialDigestService
{
    public Task<AdmissionCheckInCredentialDigest> DigestAsync(
        AdmissionCheckInCredentialDigestRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AdmissionCheckInCredentialDigest(
            [new AdmissionCheckInCredentialDigestCandidate(digest, 7)]));
}

internal sealed class AllowAdmissionCheckInAuthority : IAdmissionCheckInAuthority
{
    public Task<AdmissionCheckInAuthorizationDecision> AuthorizeAsync(
        AdmissionCheckInAuthorizationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AdmissionCheckInAuthorizationDecision(
            AdmissionCheckInAuthorizationOutcome.Authorized,
            AdmissionTargetTypeEnum.Event));
}

internal sealed class ReadyParticipantAdmissionReadinessAuthority :
    IParticipantAdmissionReadinessAuthority
{
    internal static ReadyParticipantAdmissionReadinessAuthority Instance { get; } = new();

    public Task<ParticipantAdmissionReadinessDecision?> EvaluateForUpdateAsync(
        Guid tenantId,
        Guid registrationTicketAssignmentId,
        bool orderConfirmed,
        bool paymentSatisfied,
        CancellationToken cancellationToken) =>
        Task.FromResult<ParticipantAdmissionReadinessDecision?>(
            new(ParticipantAdmissionReadinessCode.Ready));
}

internal sealed class NoOpAdmissionCheckInTelemetry : IAdmissionCheckInTelemetry
{
    public void RecordOperation(
        AdmissionCheckInAction action,
        AdmissionCheckInAuthorityKind authorityKind,
        AdmissionTargetTypeEnum? targetType,
        AdmissionCheckInTelemetryOutcome outcome,
        double durationMilliseconds)
    {
    }

    public void RecordBatch(
        AdmissionCheckInAuthorityKind authorityKind,
        AdmissionTargetTypeEnum? targetType,
        int batchSize)
    {
    }

    public void RecordSaturation(
        AdmissionCheckInSaturationKind kind,
        AdmissionCheckInTelemetryOutcome outcome)
    {
    }

    public void RecordBacklog(
        AdmissionCheckInBacklogKind kind,
        AdmissionTargetTypeEnum? targetType,
        long depth)
    {
    }
}

internal sealed class FixedAdmissionTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow);
}

public sealed record AdmissionCheckInPerformanceFixture(
    string Provider,
    int WarmupRequests,
    int ConcurrentRequests,
    int P95MaximumMilliseconds,
    int P99MaximumMilliseconds,
    bool ExecutesLiveLoad)
{
    public static AdmissionCheckInPerformanceFixture PostgreSqlCi { get; } = new(
        Provider: "PostgreSql",
        WarmupRequests: 1,
        ConcurrentRequests: 50,
        P95MaximumMilliseconds: 250,
        P99MaximumMilliseconds: 500,
        ExecutesLiveLoad: true);
}

internal sealed record Phase21Entities(
    IEntityType Target,
    IEntityType Policy,
    IEntityType CheckInEvent,
    IEntityType State,
    IEntityType Scanner,
    IEntityType Ticket,
    IEntityType Credential)
{
    internal IEntityType[] TenantEntities => [Target, Policy, CheckInEvent, State, Scanner];
}

internal sealed class Phase21PersistenceSurface
{
    internal static Phase21Entities RequireEntities(IModel model)
    {
        IEntityType policy = RequireEntity(
            model,
            typeof(AdmissionCheckInPolicy));
        return new(
            RequireEntity(model, typeof(AdmissionTarget)),
            policy,
            RequireEntity(model, typeof(AdmissionCheckInEvent)),
            RequireEntity(model, typeof(AdmissionCheckInState)),
            RequireEntity(model, typeof(AdmissionScannerCapability)),
            RequireEntity(model, typeof(AdmissionTicket)),
            RequireEntity(model, typeof(AdmissionTicketCredential)));
    }

    internal static bool HasProperties(IReadOnlyList<IReadOnlyProperty> properties, params string[] names) =>
        properties.Select(property => property.Name).SequenceEqual(names);

    internal static bool IsCanonicalTargetIdentity(IReadOnlyIndex index)
    {
        string[] properties = index.Properties.Select(property => property.Name).ToArray();
        return properties.SequenceEqual(
                   [
                       nameof(AdmissionTarget.TenantId),
                       nameof(AdmissionTarget.EventId),
                       nameof(AdmissionTarget.AdmissionTargetTypeId),
                       nameof(AdmissionTarget.EventDayId),
                       nameof(AdmissionTarget.EventSessionId),
                   ]) ||
               properties.SequenceEqual(
                   [
                       nameof(AdmissionTarget.TenantId),
                       nameof(AdmissionTarget.EventId),
                       nameof(AdmissionTarget.AdmissionTargetTypeId),
                       nameof(AdmissionTarget.ScopeId),
                   ]);
    }

    internal static string DelimitedTableIdentifier(ExploreDbContext context, IEntityType entity)
    {
        string table = entity.GetTableName() ?? throw Missing($"table mapping for {entity.ClrType.FullName}");
        string? schema = entity.GetSchema();
        ISqlGenerationHelper sql = context.GetService<ISqlGenerationHelper>();
        return schema is null ? sql.DelimitIdentifier(table) : sql.DelimitIdentifier(table, schema);
    }

    internal static async Task InsertWithForeignKeysDisabledAsync(
        ExploreDbContext context,
        params (IEntityType Entity, Dictionary<string, object?> Values)[] rows)
    {
        await context.Database.OpenConnectionAsync();
        bool sqlite = context.Database.IsSqlite();
        await context.Database.ExecuteSqlRawAsync(sqlite
            ? "PRAGMA foreign_keys = OFF;"
            : "SET session_replication_role = replica;");
        try
        {
            foreach ((IEntityType entity, Dictionary<string, object?> values) in rows)
                context.Add(CreateEntity(context, entity, values));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(sqlite
                ? "PRAGMA foreign_keys = ON;"
                : "SET session_replication_role = origin;");
        }
    }

    internal static string Digest(byte fill) =>
        Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray());

    internal static ExploreDbContext CreateModelContext(string provider)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        switch (provider)
        {
            case "PostgreSql":
                builder.UseNpgsql("Host=localhost;Database=phase21_model;Username=unused");
                break;
            case "Sqlite":
                builder.UseSqlite("Data Source=:memory:");
                break;
            case "SqlServer":
                builder.UseSqlServer("Server=localhost;Database=phase21_model;Integrated Security=true;TrustServerCertificate=True");
                break;
            case "MariaDb":
                builder.UseMySql("Server=localhost;Database=phase21_model;User=unused", new MariaDbServerVersion(new Version(10, 11)));
                break;
            case "MySql":
                builder.UseMySql("Server=localhost;Database=phase21_model;User=unused", new MySqlServerVersion(new Version(8, 0)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
        return new ExploreDbContext(builder.UseSnakeCaseNamingConvention().Options);
    }

    private static IEntityType RequireEntity(
        IModel model,
        Type clrType) =>
        model.FindEntityType(clrType)
        ?? throw Missing(
            $"EF entity/configuration {clrType.FullName}");

    internal static object CreateEntity(
        ExploreDbContext context,
        IEntityType entity,
        IReadOnlyDictionary<string, object?> values)
    {
        object instance = Activator.CreateInstance(entity.ClrType, nonPublic: true)
            ?? throw Missing($"parameterless constructor for {entity.ClrType.FullName}");
        foreach (IProperty property in entity.GetProperties().Where(property => !property.IsShadowProperty()))
        {
            object? value = values.TryGetValue(property.Name, out object? supplied)
                ? supplied
                : RequiredScalarValue(property);
            if (value is not null || property.IsNullable || Nullable.GetUnderlyingType(property.ClrType) is not null)
            {
                context.Entry(instance).Property(property.Name).CurrentValue = value;
            }
        }
        return instance;
    }

    private static object? RequiredScalarValue(IProperty property)
    {
        if (property.IsNullable)
            return null;
        Type type = property.ClrType;
        if (type == typeof(Guid)) return Guid.CreateVersion7();
        if (type == typeof(string)) return $"test-{property.Name.ToLowerInvariant()}";
        if (type == typeof(DateTime)) return new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        if (type == typeof(byte[])) return Guid.CreateVersion7().ToByteArray();
        if (type.IsEnum) return Enum.ToObject(type, 1);
        return Activator.CreateInstance(type);
    }

    private static InvalidOperationException Missing(string surface) =>
        new($"Phase 21 persistence RED: missing {surface}.");
}

internal sealed class CredentialLookupCommandCounter : DbCommandInterceptor
{
    private int credentialLookupQueries;
    private int totalReaderQueries;
    internal int CredentialLookupQueries => Volatile.Read(ref credentialLookupQueries);
    internal int TotalReaderQueries => Volatile.Read(ref totalReaderQueries);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref totalReaderQueries);
        if (command.CommandText.Contains("admission_ticket_credentials", StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains("lookup_key_version", StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains("lookup_digest", StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains("tenant_id", StringComparison.OrdinalIgnoreCase))
            Interlocked.Increment(ref credentialLookupQueries);
        return ValueTask.FromResult(result);
    }
}

internal sealed class HeldCredentialResolutionInterceptor : DbCommandInterceptor
{
    private readonly TaskCompletionSource resolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int credentialQueries;
    internal Task Resolved => resolved.Task;
    internal int CredentialQueries => Volatile.Read(ref credentialQueries);
    internal void Release() => release.TrySetResult();

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("admission_ticket_credentials", StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains("lookup_digest", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref credentialQueries);
            resolved.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
        return result;
    }
}

internal sealed class CapturingReaderCommandCounter : DbCommandInterceptor
{
    private int readerQueries;
    internal int ReaderQueries => Volatile.Read(ref readerQueries);
    internal string LastCommandText { get; private set; } = string.Empty;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref readerQueries);
        LastCommandText = command.CommandText;
        return ValueTask.FromResult(result);
    }
}

internal sealed class AsyncCommandBarrier(int participants)
{
    private readonly TaskCompletionSource allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    internal Task AllArrived => allArrived.Task;

    internal async ValueTask ArriveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref arrivals) == participants)
            allArrived.TrySetResult();
        await release.Task.WaitAsync(cancellationToken);
    }

    internal void Release() => release.TrySetResult();
}

internal sealed class InsertBarrierInterceptor(AsyncCommandBarrier barrier, string tableIdentifier) : DbCommandInterceptor
{
    private int synchronized;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await AwaitInsertAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await AwaitInsertAsync(command, cancellationToken);
        return result;
    }

    private ValueTask AwaitInsertAsync(DbCommand command, CancellationToken cancellationToken) =>
        command.CommandText.Contains($"INSERT INTO {tableIdentifier}", StringComparison.OrdinalIgnoreCase) &&
        Interlocked.Exchange(ref synchronized, 1) == 0
            ? barrier.ArriveAsync(cancellationToken)
            : ValueTask.CompletedTask;
}

internal sealed class HeldStateLockInterceptor(string tableIdentifier) : DbCommandInterceptor
{
    private readonly TaskCompletionSource lockAcquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task LockAcquired => lockAcquired.Task;
    internal void Release() => release.TrySetResult();

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        await HoldAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await HoldAsync(command, cancellationToken);
        return result;
    }

    private async ValueTask HoldAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (IsStateLock(command))
        {
            lockAcquired.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private bool IsStateLock(DbCommand command) =>
        command.CommandText.Contains(tableIdentifier, StringComparison.OrdinalIgnoreCase) &&
        command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
}

internal sealed class StateLockAttemptInterceptor(string tableIdentifier) : DbCommandInterceptor
{
    private readonly TaskCompletionSource lockAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task LockAttempted => lockAttempted.Task;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        SignalAttempt(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SignalAttempt(command);
        return ValueTask.FromResult(result);
    }

    private void SignalAttempt(DbCommand command)
    {
        if (command.CommandText.Contains(tableIdentifier, StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            lockAttempted.TrySetResult();
        }
    }
}
