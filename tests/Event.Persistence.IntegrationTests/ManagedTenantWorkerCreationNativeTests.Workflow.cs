using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management;
using Explore.Application.Management;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests;

public sealed partial class ManagedTenantWorkerCreationNativeTests
{
    private const string WorkflowObservation = "Independent Migrator verifies only this scheduled generation's graph and rollback across its new tenant.";

    [Test]
    public async Task RealScheduledPendingGenerationCreatesItsWholeGraphThroughTheAdmittedWorker()
    {
        var probe = new WorkerWorkflowProbe("none");
        await using var scenario = await CreateWorkflowAsync(probe);
        var scheduled = await ScheduleOriginalAsync(scenario);
        var claim = await scenario.Host.ClaimManagedWorkerAsync(scheduled.Id, scenario.Token);
        await Assert.That(claim.MessageId).IsEqualTo(scheduled.CurrentOutboxMessageId);

        await Assert.That(await scenario.Host.ProcessManagedWorkerAsync(claim, scenario.Token)).IsTrue();

        await Assert.That(probe.SavedOriginalGraph).IsTrue();
        await using var observer = OpenMigrator();
        var completed = await observer.ManagedTenantProvisioningOperations.AsNoTracking()
            .SingleAsync(row => row.Id == scheduled.Id, scenario.Token);
        await Assert.That(completed.Status).IsEqualTo(ManagedTenantProvisioningStatus.Succeeded);
        await Assert.That(completed.CurrentOutboxMessageId).IsEqualTo(claim.MessageId);
        var tenant = await observer.Tenants.AsNoTracking().SingleAsync(row => row.Slug == scenario.Request.TenantSlug, scenario.Token);
        await Assert.That(completed.TenantId).IsEqualTo(tenant.Id);
        await Assert.That(completed.TenantAdministratorUserId).IsNotNull();
        var revision = await observer.Set<EventDiscoveryRevision>().IgnoreTenantFilter(WorkflowObservation).AsNoTracking()
            .SingleAsync(row => row.TenantId == tenant.Id, scenario.Token);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(0L);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(1L);
        var documents = await observer.Set<TenantSettingsDocument>().IgnoreTenantFilter(WorkflowObservation).AsNoTracking()
            .Where(row => row.TenantId == tenant.Id).Select(row => row.DocumentKey).ToArrayAsync(scenario.Token);
        await Assert.That(documents.Order(StringComparer.Ordinal).SequenceEqual(new[]
            { SettingsDocumentKeys.Tenant.Branding, SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity }.Order(StringComparer.Ordinal))).IsTrue();
        var membership = await observer.TenantUsers.IgnoreTenantFilter(WorkflowObservation).AsNoTracking().SingleAsync(row =>
            row.TenantId == tenant.Id && row.UserId == completed.TenantAdministratorUserId, scenario.Token);
        await Assert.That(await observer.Set<TenantUserRoleGrant>().IgnoreTenantFilter(WorkflowObservation).AnyAsync(row => row.TenantId == tenant.Id
            && row.TenantUserId == membership.Id && row.RevokedAt == null, scenario.Token)).IsTrue();
        await Assert.That(await observer.Users.AnyAsync(row => row.Id == membership.UserId, scenario.Token)).IsTrue();
        await Assert.That(await observer.Actors.AnyAsync(row => row.UserId == membership.UserId, scenario.Token)).IsTrue();
        await Assert.That(await observer.UserExternalLogins.AnyAsync(row => row.UserId == membership.UserId, scenario.Token)).IsTrue();
    }

    [Test]
    public async Task ACurrentAdmittedGenerationCommitsOnlyItsFailureWhenRegistrationWasRevokedAfterScheduling()
    {
        var probe = new WorkerWorkflowProbe("none");
        await using var scenario = await CreateWorkflowAsync(probe);
        var scheduled = await ScheduleOriginalAsync(scenario);
        await using (var revoke = OpenMigrator())
        {
            var registration = await revoke.ManagedControlPlaneRegistrations.SingleAsync(row =>
                row.ManagedInstanceId == scenario.ManagedInstanceId, scenario.Token);
            registration.Revoke(DateTime.UtcNow);
            await revoke.SaveChangesAsync(scenario.Token);
        }
        var claim = await scenario.Host.ClaimManagedWorkerAsync(scheduled.Id, scenario.Token);

        await Assert.That(await scenario.Host.ProcessManagedWorkerAsync(claim, scenario.Token)).IsTrue();

        await Assert.That(probe.SavedOriginalGraph).IsFalse();
        await using var observer = OpenMigrator();
        var failed = await observer.ManagedTenantProvisioningOperations.SingleAsync(row => row.Id == scheduled.Id, scenario.Token);
        await Assert.That(failed.Status).IsEqualTo(ManagedTenantProvisioningStatus.Failed);
        await Assert.That(failed.CurrentOutboxMessageId).IsEqualTo(claim.MessageId);
        await Assert.That(failed.FailedAt).IsNotNull();
        await Assert.That(failed.FailureCode).IsEqualTo("managed_registration_unavailable");
        await Assert.That(failed.RequestJson).IsNull();
        await Assert.That(failed.TenantId).IsNull();
        await Assert.That(failed.TenantAdministratorUserId).IsNull();
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Slug == scenario.Request.TenantSlug, scenario.Token)).IsFalse();
    }

    [Test]
    [Arguments("mutate_actor")]
    [Arguments("post_save")]
    public async Task RealWorkerGraphTamperingOrLateFailureCannotCommitAnOperationOrPartialGraph(string fault)
    {
        var probe = new WorkerWorkflowProbe(fault);
        await using var scenario = await CreateWorkflowAsync(probe);
        var scheduled = await ScheduleOriginalAsync(scenario);
        var claim = await scenario.Host.ClaimManagedWorkerAsync(scheduled.Id, scenario.Token);
        await using var before = OpenMigrator();
        Guid[] originalUsers = await before.Users.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(scenario.Token);
        Guid[] originalActors = await before.Actors.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(scenario.Token);

        await Assert.ThrowsAsync<Exception>(() => scenario.Host.ProcessManagedWorkerAsync(claim, scenario.Token));

        await Assert.That(probe.ReachedFault).IsTrue();
        if (fault == "post_save") await Assert.That(probe.SavedOriginalGraph).IsTrue();
        await using var observer = OpenMigrator();
        var unchanged = await observer.ManagedTenantProvisioningOperations.AsNoTracking()
            .SingleAsync(row => row.Id == scheduled.Id, scenario.Token);
        await Assert.That(unchanged.Status).IsEqualTo(ManagedTenantProvisioningStatus.Pending);
        await Assert.That(unchanged.CurrentOutboxMessageId).IsEqualTo(claim.MessageId);
        await Assert.That(unchanged.StartedAt).IsNull();
        await Assert.That(unchanged.TenantId).IsNull();
        await Assert.That(unchanged.RequestHash).IsEqualTo(scheduled.RequestHash);
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Slug == scenario.Request.TenantSlug, scenario.Token)).IsFalse();
        await Assert.That(await observer.Set<EventDiscoveryRevision>().IgnoreTenantFilter(WorkflowObservation).AnyAsync(row => row.TenantId == probe.TenantId, scenario.Token)).IsFalse();
        await Assert.That(await observer.Set<TenantSettingsDocument>().IgnoreTenantFilter(WorkflowObservation).AnyAsync(row => row.TenantId == probe.TenantId, scenario.Token)).IsFalse();
        Guid[] actualUsers = await observer.Users.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(scenario.Token);
        Guid[] actualActors = await observer.Actors.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(scenario.Token);
        await Assert.That(originalUsers.SequenceEqual(actualUsers)).IsTrue();
        await Assert.That(originalActors.SequenceEqual(actualActors)).IsTrue();
    }

    private async Task<ManagedTenantProvisioningOperation> ScheduleOriginalAsync(WorkerWorkflowScenario scenario)
    {
        var response = await scenario.Host.ScheduleManagedWorkerAsync(scenario.ManagedInstanceId, scenario.Request, scenario.Token);
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsNotNull();
        await using var observer = OpenMigrator();
        var operation = await observer.ManagedTenantProvisioningOperations.AsNoTracking()
            .SingleAsync(row => row.Id == response.Id!.OperationId, scenario.Token);
        await Assert.That(operation.Status).IsEqualTo(ManagedTenantProvisioningStatus.Pending);
        await Assert.That(operation.StartedAt).IsNull();
        await Assert.That(operation.RequestHash).IsEqualTo(ManagedTenantProvisioningRequestCodec.ComputeHash(scenario.Request));
        var message = await observer.Set<OutboxMessage>().AsNoTracking()
            .SingleAsync(row => row.Id == operation.CurrentOutboxMessageId, scenario.Token);
        await Assert.That(message.AggregateId).IsEqualTo(operation.Id);
        await Assert.That(message.EventType).IsEqualTo("ManagedTenantProvisioningProcessRequested");
        await Assert.That(message.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(message.Payload).IsNull();
        return operation;
    }

    private async Task<WorkerWorkflowScenario> CreateWorkflowAsync(IInterceptor probe)
    {
        await fixture.ResetAsync();
        Guid managed = Guid.CreateVersion7();
        var host = await AuthenticatedSettingsFixture.CreateForManagedWorkerAsync(OpenMigrator, fixture, managed, probe);
        try
        {
            Guid version = Guid.CreateVersion7();
            await using var seed = OpenMigrator();
            var bootstrap = await seed.InstanceBootstrapStates.OrderByDescending(row => row.Generation)
                .ThenByDescending(row => row.CreatedAt).FirstAsync();
            bootstrap.TransitionDeploymentMode(DeploymentMode.MultiTenant);
            var binding = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.Management.ControlPlaneRegistrationCredentials,
                SecretScope.Instance, null, "CONTROL_PLANE_REGISTRATION_CREDENTIALS");
            seed.Add(binding);
            seed.Add(new ManagedControlPlaneRegistration
            {
                Id = Guid.CreateVersion7(),
                ManagedInstanceId = managed,
                EventInstanceId = Guid.CreateVersion7(),
                ControlPlaneEndpoint = "https://control.example.test",
                ManagementApiVersion = ManagedControlPlaneContract.ManagementApiVersion,
                EventVersion = "test",
                DeploymentMode = DeploymentMode.MultiTenant,
                Status = ManagedControlPlaneRegistrationStatus.Registered,
                RegisteredAt = DateTime.UtcNow,
                RequestHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                EventToControlPlaneKeyId = Guid.CreateVersion7().ToString("N"),
                ControlPlaneToEventKeyId = Guid.CreateVersion7().ToString("N"),
                EventToControlPlaneSecretHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ControlPlaneToEventSecretHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                CredentialSecretBindingId = binding.Id,
                EventToControlPlaneCredentialExpiresAt = DateTime.UtcNow.AddDays(1),
                ControlPlaneToEventCredentialExpiresAt = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow
            });
            var plan = new TenantPlan { Id = Guid.CreateVersion7(), Key = "worker-flow", DisplayName = "Worker flow", CreatedAt = DateTime.UtcNow };
            seed.Add(plan);
            seed.Add(new TenantPlanVersion
            {
                Id = version,
                TenantPlanId = plan.Id,
                VersionNumber = 1,
                TenantPlanStatusId = (int)TenantPlanStatusEnum.Published,
                IsActiveForProvisioning = true,
                CurrencyCode = "EUR",
                BillingPeriod = "monthly",
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
            string suffix = Guid.CreateVersion7().ToString("N");
            var request = ManagedTenantProvisioningRequestCodec.Deserialize($$$"""
                {"externalRequestId":"flow-{{{suffix}}}","externalCustomerReference":"customer-{{{suffix}}}",
                 "tenantName":"Worker Flow","tenantSlug":"flow-{{{suffix}}}",
                 "administrator":{"externalIdentity":{"identityProvider":"https://issuer.example.test","subject":"subject-{{{suffix}}}",
                 "email":"worker@example.test","firstName":"Worker","lastName":"Administrator","emailVerified":true}},
                 "plan":{"key":"worker-flow","versionId":"{{{version:D}}}"},
                 "directoryOperatorIdentity":{"publicName":"Worker operator","legalName":"Worker operator ASBL","operatorKindCode":"registered_organization",
                 "jurisdictionCountryCode":"BE","registrationIdentifier":"BE 0123.456.789","publicContactEmail":"operator@example.test",
                 "legalNoticeUrl":"https://example.test/legal","termsUrl":"https://example.test/terms","privacyUrl":"https://example.test/privacy"}}
                """);
            return new(host, managed, ManagedTenantProvisioningRequestCodec.Normalize(request));
        }
        catch { await host.DisposeAsync(); throw; }
    }

    private sealed class WorkerWorkflowScenario(AuthenticatedSettingsFixture host, Guid managed, ManagementTenantProvisioningRequestDto request) : IAsyncDisposable
    {
        internal AuthenticatedSettingsFixture Host => host;
        internal Guid ManagedInstanceId => managed;
        internal ManagementTenantProvisioningRequestDto Request => request;
        internal CancellationToken Token => TestContext.Current!.Execution.CancellationToken;
        public ValueTask DisposeAsync() => host.DisposeAsync();
    }

    private sealed class WorkerWorkflowProbe(string fault) : DbTransactionInterceptor
    {
        internal bool ReachedFault { get; private set; }
        internal bool SavedOriginalGraph { get; private set; }
        internal Guid TenantId { get; private set; }

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
            DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext context || !context.HasOriginalCommitContextLease || context.HasHumanCommitContextLease)
                return ValueTask.FromResult(result);
            if (result.IsolationLevel != IsolationLevel.Serializable) throw new InvalidOperationException("worker_test_original_serializable_missing");
            context.ChangeTracker.Tracked += (_, tracked) =>
            {
                if (tracked.Entry.Entity is not EventDiscoveryRevision revision || tracked.Entry.State != EntityState.Added) return;
                TenantId = revision.TenantId;
                if (fault != "mutate_actor") return;
                var actor = context.ChangeTracker.Entries<Actor>().Single(entry => entry.State == EntityState.Added).Entity;
                actor.ActorTypeId = (int)ActorTypeEnum.Organization;
                ReachedFault = true;
            };
            context.SavedChanges += (_, _) =>
            {
                if (TenantId == Guid.Empty) return;
                if (!ReferenceEquals(context.Database.GetDbConnection(), connection)
                    || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), result)
                    || context.ChangeTracker.Entries<Tenant>().Single(entry => entry.Entity.Id == TenantId).State != EntityState.Unchanged
                    || context.ChangeTracker.Entries<TenantSettingsDocument>().Count(entry => entry.Entity.TenantId == TenantId && entry.State == EntityState.Unchanged) != 2
                    || context.ChangeTracker.Entries<EventDiscoveryRevision>().Single(entry => entry.Entity.TenantId == TenantId).State != EntityState.Unchanged)
                    throw new InvalidOperationException("worker_test_original_graph_not_saved");
                SavedOriginalGraph = true;
                if (fault == "post_save")
                {
                    ReachedFault = true;
                    throw new InvalidOperationException("worker_test_post_save_failure");
                }
            };
            return ValueTask.FromResult(result);
        }
    }
}
