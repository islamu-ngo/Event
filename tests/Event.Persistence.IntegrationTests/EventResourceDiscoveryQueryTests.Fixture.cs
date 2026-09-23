using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceDiscoveryQueryTests
{
    private async Task<(EventResourcePersistenceTests.ResourceScope Scope, Guid User)> SeedAsync()
    {
        var scope = await database.SeedScopeAsync();
        await using var context = database.CreateContext();
        var user = (await context.Actors.SingleAsync(actor => actor.Id == scope.ActorId)).UserId!.Value;
        var parent = await context.Events.SingleAsync(value => value.Id == scope.EventAId);
        parent.Publish(Now);
        parent.OrganizerActorId = scope.ActorId;
        context.TenantUsers.Add(new TenantUser { Id = Guid.CreateVersion7(), TenantId = scope.TenantAId,
            Tenant = null!, UserId = user, User = null!, ActorId = scope.ActorId,
            StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now });
        await context.SaveChangesAsync();
        return (scope, user);
    }

    private async Task<Guid> MemberAsync(Guid tenantId)
    {
        await using var context = database.CreateContext();
        var user = new User { Id = Guid.CreateVersion7(), Pii = new UserPii {
            Email = $"reader-{Guid.CreateVersion7():N}@example.test", FirstName = "Reader", LastName = "Member" } };
        context.Users.Add(user);
        context.TenantUsers.Add(new TenantUser { Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!,
            UserId = user.Id, User = user, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now });
        await context.SaveChangesAsync();
        return user.Id;
    }

    private static EventResource Resource(EventResourcePersistenceTests.ResourceScope scope, Guid user, int sort,
        EventResourceAudienceKindEnum audience, EventResourceDisclosureModeEnum mode = EventResourceDisclosureModeEnum.EligibleOnly,
        Guid? alternative = null)
    {
        var id = Guid.CreateVersion7();
        var resource = EventResource.CreateDraft(id, scope.TenantAId, scope.EventAId, null,
            new EventResourceMetadata { Title = $"private-{id:N}", PublicTitle = $"public-{id:N}",
                Description = $"description-{id:N}", SensitiveNotes = $"sensitive-{id:N}", LanguageCode = "en",
                AccessibilityNote = $"accessibility-{id:N}", Kind = EventResourceKindEnum.GeneralDocument,
                DisclosureMode = mode, SortOrder = sort, AccessibleAlternativeEventResourceId = alternative },
            EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
            [EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, id, audience)], user, Now);
        resource.SetExternalDestination(Guid.CreateVersion7().ToString("N"), 1, "https://resources.example.test",
            resource.ConcurrencyStamp, user, Now);
        resource.Publish(new(scope.TenantAId, scope.EventAId, null, EventStatusEnum.Published, false, true,
            null, false, new(new(Now), new(Now.AddHours(1)), null, null)), true, resource.ConcurrencyStamp, user, Now);
        return resource;
    }

    private async Task SaveAsync(params EventResource[] rows)
    {
        await using var context = database.CreateContext();
        context.EventResources.AddRange(rows);
        await context.SaveChangesAsync();
    }

    private EventResourceAudienceWorkflow Workflow(ExploreDbContext context, Guid tenantId, Guid? subject,
        Provider? provider = null, bool isMachine = false, string[]? origins = null)
    {
        var repository = new EventResourceRepository(context);
        var unit = new EfCoreUnitOfWork(context);
        var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
        governance.ReadAsync(tenantId, Arg.Any<CancellationToken>()).Returns(EventResourceGovernancePolicy.Create(
            Enum.GetValues<EventResourceDeliveryTypeEnum>(), Enum.GetValues<EventResourceAudienceKindEnum>(),
            [EventResourceGovernancePolicy.PdfMediaType], 10_485_760, false, origins ?? [], 30, 500, long.MaxValue));
        var routes = Substitute.For<IEventResourceProviderSnapshotReader>();
        routes.ReadAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new EventResourceProviderSnapshot(EventResourceProviderMode.Local, "", "default"));
        var tenant = Substitute.For<ITenantContext>(); tenant.TenantId.Returns(tenantId);
        var user = Substitute.For<ICurrentUserService>(); user.IsAuthenticated.Returns(subject.HasValue); user.UserId.Returns(subject);
        var machine = Substitute.For<IMachinePrincipalAccessor>(); machine.IsMachineCaller.Returns(isMachine);
        return new(repository, unit, new(unit, new EventResourceAuthoritySnapshotReader(repository,
            new EventAuthoritySnapshotService(context), governance), routes, provider ?? new Provider(), new Clock()),
            tenant, user, machine, _cursors);
    }

    private sealed class Provider : IEventResourceAuthorizationProvider
    {
        public List<int> Sizes { get; } = [];
        public string? Failure { get; init; }
        public Func<IReadOnlyList<EventResourceProviderInput>, Task>? BeforeDecision { get; init; }
        public Func<EventResourceProviderInput, EventResourceProviderDecision> Decide { get; set; } = _ => EventResourceProviderDecision.Allow;
        public Task<EventResourceProviderDecision> CheckAsync(EventResourceProviderInput input, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Discovery must use category-batched authority.");
        public async Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken cancellationToken)
        {
            Sizes.Add(inputs.Count);
            if (BeforeDecision is not null) await BeforeDecision(inputs);
            if (Failure == "timeout") throw new TimeoutException();
            IReadOnlyList<EventResourceProviderDecision> result = Failure switch
            {
                "partial" => [], "malformed" => inputs.Select(_ => (EventResourceProviderDecision)int.MaxValue).ToArray(),
                _ => inputs.Select(Decide).ToArray()
            };
            return result;
        }
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed record Sql(string Text, (string Name, object Value)[] Parameters);
    private sealed class SqlRecorder : DbCommandInterceptor
    {
        public List<Sql> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
                Commands.Add(new(command.CommandText, command.Parameters.Cast<DbParameter>().Select(parameter => (parameter.ParameterName, parameter.Value!)).ToArray()));
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<string[]> ExplainAsync(ExploreDbContext context, Sql sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql.Text;
        foreach (var value in sql.Parameters)
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = value.Name; parameter.Value = value.Value;
            command.Parameters.Add(parameter);
        }
        var plans = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) plans.Add(reader.GetString(3));
        return plans.ToArray();
    }
}
