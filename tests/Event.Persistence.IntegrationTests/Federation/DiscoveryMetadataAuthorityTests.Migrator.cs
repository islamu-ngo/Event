using System.Data;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class DiscoveryMetadataAuthorityTests
{
    [Test]
    public async Task MissingDeclaredMigratorRejectsTheOriginalGlobalMetadataOwner()
    {
        await using var global = await OpenGlobalAsync();
        await global.Database.OpenConnectionAsync();
        var connector = (NpgsqlConnection)global.Database.GetDbConnection();
        int backend = connector.ProcessID;
        var contextId = global.ContextId;
        var tenantLease = global.TenantContext;
        global.RequireBoundTenantLease();
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(global.Database.CurrentTransaction).IsNull();
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, true, default);

        string missingLogin = "missing_metadata_migrator_" + Guid.CreateVersion7().ToString("N");
        await using (var identity = new NpgsqlCommand("""
            SELECT session_user=@global AND current_user=session_user
                AND current_setting('role') IN ('none',session_user::text)
                AND NULLIF(current_setting('app.current_tenant_id',true),'') IS NULL
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname=@missing)
            """, connector))
        {
            identity.Parameters.AddWithValue("global", fixture.NativeQueueEnrollment.GlobalLogin);
            identity.Parameters.AddWithValue("missing", missingLogin);
            await Assert.That(await identity.ExecuteScalarAsync()).IsEqualTo(true);
        }

        var missingEnrollment = fixture.NativeQueueEnrollment with { MigratorLogin = missingLogin };
        missingEnrollment.Validate();
        var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, missingEnrollment, true, default));
        await Assert.That(denied.Message).IsEqualTo("discovery_metadata_current_owner_not_ready");
        global.RequireBoundTenantLease();
        await Assert.That(global.ContextId).IsEqualTo(contextId);
        await Assert.That(ReferenceEquals(global.TenantContext, tenantLease)).IsTrue();
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(global.IsTenantFilterBypassed).IsFalse();
        await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
        await Assert.That(global.Database.CurrentTransaction).IsNull();
        await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();

        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, true, default);
        await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
        await Assert.That(global.Database.CurrentTransaction).IsNull();
    }
}
