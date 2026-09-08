using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Migrations;

internal sealed record PopulatedIntegrationLifecycleData(
    Guid TenantId, Guid EventId, Guid UserId, Guid RowId, Guid CatalogId) : ITenantContext
{
    private static readonly DateTime CreatedAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    internal static async Task<PopulatedIntegrationLifecycleData> SeedAsync(ExploreDbContext context)
    {
        await LookupTableSeeder.SeedAsync(context);
        var data = new PopulatedIntegrationLifecycleData(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7());
        context.TenantContext = data;
        var tenant = new Tenant
        {
            Id = data.TenantId,
            FullName = "Retained integration tenant",
            Slug = "retained-integration",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var principal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(), Code = "retained-integration",
            DisplayName = "Retained integration source", ConcurrencyStamp = Guid.CreateVersion7()
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.Bot, ActorType = null!,
            ServicePrincipalId = principal.Id, ServicePrincipal = principal,
            Pii = new ActorPii { DisplayName = "Retained integration source" },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var eventRow = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = data.EventId, Title = "Retained event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id, Actor = actor, TenantId = tenant.Id, Tenant = tenant,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
            EventStatus = null!, EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
            EventTimeZoneId = "UTC", Timezone = "UTC",
            ConcurrencyStamp = Guid.CreateVersion7(), CreatedAt = CreatedAt
        };
        var user = new User
        {
            Id = data.UserId, CreatedAt = CreatedAt,
            Pii = new UserPii
            {
                Email = "retained@example.test", FirstName = "Retained", LastName = "Recipient"
            }
        };
        var tenantUser = new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
            UserId = user.Id, User = user, StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = CreatedAt, CreatedAt = CreatedAt
        };
        var catalog = EventTicketCatalogVersion.Create(tenant.Id, eventRow.Id, "USD", 1);
        catalog.CreatedAt = CreatedAt;
        catalog.ConcurrencyStamp = Guid.CreateVersion7();
        data = data with { CatalogId = catalog.Id };
        context.AddRange(tenant, eventRow, user, tenantUser, catalog);
        await context.SaveChangesAsync();

        // Today's mapped entities include columns absent at retained Init. Insert only historical
        // columns here; all observations below use ordinary EF projections on the real store.
        await context.Database.ExecuteSqlRawAsync($$"""
            INSERT INTO {{Table<EmailDispatchProcessorState>(context)}}
                (id, processor_code, is_paused, pause_reason, optional_reminders_deferred, updated_at)
            VALUES ({0}, 'email', {1}, 'retained pause', {2}, {3})
            """, data.RowId, true, false, CreatedAt);
        await context.Database.ExecuteSqlRawAsync($$"""
            INSERT INTO {{Table<EmailDispatchTenantControl>(context)}}
                (id, tenant_id, is_paused, pause_reason, created_at)
            VALUES ({0}, {1}, {2}, 'retained tenant pause', {3})
            """, data.RowId, data.TenantId, true, CreatedAt);
        await context.Database.ExecuteSqlRawAsync($$"""
            INSERT INTO {{Table<NotificationFanoutOccurrence>(context)}}
                (id, tenant_id, event_id, occurred_at, audience_cutoff_at, aggregate_version,
                 change_set_json, safe_before_snapshot_json, safe_after_snapshot_json,
                 template_key, template_version, delivery_policy_id, policy_version, priority,
                 not_before, source_type, source_id, coalescing_key, state)
            VALUES ({0}, {1}, {2}, {3}, {3}, {0}, '[]', '[]', '[]',
                    'event.updated', 1, 2, 1, 0, {3}, 'event', {2}, 'retained-event-update', 1)
            """, data.RowId, data.TenantId, data.EventId, CreatedAt);
        await context.Database.ExecuteSqlRawAsync($$"""
            INSERT INTO {{Table<NotificationIntent>(context)}}
                (id, tenant_id, category_id, ownership_type_id, recipient_kind_id, status_id,
                 template_key, deduplication_key, recipient_user_id, fanout_occurrence_id,
                 event_id, created_at, is_deleted)
            VALUES ({0}, {1}, 3, 1, 1, 1, 'event.updated', 'retained-event-update', {2},
                    {0}, {3}, {4}, {5})
            """, data.RowId, data.TenantId, data.UserId, data.EventId, CreatedAt, false);
        // MySQL-family stores materialize this portable key in SaveChanges rather than a computed column.
        string workflowKeyColumn = context.Database.IsMySql() ? ", registration_workflow_version_key" : string.Empty;
        string workflowKeyValue = context.Database.IsMySql() ? ", {6}" : string.Empty;
        await context.Database.ExecuteSqlRawAsync($$"""
            INSERT INTO {{Table<RegistrationOrder>(context)}}
                (id, tenant_id, event_id, booking_party_type_id, registration_order_status_id,
                 ticket_catalog_version_id, participation_configuration_version_snapshot,
                 participation_handling_mode_id_snapshot, advance_registration_obligation_id_snapshot,
                 registration_order_participation_configuration_version_snapshot, currency_code,
                 pre_discount_organizer_directed_total_minor_snapshot, promotion_discount_total_minor_snapshot,
                 post_discount_organizer_directed_total_minor_snapshot, organizer_directed_total_minor_snapshot,
                 platform_fee_total_minor_snapshot, organizer_earnings_total_minor_snapshot,
                 platform_contribution_total_minor_snapshot, total_due_minor_snapshot,
                 add_on_total_minor_snapshot, concurrency_stamp, created_at, is_deleted{{workflowKeyColumn}})
            VALUES ({0}, {1}, {2}, 1, 1, {3}, {0}, 1, 1, {0}, 'USD',
                    1250, 0, 1250, 1250, 0, 1250, 0, 1250, 0, {0}, {4}, {5}{{workflowKeyValue}})
            """, data.RowId, data.TenantId, data.EventId, data.CatalogId, CreatedAt, false, Guid.Empty);
        await context.Database.ExecuteSqlRawAsync($$"""
            INSERT INTO {{Table<StorageObject>(context)}}
                (id, tenant_id, file_type_id, uri, provider, full_name, safe_display_name,
                 extension, size, visibility, purpose, lifecycle_state, created_at, is_deleted, concurrency_stamp)
            VALUES ({0}, {1}, 2, 'retained/document.txt', 'local', 'document.txt', 'document.txt',
                    '.txt', 37, 'private_owner', 'document', 'active', {2}, {3}, {0})
            """, data.RowId, data.TenantId, CreatedAt, false);
        context.ChangeTracker.Clear();
        return data;
    }

    internal async Task AssertPreservedAsync(ExploreDbContext context)
    {
        await Assert.That(await context.Tenants.Where(row => row.Id == TenantId)
            .Select(row => row.FullName).SingleAsync()).IsEqualTo("Retained integration tenant");
        await Assert.That(await context.Events.Where(row => row.Id == EventId)
            .Select(row => row.Title).SingleAsync()).IsEqualTo("Retained event");
        await Assert.That(await context.Set<EmailDispatchProcessorState>().Where(row => row.Id == RowId)
            .Select(row => row.PauseReason).SingleAsync()).IsEqualTo("retained pause");
        await Assert.That(await context.Set<EmailDispatchTenantControl>().Where(row => row.Id == RowId)
            .Select(row => row.PauseReason).SingleAsync()).IsEqualTo("retained tenant pause");
        await Assert.That(await context.Set<NotificationIntent>().Where(row => row.Id == RowId)
            .Select(row => row.RecipientUserId).SingleAsync()).IsEqualTo(UserId);
        await Assert.That(await context.Set<NotificationIntent>().Where(row => row.Id == RowId)
            .Select(row => row.FanoutOccurrenceId).SingleAsync()).IsEqualTo(RowId);
        await Assert.That(await context.Set<NotificationFanoutOccurrence>().Where(row => row.Id == RowId)
            .Select(row => row.CoalescingKey).SingleAsync()).IsEqualTo("retained-event-update");
        await Assert.That(await context.Set<RegistrationOrder>().Where(row => row.Id == RowId)
            .Select(row => row.TotalDueMinorSnapshot).SingleAsync()).IsEqualTo(1250L);
        await Assert.That(await context.Set<RegistrationOrder>().Where(row => row.Id == RowId)
            .Select(row => row.TicketCatalogVersionId).SingleAsync()).IsEqualTo(CatalogId);
        await Assert.That(await context.Set<StorageObject>().Where(row => row.Id == RowId)
            .Select(row => row.Size).SingleAsync()).IsEqualTo(37L);
        await Assert.That(await context.Set<StorageObject>().Where(row => row.Id == RowId)
            .Select(row => row.Uri).SingleAsync()).IsEqualTo("retained/document.txt");
    }

    internal async Task AssertIntegratedAsync(ExploreDbContext context)
    {
        await AssertPreservedAsync(context);
        var processor = await context.Set<EmailDispatchProcessorState>().AsNoTracking()
            .SingleAsync(row => row.Id == RowId);
        var tenantControl = await context.Set<EmailDispatchTenantControl>().AsNoTracking()
            .SingleAsync(row => row.Id == RowId);
        await Assert.That(processor.DeliveryPolicyRevision).IsEqualTo(0L);
        await Assert.That(tenantControl.DeliveryPolicyRevision).IsEqualTo(0L);
        await Assert.That(processor.OptionalSuppressedThroughRevision).IsNull();
        await Assert.That(processor.OptionalSuppressedThroughUtc).IsNull();
        await Assert.That(tenantControl.OptionalSuppressedThroughRevision).IsNull();
        await Assert.That(tenantControl.OptionalSuppressedThroughUtc).IsNull();
        await Assert.That(await context.Set<NotificationIntent>().Where(row => row.Id == RowId)
            .Select(row => row.EmailDeliveryPolicyRevision).SingleAsync()).IsEqualTo(0L);
        await Assert.That(await context.Set<NotificationFanoutOccurrence>().Where(row => row.Id == RowId)
            .Select(row => row.EmailDeliveryPolicyRevision).SingleAsync()).IsEqualTo(0L);
        var order = await context.Set<RegistrationOrder>().AsNoTracking().SingleAsync(row => row.Id == RowId);
        await Assert.That(order.GuestStatusAccessUntilUtc).IsNull();
        await Assert.That(order.AnonymousPiiRetentionUntilUtc).IsNull();
        await Assert.That(await context.Set<StorageObject>().Where(row => row.Id == RowId)
            .Select(row => row.RegistrationContentRetentionUntilUtc).SingleAsync()).IsNull();
    }

    internal static async Task AssertLocalBootstrapRollbackRejectedAsync(
        ExploreDbContext context, string initialMigration)
    {
        var bootstrap = InstanceBootstrapState.CreateConfiguredAdministratorPending(
            Guid.CreateVersion7(), AuthenticationProviderKind.Local, DeploymentMode.SingleTenant,
            1, new string('a', 64), new string('b', 64), CreatedAt);
        context.Set<InstanceBootstrapState>().Add(bootstrap);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        DbException rejection = (await Assert.ThrowsAsync<DbException>(() =>
            context.GetService<IMigrator>().MigrateAsync(initialMigration)))!;
        await Assert.That(rejection.Message).Contains("ck_instance_bootstrap_states_provider_kind");
        var retained = await context.Set<InstanceBootstrapState>().AsNoTracking().SingleAsync();
        await Assert.That(retained.Id).IsEqualTo(bootstrap.Id);
        await Assert.That(retained.ProviderKind).IsEqualTo(AuthenticationProviderKind.Local);
        await Assert.That(retained.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(retained.Generation).IsEqualTo(1L);
        await Assert.That(retained.ConfigurationFingerprint).IsEqualTo(bootstrap.ConfigurationFingerprint);
        await Assert.That(retained.SelectorFingerprint).IsEqualTo(bootstrap.SelectorFingerprint);
    }

    private static string Table<TEntity>(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }
}
