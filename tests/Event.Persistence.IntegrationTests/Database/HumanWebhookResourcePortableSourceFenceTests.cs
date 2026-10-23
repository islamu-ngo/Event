using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel("AdmissionAuthorityProviderDb")]
[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
public sealed class HumanWebhookResourcePortableSourceFenceTests(AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    public async Task MissingConsumerInsertIsExcludedAndOriginalOwnerSeesOwnPersistedReplacement(PrimaryDatabaseProvider provider)
    {
        await using var ingress = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var (subject, forwarding, _) = await HumanSupportAccessSourceFenceTests.CaptureAsync(ingress, null);
        bool sqlServer = provider == PrimaryDatabaseProvider.SqlServer;
        await using var setup = Open(provider, ingress.TenantId);
        if (sqlServer)
            await setup.Database.ExecuteSqlRawAsync(
                "IF SCHEMA_ID(N'islamu_event') IS NULL EXEC(N'CREATE SCHEMA [islamu_event]')");
        Type[] types =
        [
            typeof(WebhookConsumer), typeof(WebhookEndpoint), typeof(WebhookDeliveryAttempt), typeof(WebhookMessage),
            typeof(WebhookLocalTargetSnapshot), typeof(WebhookDeliveryPlanSnapshot), typeof(WebhookProviderPublication),
            typeof(WebhookBulkReplayOperation), typeof(WebhookConsumerProviderBinding), typeof(InstanceBootstrapState),
            typeof(Organization), typeof(OrganizationTenant), typeof(Group), typeof(GroupTenant), typeof(TenantUser),
            typeof(SupportAccessSession), typeof(SystemSetting), typeof(TenantSetting), typeof(User),
            typeof(UserExternalLogin), typeof(PlatformUserRole), typeof(TenantUserRoleGrant),
            typeof(OrganizationMember), typeof(GroupMember), typeof(Role), typeof(RolePermission), typeof(Permission),
            typeof(Tenant), typeof(TenantStatus)
        ];
        var helper = setup.GetService<ISqlGenerationHelper>();
        foreach (Type type in types)
        {
            var entity = setup.Model.FindEntityType(type)!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string columns = string.Join(",", entity.GetProperties().Select(property =>
                $"{helper.DelimitIdentifier(property.GetColumnName(store)!)} {property.GetRelationalTypeMapping().StoreType} NULL"));
            string table = Table(setup, type);
            await setup.Database.ExecuteSqlRawAsync(sqlServer
                ? $"IF OBJECT_ID(N'{table}', N'U') IS NULL CREATE TABLE {table} ({columns})"
                : $"CREATE TABLE IF NOT EXISTS {table} ({columns}) ENGINE=InnoDB");
        }
        await setup.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Table(setup, typeof(User))} (id,is_deleted) VALUES ({{0}},{{1}})", subject.PrincipalId, false);
        Guid consumer = Guid.CreateVersion7();
        await using var context = Open(provider, ingress.TenantId);
        var source = new HumanWebhookResourceSourceFence(context, subject,
            HumanWebhookResourceTarget.From(new OpenSvixAppPortalCommand { ConsumerId = consumer, SessionId = "source" }),
            PolicySourceSetPurpose.RuntimeTenant);
        var principal = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant);
        var support = new HumanSupportAccessSourceFence(context, subject, forwarding, PolicySourceSetPurpose.RuntimeTenant);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var s = await sets.AcquireAsync();
        var p = await principal.AcquireAsync(s);
        var held = await source.AcquireAsync(s, p, await support.AcquireAsync(s, p));
        await Assert.That(HumanWebhookResourceProjection.Resource(held.Facts)).IsNull();
        await using var writer = Open(provider, ingress.TenantId);
        await writer.Database.OpenConnectionAsync();
        await writer.Database.ExecuteSqlRawAsync(sqlServer ? "SET LOCK_TIMEOUT 0" : "SET SESSION innodb_lock_wait_timeout = 1");
        string insert = $"INSERT INTO {Table(writer, typeof(WebhookConsumer))} "
            + "(id,tenant_id,configuration_scope_id,consumer_kind_id,status_id,provider_mode_id,configuration_version) "
            + "VALUES ({0},{1},{1},{2},{3},{4},{5})";
        object[] values = [consumer, ingress.TenantId, (int)WebhookConsumerKind.Tenant,
            (int)WebhookConsumerStatus.Active, (int)WebhookProviderMode.Local, 1];
        await using (var attempt = await writer.Database.BeginTransactionAsync())
        {
            Exception? failure = null;
            try { await writer.Database.ExecuteSqlRawAsync(insert, values); }
            catch (Exception error) { failure = error; }
            await Assert.That(failure is SqlException { Number: 1222 } or MySqlException { Number: 1205 }).IsTrue();
            await attempt.RollbackAsync();
        }
        await context.Database.ExecuteSqlRawAsync(insert, values);
        var fresh = await held.ValidateAsync();
        await Assert.That(fresh.Consumers.Single().Id).IsEqualTo(consumer);
        await Assert.That(HumanWebhookResourceProjection.Resource(fresh)!.ResourceId).IsEqualTo(consumer.ToString("D"));
        await Assert.That(held.Facts.Consumers.Length).IsEqualTo(0);
        await context.Database.ExecuteSqlRawAsync(
            $"UPDATE {Table(context, typeof(WebhookConsumer))} SET tenant_id = {{0}}, configuration_scope_id = {{0}} WHERE id = {{1}}",
            Guid.CreateVersion7(), consumer);
        await Assert.That(HumanWebhookResourceProjection.Resource(await held.ValidateAsync())).IsNull();
        await owner.RollbackAsync();
    }

    private ExploreDbContext Open(PrimaryDatabaseProvider provider, Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.CreateOptions(provider));
        return new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
    }
    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
