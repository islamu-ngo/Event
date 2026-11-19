using System.Data;
using System.Net;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class TenantDiscoveryCreationNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreparedCreatorCommitAdmissionRejectsLateDriftOrReplacement(bool replaceTransaction)
    {
        await database.ResetAsync();
        var request = Request(assignAdministrator: true);
        bool grantChanged = false;
        var probe = new PreparedCreatorCommitProbe();
        var failure = new PreparedCreatorCommitFailureObserver(probe);
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, configureServices: services =>
            {
                services.AddScoped<IHumanTenantCreationGraphWriter>(provider =>
                {
                    var context = provider.GetRequiredService<ExploreDbContext>();
                    var issuer = new PreparedCreatorCommitIssuer(
                        provider.GetRequiredService<IHumanTenantCreationPort>(), context, probe);
                    return new PreparedCreatorCommitWriter(new HumanTenantCreationGraphWriter(context,
                        provider.GetRequiredService<ExploreDbContextExecutionLease>(), issuer,
                        IdentityDatabaseTopology.Colocated), probe);
                });
                services.Insert(0, ServiceDescriptor.Singleton<IExceptionHandler>(failure));
            });
        var tenantsBefore = await ReadTenantsAsync();
        var revisionsBefore = await ReadRevisionsAsync();
        var effectsBefore = await ReadCreationEffectsAsync();
        probe.BeforeCommit = async (context, token) =>
        {
            var transaction = context.Database.CurrentTransaction
                ?? throw new InvalidOperationException("The original creator transaction is required.");
            var physical = transaction.GetDbTransaction();
            var connector = (NpgsqlConnection)context.Database.GetDbConnection();
            int backend = connector.ProcessID;
            probe.InjectionStage = "Checking the original Serializable transaction";
            await Assert.That(physical.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
            probe.InjectionStage = "Checking the original Global login";
            await Assert.That(await context.Database.SqlQueryRaw<bool>(
                "SELECT current_user=session_user AND session_user={0} AS \"Value\"",
                database.NativeQueueEnrollment.GlobalLogin).SingleAsync(token)).IsTrue();
            if (replaceTransaction)
            {
                const string transactionIdentitySql =
                    "SELECT pg_catalog.pg_current_xact_id()::text AS \"Value\"";
                probe.InjectionStage = "Reading the original database transaction identity";
                string originalDatabaseTransactionId =
                    await context.Database.SqlQueryRaw<string>(transactionIdentitySql).SingleAsync(token);
                probe.InjectionStage = "Rolling back and replacing the original transaction";
                await transaction.RollbackAsync(token);
                await transaction.DisposeAsync();
                var replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                probe.InjectionStage = "Checking the replacement EF transaction owner";
                await Assert.That(replacement).IsNotSameReferenceAs(transaction);
                await Assert.That(replacement.GetDbTransaction().IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
                probe.InjectionStage = "Checking the replacement database transaction identity";
                string replacementDatabaseTransactionId =
                    await context.Database.SqlQueryRaw<string>(transactionIdentitySql).SingleAsync(token);
                await Assert.That(replacementDatabaseTransactionId).IsNotEqualTo(originalDatabaseTransactionId);
                probe.InjectionStage = "Checking the replacement retains the original backend";
                await Assert.That(connector.ProcessID).IsEqualTo(backend);
            }
            else
            {
                await using var migrator = OpenMigrator();
                var names = new HumanPrincipalSourceSql(migrator);
                await migrator.Database.OpenConnectionAsync(token);
                await using var command = migrator.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"""
                    GRANT UPDATE ({names.Column(typeof(Explore.Domain.Tenant), nameof(Explore.Domain.Tenant.Slug))})
                    ON {names.Table(typeof(Explore.Domain.Tenant))} TO {names.Quote(database.NativeQueueEnrollment.GlobalLogin)}
                    """;
                await command.ExecuteNonQueryAsync(token);
                grantChanged = true;
                await Assert.That(await migrator.Database.SqlQueryRaw<bool>(
                    "SELECT pg_catalog.has_column_privilege({0},{1},{2},'UPDATE') AS \"Value\"",
                    database.NativeQueueEnrollment.GlobalLogin, names.Table(typeof(Explore.Domain.Tenant)),
                    names.ColumnName(typeof(Explore.Domain.Tenant), nameof(Explore.Domain.Tenant.Slug))).SingleAsync(token)).IsTrue();
                await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
                await Assert.That(connector.ProcessID).IsEqualTo(backend);
            }
            probe.Injected = true;
        };
        try
        {
            using var response = await authentication.CreateTenantViaApiAsync(request, Token);
            await Assert.That(probe.WriteCompleted).IsTrue().Because(authentication.TenantCreationFailureDescription);
            await Assert.That(probe.CommitEntered).IsTrue().Because(authentication.TenantCreationFailureDescription);
            await Assert.That(probe.Injected).IsTrue().Because(
                $"{probe.InjectionStage}. {authentication.TenantCreationFailureDescription}");
            await Assert.That(response.IsSuccessStatusCode).IsFalse();
            if (replaceTransaction)
            {
                await Assert.That(failure.SawOriginalOwnerRefusal).IsTrue()
                    .Because(authentication.TenantCreationFailureDescription);
                await Assert.That(failure.SawCatalogueRefusal).IsFalse();
                await Assert.That(failure.SawCatalogueChange).IsFalse();
            }
            else
            {
                await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
                await Assert.That(failure.SawCatalogueRefusal).IsTrue();
            }
        }
        finally
        {
            if (grantChanged)
            {
                await using var migrator = OpenMigrator();
                var names = new HumanPrincipalSourceSql(migrator);
                await migrator.Database.OpenConnectionAsync(CancellationToken.None);
                await using var command = migrator.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"""
                    REVOKE UPDATE ({names.Column(typeof(Explore.Domain.Tenant), nameof(Explore.Domain.Tenant.Slug))})
                    ON {names.Table(typeof(Explore.Domain.Tenant))} FROM {names.Quote(database.NativeQueueEnrollment.GlobalLogin)}
                    """;
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        await Assert.That(await ReadTenantsAsync()).IsEquivalentTo(tenantsBefore);
        await Assert.That(await ReadRevisionsAsync()).IsEquivalentTo(revisionsBefore);
        await AssertExistingEffectsUnchangedAsync(effectsBefore);
    }

    private sealed class PreparedCreatorCommitProbe
    {
        internal bool WriteCompleted { get; set; }
        internal bool CommitEntered { get; set; }
        internal bool Injected { get; set; }
        internal string InjectionStage { get; set; } = "Before native injection";
        internal Func<ExploreDbContext, CancellationToken, Task>? BeforeCommit { get; set; }
    }

    private sealed class PreparedCreatorCommitWriter(IHumanTenantCreationGraphWriter original,
        PreparedCreatorCommitProbe probe) : IHumanTenantCreationGraphWriter
    {
        public async Task<HumanTenantCreationResult> WriteAsync(IHumanTenantCreationAuthority authority,
            HumanTenantCreationPlan plan, CancellationToken cancellationToken = default)
        {
            var result = await original.WriteAsync(authority, plan, cancellationToken);
            probe.WriteCompleted = result.Creation is not null;
            return result;
        }

        public HumanTenantCreationAdministratorEffect? ReadCompletedAdministratorEffect(
            IHumanTenantCreationAuthority authority, HumanTenantCreationPlan plan) =>
            original.ReadCompletedAdministratorEffect(authority, plan);
    }

    private sealed class PreparedCreatorCommitIssuer(IHumanTenantCreationPort original, ExploreDbContext context,
        PreparedCreatorCommitProbe probe) : IHumanTenantCreationPort
    {
        public Task<HumanTenantCreationResult> CreateAsync(CreateTenantCommand request,
            CancellationToken cancellationToken = default) => original.CreateAsync(request, cancellationToken);

        public async Task RequireCurrentAsync(IHumanTenantCreationAuthority authority, HumanTenantCreationPlan plan,
            CancellationToken cancellationToken = default)
        {
            await original.RequireCurrentAsync(authority, plan, cancellationToken);
            if (probe.WriteCompleted && !probe.CommitEntered)
            {
                probe.CommitEntered = true;
                await probe.BeforeCommit!(context, cancellationToken);
            }
        }
    }

    private sealed class PreparedCreatorCommitFailureObserver(PreparedCreatorCommitProbe probe) : IExceptionHandler
    {
        internal bool SawOriginalOwnerRefusal { get; private set; }
        internal bool SawCatalogueRefusal { get; private set; }
        internal bool SawCatalogueChange { get; private set; }

        public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            if (!probe.Injected) return ValueTask.FromResult(false);
            var pending = new Stack<Exception>();
            pending.Push(exception);
            while (pending.Count != 0)
            {
                var cause = pending.Pop();
                SawOriginalOwnerRefusal |= cause is UnauthorizedAccessException
                    && cause.Message == "The original human creation attempt changed or failed.";
                SawCatalogueRefusal |= cause is UnauthorizedAccessException
                    && cause.Message == "tenant_creation_permissions_not_ready";
                SawCatalogueChange |= cause is UnauthorizedAccessException
                    && cause.Message == "The original human creation catalogue changed.";
                if (cause is AggregateException aggregate)
                    foreach (var inner in aggregate.InnerExceptions) pending.Push(inner);
                else if (cause.InnerException is { } nested)
                    pending.Push(nested);
            }
            return ValueTask.FromResult(false);
        }
    }
}
