using System.Data.Common;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionTemplates.Handlers.Commands;
using Explore.Application.Features.EventTemplates.Handlers.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TemplateCreationConflictHonorsOwnerIdentityAndReplayContract(bool session)
    {
        var gate = new TemplateReplayGate();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(gate);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid parentId = session ? await SeedDeletionTemplate(db, false) : Guid.Empty;
        await GrantTemplateAdministrator(db, data.OutsiderId);
        string key = $"retry-{Guid.CreateVersion7():N}";
        gate.Arm(db, () => CaptureCreatedTemplate(db, session, key));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<HumanMutationAttemptRolledBackException>(() =>
            CreateReplayTemplate(scope.ServiceProvider, session, parentId, key, deadline.Token));
        await Assert.That(gate.Attempts).IsEqualTo(1);
        await Assert.That(gate.FirstAttempt).IsNotNull();
        await Assert.That(await CreatedTemplateExists(db, session, key)).IsFalse();
        await AssertRolledBackTemplateChildren(db, session, gate.FirstAttempt!);
        // A new whole action owns new preparation and identities, never a
        // replay of the original credential/source admission.
        using var fresh = fixture.CreateScope();
        await fixture.AuthenticateAsync(fresh);
        var response = await CreateReplayTemplate(fresh.ServiceProvider, session, parentId, key, deadline.Token);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(gate.Attempts).IsEqualTo(1);
        await Assert.That(gate.FirstAttempt).IsNotNull();
        var stored = await ReadCreatedTemplate(db, session, key);
        await Assert.That(stored.RootId).IsNotEqualTo(gate.FirstAttempt!.RootId);
        await Assert.That(stored.DefinitionId).IsNotEqualTo(gate.FirstAttempt.DefinitionId);
        await Assert.That(stored.OptionId).IsNotEqualTo(gate.FirstAttempt.OptionId);
        await Assert.That(stored.RootCreatedAt).IsEqualTo(stored.DefinitionCreatedAt);
        await Assert.That(stored.DefinitionCreatedAt).IsEqualTo(stored.OptionCreatedAt);
        await Assert.That(stored.DefaultOptionId).IsEqualTo(stored.OptionId);
        await Assert.That(response.Id).IsEqualTo(stored.RootId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TemplateReplacementConflictHonorsOwnerIdentityAndReplayContract(bool session)
    {
        var gate = new TemplateReplayGate();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(gate);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedDeletionTemplate(db, session);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var before = await TemplateUpdateState(db, session, id);
        string key = session
            ? await db.EventSessionTemplates.Where(row => row.Id == id).Select(row => row.SessionTemplateKey).SingleAsync()
            : await db.EventTemplates.Where(row => row.Id == id).Select(row => row.TemplateKey).SingleAsync();
        gate.Arm(db, () => CaptureCreatedTemplate(db, session, key));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        async Task<BaseCommandResponse<Guid>> ReplaceAsync(IServiceProvider services)
        {
            await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
                .PrepareAsync(deadline.Token);
            return session
            ? await ActivatorUtilities.CreateInstance<UpdateEventSessionTemplateCommandHandler>(services).ExecuteAsync(new()
            {
                SessionTemplateId = id,
                ExpectedConcurrencyStamp = before.Stamp,
                SessionTemplateDto = new()
                {
                    Definitions = new()
                    {
                        Items = [new()
                        {
                            Namespace = "tenant", Key = "choice", DisplayName = "Choice",
                            PropertyType = PropertyType.Option, ExposureLevel = ExposureLevel.Public,
                            Options = [new() { Namespace = "tenant", Key = "selected", DisplayName = "Selected", Value = "selected", IsDefault = true }]
                        }]
                    }
                }
            }, deadline.Token)
            : await ActivatorUtilities.CreateInstance<UpdateEventTemplateCommandHandler>(services).ExecuteAsync(new()
            {
                TemplateId = id,
                ExpectedConcurrencyStamp = before.Stamp,
                TemplateDto = new()
                {
                    Definitions = new()
                    {
                        Items = [new()
                        {
                            Namespace = "tenant", Key = "choice", DisplayName = "Choice",
                            PropertyType = PropertyType.Option, ExposureLevel = ExposureLevel.Public,
                            Options = [new() { Namespace = "tenant", Key = "selected", DisplayName = "Selected", Value = "selected", IsDefault = true }]
                        }]
                    }
                }
            }, deadline.Token);
        }

        await Assert.ThrowsAsync<HumanMutationAttemptRolledBackException>(() => ReplaceAsync(scope.ServiceProvider));
        await Assert.That(gate.Attempts).IsEqualTo(1);
        await Assert.That(gate.FirstAttempt).IsNotNull();
        await Assert.That(await TemplateUpdateState(db, session, id)).IsEqualTo(before);
        await AssertRolledBackTemplateChildren(db, session, gate.FirstAttempt!);
        using var fresh = fixture.CreateScope();
        await fixture.AuthenticateAsync(fresh);
        var response = await ReplaceAsync(fresh.ServiceProvider);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(gate.Attempts).IsEqualTo(1);
        await Assert.That(gate.FirstAttempt).IsNotNull();
        var stored = await ReadCreatedTemplate(db, session, key);
        await Assert.That(stored.DefinitionId).IsNotEqualTo(gate.FirstAttempt!.DefinitionId);
        await Assert.That(stored.OptionId).IsNotEqualTo(gate.FirstAttempt.OptionId);
        await Assert.That(stored.DefinitionCreatedAt).IsEqualTo(stored.OptionCreatedAt);
        await Assert.That(stored.RootId).IsEqualTo(id);
        await Assert.That(stored.DefaultOptionId).IsEqualTo(stored.OptionId);
        await Assert.That(response.Id).IsEqualTo(id);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TemplateConflictRequiresCurrentAuthorityBeforeAnotherBody(bool session)
    {
        var gate = new TemplateReplayGate();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(gate);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid parentId = session ? await SeedDeletionTemplate(db, false) : Guid.Empty;
        Guid grantId = await GrantTemplateAdministrator(db, data.OutsiderId);
        string key = $"retry-{Guid.CreateVersion7():N}";
        async Task RevokeAsync(CancellationToken token)
        {
            await using var revocation = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            revocation.PlatformUserRoles.Remove(
                await revocation.PlatformUserRoles.SingleAsync(row => row.Id == grantId, token));
            await revocation.SaveChangesAsync(token);
        }
        gate.Arm(db, () => CaptureCreatedTemplate(db, session, key));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<HumanMutationAttemptRolledBackException>(() =>
            CreateReplayTemplate(scope.ServiceProvider, session, parentId, key, deadline.Token));
        await RevokeAsync(deadline.Token);
        using var fresh = fixture.CreateScope();
        await fixture.AuthenticateAsync(fresh);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            CreateReplayTemplate(fresh.ServiceProvider, session, parentId, key, deadline.Token));

        await Assert.That(gate.FirstAttempt).IsNotNull();
        await Assert.That(gate.Attempts).IsEqualTo(1);
        await Assert.That(await CreatedTemplateExists(db, session, key)).IsFalse();
        Guid definitionId = gate.FirstAttempt!.DefinitionId;
        Guid optionId = gate.FirstAttempt.OptionId;
        if (session)
        {
            await Assert.That(await db.EventSessionTemplateCustomPropertyDefinitions.AnyAsync(row => row.Id == definitionId)).IsFalse();
            await Assert.That(await db.EventSessionTemplateCustomPropertyOptions.AnyAsync(row => row.Id == optionId)).IsFalse();
        }
        else
        {
            await Assert.That(await db.EventTemplateCustomPropertyDefinitions.AnyAsync(row => row.Id == definitionId)).IsFalse();
            await Assert.That(await db.EventTemplateCustomPropertyOptions.AnyAsync(row => row.Id == optionId)).IsFalse();
        }
    }

    private static async Task AssertRolledBackTemplateChildren(ExploreDbContext db, bool session, CreatedTemplateSnapshot attempt)
    {
        if (session)
        {
            await Assert.That(await db.EventSessionTemplateCustomPropertyDefinitions.AnyAsync(row => row.Id == attempt.DefinitionId)).IsFalse();
            await Assert.That(await db.EventSessionTemplateCustomPropertyOptions.AnyAsync(row => row.Id == attempt.OptionId)).IsFalse();
        }
        else
        {
            await Assert.That(await db.EventTemplateCustomPropertyDefinitions.AnyAsync(row => row.Id == attempt.DefinitionId)).IsFalse();
            await Assert.That(await db.EventTemplateCustomPropertyOptions.AnyAsync(row => row.Id == attempt.OptionId)).IsFalse();
        }
        await Assert.That(db.Database.CurrentTransaction).IsNull();
    }

    private static async Task<BaseCommandResponse<Guid>> CreateReplayTemplate(
        IServiceProvider services, bool session, Guid parentId, string key, CancellationToken token)
    {
        await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(token);
        return await (session
            ? ActivatorUtilities.CreateInstance<CreateEventSessionTemplateCommandHandler>(services).ExecuteAsync(new()
            {
                SessionTemplateDto = new()
                {
                    EventTemplateId = parentId,
                    SessionTemplateKey = key,
                    DisplayName = "Retry identity",
                    Definitions = [new()
                    {
                        Namespace = "tenant", Key = "choice", DisplayName = "Choice",
                        PropertyType = PropertyType.Option, ExposureLevel = ExposureLevel.Public,
                        Options = [new() { Namespace = "tenant", Key = "selected", DisplayName = "Selected", Value = "selected", IsDefault = true }]
                    }]
                }
            }, token)
            : ActivatorUtilities.CreateInstance<CreateEventTemplateCommandHandler>(services).ExecuteAsync(new()
            {
                TemplateDto = new()
                {
                    TemplateKey = key,
                    DisplayName = "Retry identity",
                    Definitions = [new()
                    {
                        Namespace = "tenant", Key = "choice", DisplayName = "Choice",
                        PropertyType = PropertyType.Option, ExposureLevel = ExposureLevel.Public,
                        Options = [new() { Namespace = "tenant", Key = "selected", DisplayName = "Selected", Value = "selected", IsDefault = true }]
                    }]
                }
            }, token));
    }

    private sealed record CreatedTemplateSnapshot(
        Guid RootId, Guid DefinitionId, Guid OptionId, Guid? DefaultOptionId,
        DateTime RootCreatedAt, DateTime DefinitionCreatedAt, DateTime OptionCreatedAt);

    private static CreatedTemplateSnapshot CaptureCreatedTemplate(ExploreDbContext db, bool session, string key)
    {
        if (session)
        {
            var rootEntity = db.ChangeTracker.Entries<EventSessionTemplate>().Single(row => row.Entity.SessionTemplateKey == key).Entity;
            var definition = db.ChangeTracker.Entries<EventSessionTemplateCustomPropertyDefinition>()
                .Single(row => row.Entity.EventSessionTemplateId == rootEntity.Id).Entity;
            var option = db.ChangeTracker.Entries<EventSessionTemplateCustomPropertyOption>()
                .Single(row => row.Entity.EventSessionTemplateCustomPropertyDefinitionId == definition.Id).Entity;
            return new(rootEntity.Id, definition.Id, option.Id, definition.DefaultOptionId,
                rootEntity.CreatedAt, definition.CreatedAt, option.CreatedAt);
        }
        var template = db.ChangeTracker.Entries<EventTemplate>().Single(row => row.Entity.TemplateKey == key).Entity;
        var templateDefinition = db.ChangeTracker.Entries<EventTemplateCustomPropertyDefinition>()
            .Single(row => row.Entity.EventTemplateId == template.Id).Entity;
        var templateOption = db.ChangeTracker.Entries<EventTemplateCustomPropertyOption>()
            .Single(row => row.Entity.EventTemplateCustomPropertyDefinitionId == templateDefinition.Id).Entity;
        return new(template.Id, templateDefinition.Id, templateOption.Id, templateDefinition.DefaultOptionId,
            template.CreatedAt, templateDefinition.CreatedAt, templateOption.CreatedAt);
    }

    private static async Task<CreatedTemplateSnapshot> ReadCreatedTemplate(ExploreDbContext db, bool session, string key)
    {
        if (session)
        {
            var rootEntity = await db.EventSessionTemplates.AsNoTracking().SingleAsync(row => row.SessionTemplateKey == key);
            var definition = await db.EventSessionTemplateCustomPropertyDefinitions.AsNoTracking()
                .SingleAsync(row => row.EventSessionTemplateId == rootEntity.Id);
            var option = await db.EventSessionTemplateCustomPropertyOptions.AsNoTracking()
                .SingleAsync(row => row.EventSessionTemplateCustomPropertyDefinitionId == definition.Id);
            return new(rootEntity.Id, definition.Id, option.Id, definition.DefaultOptionId,
                rootEntity.CreatedAt, definition.CreatedAt, option.CreatedAt);
        }
        var template = await db.EventTemplates.AsNoTracking().SingleAsync(row => row.TemplateKey == key);
        var templateDefinition = await db.EventTemplateCustomPropertyDefinitions.AsNoTracking()
            .SingleAsync(row => row.EventTemplateId == template.Id);
        var templateOption = await db.EventTemplateCustomPropertyOptions.AsNoTracking()
            .SingleAsync(row => row.EventTemplateCustomPropertyDefinitionId == templateDefinition.Id);
        return new(template.Id, templateDefinition.Id, templateOption.Id, templateDefinition.DefaultOptionId,
            template.CreatedAt, templateDefinition.CreatedAt, templateOption.CreatedAt);
    }

    private sealed class TemplateReplayGate : DbTransactionInterceptor
    {
        private ExploreDbContext? _target;
        private Func<CreatedTemplateSnapshot>? _capture;
        public CreatedTemplateSnapshot? FirstAttempt { get; private set; }
        public int Attempts { get; private set; }

        public void Arm(ExploreDbContext target, Func<CreatedTemplateSnapshot> capture)
        {
            _target = target;
            _capture = capture;
        }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target))
            {
                Attempts++;
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target) && Interlocked.Exchange(ref _capture, null) is { } capture)
            {
                FirstAttempt = capture();
                throw new SqliteException("Injected pre-commit template write conflict.", 5);
            }
            return ValueTask.FromResult(result);
        }
    }
}
