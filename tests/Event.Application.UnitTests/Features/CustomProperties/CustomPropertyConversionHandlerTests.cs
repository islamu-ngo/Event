using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCustomProperties.Handlers.Commands;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;

namespace Event.Application.UnitTests.Features.CustomProperties;

public sealed class CustomPropertyConversionHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000021");
    private static readonly Guid UserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000022");
    private static readonly Guid EventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000023");
    private static readonly Guid DefinitionId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000024");
    private static readonly Guid Stamp = Guid.Parse("018e4e5c-7f00-7000-8000-000000000025");

    [Test]
    public async Task Create_IgnoresForgedIdentityLifecycleAndDefaultOption_UsesTrustedContext()
    {
        var input = JsonSerializer.Deserialize<CreateEventCustomPropertyDefinitionDto>("""
            {"EventId":"018e4e5c-7f00-7000-8000-000000000023",
             "Namespace":"tenant.community","Key":"language","DisplayName":"Language",
             "PropertyType":0,"ExposureLevel":0,"IsActive":true,
             "TenantId":"018e4e5c-7f00-7000-8000-000000000099",
             "Id":"018e4e5c-7f00-7000-8000-000000000099",
             "CreatedBy":"018e4e5c-7f00-7000-8000-000000000099","IsDeleted":true,
             "SourceTemplateId":"018e4e5c-7f00-7000-8000-000000000099",
             "DefaultOptionId":"018e4e5c-7f00-7000-8000-000000000099"}
            """)! with { PropertyType = PropertyType.Text, ExposureLevel = ExposureLevel.OrganizerOnly };
        var store = new EventStore();
        var context = new RequestContext(TenantId, UserId);
        var handler = new CreateEventCustomPropertyDefinitionCommandHandler(
            store, new CustomPropertyGovernancePolicy(), new QuotaResolver(), context, context,
            new InlineCache(), new InlineUnitOfWork());
        var result = await handler.Handle(new CreateEventCustomPropertyDefinitionCommand { DefinitionDto = input }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        var saved = (await store.GetAllDefinitionsForEvent(EventId)).Single();
        await Assert.That(saved.TenantId).IsEqualTo(TenantId);
        await Assert.That(saved.CreatedBy).IsEqualTo(UserId);
        await Assert.That(saved.UpdatedBy).IsEqualTo(UserId);
        await Assert.That(saved.IsDeleted).IsFalse();
        await Assert.That(saved.SourceTemplateId).IsNull();
        await Assert.That(saved.DefaultOptionId).IsNull();
        await Assert.That(saved.DisplayName).IsEqualTo("Language");
        await Assert.That(saved.Id).IsNotEqualTo(Guid.Parse("018e4e5c-7f00-7000-8000-000000000099"));
    }

    [Test]
    public async Task Update_AppliesExplicitClear_PreservesIdentityProvenanceAndExistingOptions()
    {
        var originalAuthor = Guid.Parse("018e4e5c-7f00-7000-8000-000000000026");
        var definition = CreateDefinition();
        definition.CreatedBy = originalAuthor;
        definition.CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        definition.SourceTemplateId = originalAuthor;
        definition.SourceTemplateKey = "conference";
        definition.SourceTemplateVersion = 7;
        definition.InstantiatedAt = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        definition.LastSyncedFromTemplateAt = definition.InstantiatedAt;
        definition.PropertyType = PropertyType.Option;
        definition.Description = "previous";
        var option = new EventCustomPropertyOption
        {
            Id = originalAuthor, EventCustomPropertyDefinitionId = DefinitionId,
            Namespace = "tenant.community", Key = "ar", DisplayName = "Arabic", Value = "ar", IsActive = true
        };
        definition.AddOption(option);
        definition.DefaultOptionId = option.Id;
        definition.DefaultOption = option;
        var store = new EventStore(definition);
        var handler = UpdateHandler(store);
        var patch = JsonSerializer.Deserialize<UpdateEventCustomPropertyDefinitionDto>("""
            {"Metadata":{"DisplayName":"Updated"},"TenantId":"018e4e5c-7f00-7000-8000-000000000099",
             "ConcurrencyStamp":"018e4e5c-7f00-7000-8000-000000000099","IsDeleted":true}
            """)! with
        {
            Metadata = new UpdateCustomPropertyDefinitionMetadataDto
            {
                DisplayName = "Updated", Description = OptionalUpdate<string?>.Set(null)
            }
        };
        var result = await handler.Handle(new UpdateEventCustomPropertyDefinitionCommand
        {
            DefinitionId = DefinitionId, ExpectedConcurrencyStamp = Stamp, TenantId = Guid.NewGuid(), DefinitionDto = patch
        }, CancellationToken.None);
        var saved = (await store.GetAllDefinitionsForEvent(EventId)).Single();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(saved.DisplayName).IsEqualTo("Updated");
        await Assert.That(saved.Description).IsNull();
        await Assert.That(saved.DefaultTextValue).IsNull();
        await Assert.That(saved.Id).IsEqualTo(DefinitionId);
        await Assert.That(saved.ConcurrencyStamp).IsEqualTo(Stamp);
        await Assert.That(saved.TenantId).IsEqualTo(TenantId);
        await Assert.That(saved.EventId).IsEqualTo(EventId);
        await Assert.That(saved.CreatedBy).IsEqualTo(originalAuthor);
        await Assert.That(saved.CreatedAt).IsEqualTo(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(saved.SourceTemplateId).IsEqualTo(originalAuthor);
        await Assert.That(saved.SourceTemplateKey).IsEqualTo("conference");
        await Assert.That(saved.SourceTemplateVersion).IsEqualTo(7);
        await Assert.That(saved.LastSyncedFromTemplateAt).IsEqualTo(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(saved.DefaultOptionId).IsEqualTo(option.Id);
        await Assert.That(ReferenceEquals(saved.DefaultOption, option)).IsTrue();
        await Assert.That(ReferenceEquals(saved.Options.Single(), option)).IsTrue();
        await Assert.That(saved.IsDeleted).IsFalse();
        await Assert.That(saved.UpdatedBy).IsEqualTo(UserId);
    }

    [Test]
    public async Task Update_StaleConcurrencyStamp_DoesNotChangeTrackedState()
    {
        var store = new EventStore(CreateDefinition());
        var handler = UpdateHandler(store);
        await Assert.That(async () => await handler.Handle(new UpdateEventCustomPropertyDefinitionCommand
        {
            DefinitionId = DefinitionId, ExpectedConcurrencyStamp = UserId,
            DefinitionDto = new UpdateEventCustomPropertyDefinitionDto
            {
                Metadata = new UpdateCustomPropertyDefinitionMetadataDto { DisplayName = "forged" }
            }
        }, CancellationToken.None)).Throws<ConcurrencyConflictException>();
        await Assert.That((await store.GetAllDefinitionsForEvent(EventId)).Single().DisplayName).IsEqualTo("Language");
    }

    private static UpdateEventCustomPropertyDefinitionCommandHandler UpdateHandler(EventStore store) => new(
        store, new ProjectionUpdater(), new CustomPropertyGovernancePolicy(), new QuotaResolver(),
        new RequestContext(TenantId, UserId), new InlineCache(), new InlineUnitOfWork());

    [Test]
    [Arguments(PropertyType.Text)]
    [Arguments(PropertyType.Number)]
    [Arguments(PropertyType.Boolean)]
    [Arguments(PropertyType.DateTime)]
    public async Task SetValue_RoundTripsTypedPayloadWithTrustedAudit(PropertyType propertyType)
    {
        var definition = CreateDefinition();
        definition.PropertyType = propertyType;
        var store = new EventStore(definition);
        var context = new RequestContext(TenantId, UserId);
        var input = new SetEventCustomPropertyValueDto
        {
            EventCustomPropertyDefinitionId = DefinitionId, EventId = EventId,
            TextValue = propertyType == PropertyType.Text ? "Arabic" : null,
            NumberValue = propertyType == PropertyType.Number ? 0m : null,
            BooleanValue = propertyType == PropertyType.Boolean ? false : null,
            DateTimeValue = propertyType == PropertyType.DateTime ? new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero) : null
        };
        var handler = new SetEventCustomPropertyValueCommandHandler(store, new ProjectionUpdater(), new InlineUnitOfWork(), context, context);
        var result = await handler.Handle(new SetEventCustomPropertyValueCommand { ValueDto = input }, CancellationToken.None);
        var saved = (await store.GetValuesForEvent(EventId)).Single();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(saved.TenantId).IsEqualTo(TenantId);
        await Assert.That(saved.CreatedBy).IsEqualTo(UserId);
        await Assert.That(saved.IsDeleted).IsFalse();
        await Assert.That(saved.TextValue).IsEqualTo(input.TextValue);
        await Assert.That(saved.NumberValue).IsEqualTo(input.NumberValue);
        await Assert.That(saved.BooleanValue).IsEqualTo(input.BooleanValue);
        await Assert.That(saved.DateTimeValue).IsEqualTo(input.DateTimeValue);
        await Assert.That(saved.OptionId).IsNull();
    }

    [Test]
    public async Task SetMultiValues_UsesCommandScopeAndSequence_NotItemIdentityOrOrdinal()
    {
        var definition = CreateDefinition();
        definition.IsMulti = true;
        var store = new EventStore(definition);
        var context = new RequestContext(TenantId, UserId);
        var handler = new SetEventCustomPropertyMultiValuesCommandHandler(store, new ProjectionUpdater(), new QuotaResolver(), context, context, new InlineUnitOfWork());
        var result = await handler.Handle(new SetEventCustomPropertyMultiValuesCommand
        {
            DefinitionId = DefinitionId, EventId = EventId,
            Values =
            [
                new SetEventCustomPropertyValueDto { EventCustomPropertyDefinitionId = UserId, EventId = UserId, Ordinal = 9, TextValue = "Arabic" },
                new SetEventCustomPropertyValueDto { EventCustomPropertyDefinitionId = UserId, EventId = UserId, Ordinal = 8, TextValue = "English" }
            ]
        }, CancellationToken.None);
        var saved = await store.GetValuesForEvent(EventId);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(saved.Select(value => value.TextValue).SequenceEqual(["Arabic", "English"])).IsTrue();
        await Assert.That(saved.Select(value => value.Ordinal).SequenceEqual([0, 1])).IsTrue();
        await Assert.That(saved.All(value => value.EventCustomPropertyDefinitionId == DefinitionId && value.EventId == EventId
            && value.TenantId == TenantId && value.CreatedBy == UserId)).IsTrue();
    }

    private static EventCustomPropertyDefinition CreateDefinition() => new()
    {
        Id = DefinitionId, ConcurrencyStamp = Stamp, EventId = EventId, TenantId = TenantId,
        Namespace = "tenant.community", Key = "language", DisplayName = "Language",
        PropertyType = PropertyType.Text, ExposureLevel = ExposureLevel.OrganizerOnly, IsActive = true
    };

    private sealed record RequestContext(Guid TenantId, Guid? UserId) : ITenantContext, ICurrentUserService
    {
        public bool IsAuthenticated => UserId.HasValue;
    }

    private sealed class QuotaResolver : ICustomPropertyQuotaResolver
    {
        public Task<int> GetIntAsync(string key, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(100);
        public Task<bool> GetBoolAsync(string key, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // This fixture executes the actual handler delegate; it makes no database transaction guarantee.
    private sealed class InlineUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class InlineCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => factory(state, cancellationToken);
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ProjectionUpdater : IEventCustomPropertyProjectionUpdater
    {
        public Task UpdateForDefinitionAsync(Guid definitionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateForValueAsync(Guid valueId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveForDefinitionAsync(Guid definitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RefreshForEventAsync(Guid eventId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectionRebuildResult> RebuildForTenantAsync(Guid tenantId, int? batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DrainDirtyScopesForTenantAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EventStore(params EventCustomPropertyDefinition[] definitions) : IEventCustomPropertyRepository
    {
        private readonly List<EventCustomPropertyDefinition> _definitions = [.. definitions];
        private readonly List<EventCustomPropertyValue> _values = [];
        public Task<EventCustomPropertyDefinition?> GetDefinitionWithDetails(Guid id) => Task.FromResult(_definitions.SingleOrDefault(item => item.Id == id));
        public Task<EventCustomPropertyDefinition?> GetTrackedDefinitionWithOptions(Guid id, CancellationToken cancellationToken) => GetDefinitionWithDetails(id);
        public Task<List<EventCustomPropertyDefinition>> GetAllDefinitionsForEvent(Guid eventId) => Task.FromResult(_definitions.Where(item => item.EventId == eventId).ToList());
        public Task<int> CountDefinitionsForEvent(Guid eventId, CancellationToken cancellationToken) => Task.FromResult(_definitions.Count(item => item.EventId == eventId));
        public Task<bool> ExistsDefinitionKey(Guid eventId, string namespaceValue, string key, Guid? excludeDefinitionId = null) => Task.FromResult(_definitions.Any(item => item.EventId == eventId && item.Namespace == namespaceValue && item.Key == key && item.Id != excludeDefinitionId));
        public Task<EventCustomPropertyDefinition> CreateWithOptions(EventCustomPropertyDefinition definition, IReadOnlyCollection<EventCustomPropertyOption> options, Guid? defaultOptionId, CancellationToken cancellationToken)
        {
            if (definition.Id == Guid.Empty) definition.Id = DefinitionId;
            foreach (var option in options)
            {
                option.EventCustomPropertyDefinitionId = definition.Id;
                definition.AddOption(option);
            }
            definition.DefaultOptionId = defaultOptionId;
            _definitions.Add(definition);
            return Task.FromResult(definition);
        }
        public Task Update(EventCustomPropertyDefinition entity)
        {
            if (!_definitions.Contains(entity)) throw new InvalidOperationException("Update must retain the tracked entity.");
            return Task.CompletedTask;
        }
        public Task<EventCustomPropertyDefinition> UpdateWithOptions(EventCustomPropertyDefinition definition, IReadOnlyCollection<EventCustomPropertyOption> options, Guid? defaultOptionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(List<EventCustomPropertyDefinition> Items, int TotalCount)> GetDefinitionsForEventPaged(Guid eventId, int pageNumber, int pageSize) => throw new NotSupportedException();
        public Task<List<EventCustomPropertyDefinition>> GetTrackedDefinitionsForEvent(Guid eventId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteDefinition(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomPropertyPurgeDependencySummary?> GetPurgeDependencies(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> PurgeDefinition(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<EventCustomPropertyValue>> GetValuesForEvent(Guid eventId) => Task.FromResult(_values.Where(value => value.EventId == eventId).ToList());
        public Task<List<EventCustomPropertyValue>> GetValuesForDefinition(Guid definitionId) => throw new NotSupportedException();
        public Task<EventCustomPropertyValue> SetValue(EventCustomPropertyValue value, CancellationToken cancellationToken)
        {
            _values.RemoveAll(existing => existing.EventCustomPropertyDefinitionId == value.EventCustomPropertyDefinitionId && existing.Ordinal == value.Ordinal);
            _values.Add(value);
            return Task.FromResult(value);
        }
        public Task<EventCustomPropertyOption> CreateOption(EventCustomPropertyOption option, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateOption(EventCustomPropertyOption option, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetMultiValues(Guid definitionId, Guid eventId, IReadOnlyCollection<EventCustomPropertyValue> values, CancellationToken cancellationToken)
        {
            _values.RemoveAll(value => value.EventCustomPropertyDefinitionId == definitionId && value.EventId == eventId);
            _values.AddRange(values);
            return Task.CompletedTask;
        }
        public Task<EventCustomPropertyDefinition?> GetById(Guid id) => GetDefinitionWithDetails(id);
        public Task<IReadOnlyList<EventCustomPropertyDefinition>> GetAll() => throw new NotSupportedException();
        public Task<(IReadOnlyList<EventCustomPropertyDefinition> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => throw new NotSupportedException();
        public Task<bool> Exists(Guid id) => throw new NotSupportedException();
        public Task<EventCustomPropertyDefinition> Create(EventCustomPropertyDefinition entity) => throw new NotSupportedException();
        public Task Delete(EventCustomPropertyDefinition entity) => throw new NotSupportedException();
    }
}
