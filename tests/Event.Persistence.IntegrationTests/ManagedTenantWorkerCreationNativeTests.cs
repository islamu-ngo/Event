using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Management;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

/// <summary>
/// Genuine PostgreSQL delivery/issuer custody only. Migrator seeds the persisted
/// source envelope; these cases do not claim Schedule, creator graph or handoff proof.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed partial class ManagedTenantWorkerCreationNativeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task AnOrdinaryRuntimeOwnerCannotAdmitAWorkerFromOperationIdentifiers()
    {
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        runtime.BindTenantLease();
        var selection = new ExploreDbContextExecutionLease();
        selection.Bind(runtime, null);
        var global = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var admission = new GenericOutboxAdmission(global, TimeProvider.System);
        var issuer = new ManagedTenantWorkerCreationAdmission(runtime, selection,
            new GenericOutboxExecutionAuthority(runtime, admission),
            new OutboxRepository(runtime, fixture.NativeQueueEnrollment), TimeProvider.System);

        var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            issuer.AcquireAsync(Guid.CreateVersion7(), Guid.CreateVersion7()).AsTask());

        await Assert.That(denied.Message).IsEqualTo("managed_worker_creation_not_admitted");
        await Assert.That(runtime.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task AnInterfaceOrCopiedIdentifiersCannotReplaceTheOriginalIssuedInvocation()
    {
        await using var session = await CreateAdmittedSessionAsync();
        await using var original = await session.Admission.AcquireAsync(session.Claim.AggregateId, session.Claim.MessageId);
        await original.RequireCurrentAsync();
        var factory = new ManagedTenantWorkerCreationOwnerFactory(session.Admission,
            session.Provider.GetRequiredService<IServiceScopeFactory>(), session.Global);

        foreach (var candidate in new[]
                 {
                     new UnissuedInvocation(Guid.CreateVersion7(), Guid.CreateVersion7()),
                     new UnissuedInvocation(original.OperationId, original.OutboxMessageId)
                 })
        {
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => factory.CreateAsync(candidate).AsTask());
            await Assert.That(denied.Message).IsEqualTo("managed_worker_creation_not_admitted");
        }
        await original.RequireCurrentAsync();
        await Assert.That(session.Runtime.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task AnExpiredRealDeliveryCannotRetainWorkerCreationCustody()
    {
        await using var session = await CreateAdmittedSessionAsync();
        await using var original = await session.Admission.AcquireAsync(session.Claim.AggregateId, session.Claim.MessageId);
        await original.RequireCurrentAsync();
        session.Clock.AdvanceTo(session.Claim.LeaseExpiresAt.AddTicks(1));

        var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => original.RequireCurrentAsync());

        await Assert.That(denied.Message).IsEqualTo("managed_worker_creation_not_admitted");
    }

    [Test]
    public async Task DisposingTheOriginalInvocationCannotIssueAnotherCreator()
    {
        await using var session = await CreateAdmittedSessionAsync();
        var original = await session.Admission.AcquireAsync(session.Claim.AggregateId, session.Claim.MessageId);
        await original.RequireCurrentAsync();
        await original.DisposeAsync();
        var factory = new ManagedTenantWorkerCreationOwnerFactory(session.Admission,
            session.Provider.GetRequiredService<IServiceScopeFactory>(), session.Global);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => factory.CreateAsync(original).AsTask());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Admission
            .AcquireAsync(session.Claim.AggregateId, session.Claim.MessageId).AsTask());
    }

    [Test]
    public async Task ReopeningTheOriginalRuntimeConnectorPoisonsAnActuallyAdmittedInvocation()
    {
        await using var session = await CreateAdmittedSessionAsync();
        await using var original = await session.Admission.AcquireAsync(session.Claim.AggregateId, session.Claim.MessageId);
        await original.RequireCurrentAsync();
        await session.Runtime.Database.CloseConnectionAsync();
        await session.Runtime.Database.OpenConnectionAsync();

        var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => original.RequireCurrentAsync());

        await Assert.That(denied.Message).IsEqualTo("managed_worker_creation_not_admitted");
    }

    [Test]
    [Arguments("payload")]
    [Arguments("generation")]
    public async Task ARealCurrentEnvelopeAndItsOriginalSourceGenerationRemainMandatory(string mutation)
    {
        await using var session = await CreateAdmittedSessionAsync();
        await using var original = await session.Admission.AcquireAsync(session.Claim.AggregateId, session.Claim.MessageId);
        await original.RequireCurrentAsync();
        await using (var migrator = OpenMigrator())
        {
            int affected = mutation == "payload"
                ? await migrator.Set<OutboxMessage>().Where(row => row.Id == session.Claim.MessageId)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Payload, "{}"))
                : await migrator.Set<ManagedTenantProvisioningOperation>().Where(row => row.Id == session.Claim.AggregateId)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.CurrentOutboxMessageId, Guid.CreateVersion7()));
            await Assert.That(affected).IsEqualTo(1);
        }

        var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => original.RequireCurrentAsync());
        await Assert.That(denied.Message).IsEqualTo("managed_worker_creation_not_admitted");
    }

    private async Task<Session> CreateAdmittedSessionAsync()
    {
        await fixture.ResetAsync();
        var clock = new DeliveryClock();
        Guid operationId = Guid.CreateVersion7();
        Guid messageId = Guid.CreateVersion7();
        var request = ManagedTenantProvisioningRequestCodec.Normalize(
            ManagedTenantProvisioningRequestCodec.Deserialize(RequestJson));
        await using (var migrator = OpenMigrator())
        {
            var operation = new ManagedTenantProvisioningOperation
            {
                Id = operationId,
                ManagedInstanceId = Guid.CreateVersion7(),
                ExternalRequestId = request.ExternalRequestId,
                ExternalCustomerReference = request.ExternalCustomerReference,
                RequestHash = ManagedTenantProvisioningRequestCodec.ComputeHash(request),
                RequestJson = ManagedTenantProvisioningRequestCodec.Serialize(request),
                TenantSlug = request.TenantSlug,
                CurrentOutboxMessageId = messageId,
                Status = ManagedTenantProvisioningStatus.Pending,
                CreatedAt = clock.GetUtcNow().UtcDateTime
            };
            operation.Start(clock.GetUtcNow().UtcDateTime);
            migrator.AddRange(operation, new OutboxMessage
            {
                Id = messageId,
                AggregateType = nameof(ManagedTenantProvisioningOperation),
                AggregateId = operationId,
                EventType = "ManagedTenantProvisioningProcessRequested",
                Payload = null,
                Status = OutboxMessageStatus.Pending,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                MaxRetries = 5
            });
            await migrator.SaveChangesAsync();
        }
        var global = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var admission = new GenericOutboxAdmission(global, clock);
        var claim = (await admission.ClaimDueAsync(1, clock.GetUtcNow().UtcDateTime, default)).Single();
        await Assert.That(claim.AggregateId).IsEqualTo(operationId);
        await Assert.That(claim.MessageId).IsEqualTo(messageId);
        var runtimeOptions = TestDbContextOptions.Create<ExploreDbContext>();
        var declaredRuntime = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime);
        PrimaryDatabaseProviderComposition.ConfigureApplication(runtimeOptions, declaredRuntime);
        runtimeOptions.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        var runtimeFactory = new PooledDbContextFactory<ExploreDbContext>(runtimeOptions.Options);
        var services = new ServiceCollection();
        services.AddScoped<ExploreDbContextExecutionLease>();
        services.AddScoped(provider => provider.GetRequiredService<ExploreDbContextExecutionLease>().Context
            ?? throw new InvalidOperationException("The original Runtime context was not selected."));
        services.AddScoped<IGenericOutboxExecutionAuthority>(provider =>
            new GenericOutboxExecutionAuthority(provider.GetRequiredService<ExploreDbContext>(), admission));
        services.AddScoped(provider => new ManagedTenantWorkerCreationAdmission(
            provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<ExploreDbContextExecutionLease>(),
            provider.GetRequiredService<IGenericOutboxExecutionAuthority>(),
            new OutboxRepository(provider.GetRequiredService<ExploreDbContext>(), fixture.NativeQueueEnrollment), clock));
        var provider = services.BuildIsolatedServiceProvider();
        try
        {
            var factory = new GenericOutboxExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>(),
                new TenantExploreDbContextFactory(declaredRuntime), runtimeFactory);
            var execution = await factory.CreateAsync(claim, default);
            return new(provider, execution, global, clock, claim);
        }
        catch { await provider.DisposeAsync(); throw; }
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private sealed class Session(ServiceProvider provider, IGenericOutboxExecutionScope execution,
        GlobalExploreDbContextFactory global, DeliveryClock clock, GenericOutboxClaim claim) : IAsyncDisposable
    {
        internal ServiceProvider Provider => provider;
        internal GlobalExploreDbContextFactory Global => global;
        internal DeliveryClock Clock => clock;
        internal GenericOutboxClaim Claim => claim;
        internal ExploreDbContext Runtime => execution.Services.GetRequiredService<ExploreDbContext>();
        internal ManagedTenantWorkerCreationAdmission Admission => execution.Services.GetRequiredService<ManagedTenantWorkerCreationAdmission>();
        public async ValueTask DisposeAsync()
        {
            try { await execution.DisposeAsync(); }
            finally { await provider.DisposeAsync(); }
        }
    }

    private sealed class DeliveryClock : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => current;
        internal void AdvanceTo(DateTime utc) => current = new DateTimeOffset(utc);
    }

    private sealed class UnissuedInvocation(Guid operationId, Guid messageId) : IManagedTenantWorkerCreationInvocation
    {
        public Guid OperationId => operationId;
        public Guid OutboxMessageId => messageId;
        public Task RequireCurrentAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private const string RequestJson = """
        {"externalRequestId":"custody-request","externalCustomerReference":"custody-customer",
         "tenantName":"Custody Tenant","tenantSlug":"custody-tenant",
         "administrator":{"externalIdentity":{"identityProvider":"https://issuer.example.test","subject":"custody-subject",
         "email":"custody@example.test","firstName":"Custody","lastName":"Administrator","emailVerified":true}},
         "plan":{"key":"managed","versionId":"0199ec4b-0000-7000-8000-000000000002"}}
        """;
}
