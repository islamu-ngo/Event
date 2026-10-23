using Cerbos.Sdk.Builder;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(0, 0)]
    [Arguments(1, 0)]
    [Arguments(2, 0)]
    [Arguments(3, 0)]
    [Arguments(4, 0)]
    [Arguments(5, 0)]
    [Arguments(0, 1)]
    [Arguments(1, 1)]
    [Arguments(2, 1)]
    [Arguments(3, 1)]
    [Arguments(4, 1)]
    [Arguments(5, 1)]
    [Arguments(0, 2)]
    [Arguments(1, 2)]
    [Arguments(2, 2)]
    [Arguments(3, 2)]
    [Arguments(4, 2)]
    [Arguments(5, 2)]
    [Arguments(0, 3)]
    [Arguments(1, 3)]
    [Arguments(2, 3)]
    [Arguments(3, 3)]
    [Arguments(4, 3)]
    [Arguments(5, 3)]
    public async Task ProjectionMaintenanceRequiresCurrentAdministrationAndExactTenant(int operation, int scenario)
    {
        await using var fixture = await ProjectionRoomFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        bool session = operation % 2 == 1;
        var seeded = await SeedProjectionMaintenance(db, data.PublicId, session);
        Guid tenantId = db.TenantContext!.TenantId;
        Guid parentId = seeded.ParentId;
        if (scenario != 0)
        {
            Guid grantId = await GrantTemplateAdministrator(db, data.OwnerId);
            await Assert.That(await scope.ServiceProvider.GetRequiredService<IAdminContext>().IsInstanceAdminAsync(default)).IsTrue();
            if (scenario == 2)
                await db.PlatformUserRoles.Where(grant => grant.Id == grantId).ExecuteDeleteAsync();
        }
        if (scenario == 3)
        {
            tenantId = data.ForeignTenantId;
            parentId = data.ForeignId;
            if (session)
            {
                await using var foreign = await scope.ServiceProvider
                    .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync();
                foreign.TenantContext = new AuthorityTenant(data.ForeignTenantId);
                var foreignSession = new EventSession
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = data.ForeignTenantId,
                    Tenant = null!,
                    EventId = data.ForeignId,
                    Event = null!,
                    Title = "Foreign projection session"
                };
                foreign.EventSessions.Add(foreignSession);
                await foreign.SaveChangesAsync();
                parentId = foreignSession.Id;
            }
        }

        if (scenario == 1)
            await Assert.That(await RunProjectionMaintenance(scope.ServiceProvider, operation, tenantId, parentId)).IsTrue();
        else
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                RunProjectionMaintenance(scope.ServiceProvider, operation, tenantId, parentId));

        string? value = await ProjectionValue(db, session, seeded.ValueId);
        await Assert.That(value).IsEqualTo(scenario == 1 ? "maintenance" : null);
        if (scenario != 1 || operation < 4)
            await Assert.That(await db.CustomPropertyProjectionDirtyScopes.AsNoTracking()
                .AnyAsync(row => row.ScopeId == seeded.ParentId && row.DrainedAt != null)).IsEqualTo(scenario == 1);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task ProjectionMaintenanceSelectedCerbosOutageCannotMutateRows(int operation)
    {
        var transport = new ProjectionPolicyOutageTransport();
        var client = CerbosClientBuilder.ForTarget("http://projection-policy.test").WithPlaintext()
            .WithGrpcChannelOptions(new GrpcChannelOptions { HttpHandler = transport, DisposeHttpClient = true }).Build();
        await using var fixture = await ProjectionRoomFixture.CreateAsync(cerbos: client);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        bool session = operation % 2 == 1;
        var seeded = await SeedProjectionMaintenance(db, data.PublicId, session);
        await GrantTemplateAdministrator(db, data.OwnerId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() =>
            RunProjectionMaintenance(scope.ServiceProvider, operation, db.TenantContext!.TenantId, seeded.ParentId, deadline.Token));

        await Assert.That(transport.Requests).IsGreaterThan(0);
        await Assert.That(await ProjectionValue(db, session, seeded.ValueId)).IsNull();
        await Assert.That(await db.CustomPropertyProjectionDirtyScopes
            .AnyAsync(row => row.ScopeId == seeded.ParentId && row.DrainedAt != null)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProjectionUpdaterRequiresCallerTransactionAndRollsBackItsActualEffects(bool session)
    {
        await using var fixture = await ProjectionRoomFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var seeded = await SeedProjectionMaintenance(db, data.PublicId, session);
        Guid tenantId = db.TenantContext!.TenantId;
        async Task Rebuild(CancellationToken token)
        {
            if (session)
                await scope.ServiceProvider.GetRequiredService<IEventSessionCustomPropertyProjectionUpdater>()
                    .RebuildForTenantAsync(tenantId, null, token);
            else
                await scope.ServiceProvider.GetRequiredService<IEventCustomPropertyProjectionUpdater>()
                    .RebuildForTenantAsync(tenantId, null, token);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rebuild(default));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync<bool>(async token =>
            {
                await Rebuild(token);
                throw new OperationCanceledException("Abort after actual projection work.");
            }));

        await Assert.That(await ProjectionValue(db, session, seeded.ValueId)).IsNull();
        await Assert.That(await db.CustomPropertyProjectionDirtyScopes
            .AnyAsync(row => row.ScopeId == seeded.ParentId && row.DrainedAt != null)).IsFalse();
    }

    private static async Task<(Guid ParentId, Guid ValueId)> SeedProjectionMaintenance(
        ExploreDbContext db, Guid eventId, bool session)
    {
        var definition = await PrepareValueDefinition(db, eventId, session, false);
        Guid valueId = Guid.CreateVersion7();
        Guid tenantId = db.TenantContext!.TenantId;
        if (session)
            db.EventSessionCustomPropertyValues.Add(new EventSessionCustomPropertyValue
            {
                Id = valueId,
                TenantId = tenantId,
                EventSessionId = definition.ParentId,
                EventSessionCustomPropertyDefinitionId = definition.Id,
                TextValue = "Maintenance",
                Ordinal = 0
            });
        else
            db.EventCustomPropertyValues.Add(new EventCustomPropertyValue
            {
                Id = valueId,
                TenantId = tenantId,
                EventId = definition.ParentId,
                EventCustomPropertyDefinitionId = definition.Id,
                TextValue = "Maintenance",
                Ordinal = 0
            });
        db.CustomPropertyProjectionDirtyScopes.Add(new CustomPropertyProjectionDirtyScope
        {
            TenantId = tenantId,
            ScopeId = definition.ParentId,
            DefinitionId = definition.Id,
            ScopeType = session ? CustomPropertyProjectionScopeType.EventSession : CustomPropertyProjectionScopeType.Event,
            ProjectionName = session ? IEventSessionCustomPropertyProjectionUpdater.ProjectionName : IEventCustomPropertyProjectionUpdater.ProjectionName,
            ProjectionVersion = session ? IEventSessionCustomPropertyProjectionUpdater.ProjectionVersion : IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            Reason = "native_authority",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (definition.ParentId, valueId);
    }

    private static Task<string?> ProjectionValue(ExploreDbContext db, bool session, Guid valueId) => session
        ? db.EventSessionCustomPropertyProjections.AsNoTracking().Where(row => row.EventSessionCustomPropertyValueId == valueId)
            .Select(row => row.NormalizedValue).SingleOrDefaultAsync()
        : db.EventCustomPropertyProjections.AsNoTracking().Where(row => row.EventCustomPropertyValueId == valueId)
            .Select(row => row.NormalizedValue).SingleOrDefaultAsync();

    private static async Task<bool> RunProjectionMaintenance(
        IServiceProvider services, int operation, Guid tenantId, Guid parentId, CancellationToken token = default)
    {
        await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(token);
        return await InvokeProjectionRoomOwner(services, ProjectionCommand(operation, tenantId, parentId), token);
    }

    private sealed class ProjectionPolicyOutageTransport : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }
    }
}
