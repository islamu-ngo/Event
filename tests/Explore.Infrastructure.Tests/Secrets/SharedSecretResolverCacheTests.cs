using System.Collections.Concurrent;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SharedSecretResolverCacheTests
{
    private const string Key = SecretDefinitionRegistry.Keys.Storage.AccessKeyId;

    [Test]
    public async Task TombstoneCompactionCannotResurrectAnInvalidatedPendingRead()
    {
        SecretBinding binding = Binding(Guid.CreateVersion7());
        await using var source = new ControlledSource([binding]);
        using ServiceProvider provider = Provider(new BindingStore([binding]), source);
        using IServiceScope readerScope = provider.CreateScope();
        using IServiceScope writerScope = provider.CreateScope();
        var reader = readerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var writer = writerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var barrier = new ReadBarrier();
        source.NextRead = barrier;
        Task<SecretResolutionResult> pending = reader.ResolveAsync(Key, binding.ScopeId);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await writer.InvalidateAsync(Key, binding.Scope, binding.ScopeId);
            for (int index = 0; index < 4096; index++)
                await writer.InvalidateAsync(Key, SecretScope.Tenant, Guid.CreateVersion7());
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        SecretResolutionResult rejected = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(rejected.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(rejected.Value).IsNull();
        string replacement = source.Retarget(binding);
        await Assert.That((await reader.ResolveAsync(Key, binding.ScopeId)).Value).IsEqualTo(replacement);
    }

    [Test]
    public async Task AnotherScopeInvalidatesAllQualifiersWithoutEvictingAnotherTenant()
    {
        Guid tenant = Guid.CreateVersion7();
        SecretBinding first = Binding(tenant, "first");
        SecretBinding second = Binding(tenant, "second");
        SecretBinding other = Binding(Guid.CreateVersion7(), "first");
        var store = new BindingStore([first, second, other]);
        await using var source = new ControlledSource([first, second, other]);
        using ServiceProvider provider = Provider(store, source);
        using IServiceScope readerScope = provider.CreateScope();
        using IServiceScope writerScope = provider.CreateScope();
        var reader = readerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var writer = writerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        await Assert.That(ReferenceEquals(
            readerScope.ServiceProvider.GetRequiredService<ISecretBindingRepository>(),
            writerScope.ServiceProvider.GetRequiredService<ISecretBindingRepository>())).IsFalse();
        await Assert.That(ReferenceEquals(reader, writer)).IsFalse();
        await Assert.That(ReferenceEquals(
            readerScope.ServiceProvider.GetRequiredService<SecretResolverCacheState>(),
            writerScope.ServiceProvider.GetRequiredService<SecretResolverCacheState>())).IsTrue();

        var firstWarm = await reader.ResolveQualifiedAsync(Key, SecretScope.Tenant, tenant, "first");
        var secondWarm = await reader.ResolveQualifiedAsync(Key, SecretScope.Tenant, tenant, "second");
        SecretResolutionResult otherWarm = await reader.ResolveQualifiedAsync(
            Key, SecretScope.Tenant, other.ScopeId, "first");
        source.ChangeConfiguredValue(first);
        source.ChangeConfiguredValue(second);
        source.ChangeConfiguredValue(other);
        int otherReads = source.ReadCount(other);

        await writer.InvalidateAsync(Key, SecretScope.Tenant, tenant);
        SecretResolutionResult firstFresh = await reader.ResolveQualifiedAsync(Key, SecretScope.Tenant, tenant, "first");
        SecretResolutionResult secondFresh = await writer.ResolveQualifiedAsync(Key, SecretScope.Tenant, tenant, "second");
        SecretResolutionResult otherCached = await writer.ResolveQualifiedAsync(
            Key, SecretScope.Tenant, other.ScopeId, "first");

        await Assert.That(firstFresh.Value).IsEqualTo(source.Values[first.Id]);
        await Assert.That(secondFresh.Value).IsEqualTo(source.Values[second.Id]);
        await Assert.That(ReferenceEquals(firstFresh.Secret, firstWarm.Secret)).IsFalse();
        await Assert.That(ReferenceEquals(secondFresh.Secret, secondWarm.Secret)).IsFalse();
        await Assert.That(source.ReadCount(first)).IsEqualTo(2);
        await Assert.That(source.ReadCount(second)).IsEqualTo(2);
        await Assert.That(otherCached.Value).IsEqualTo(otherWarm.Value);
        await Assert.That(ReferenceEquals(otherCached.Secret, otherWarm.Secret)).IsTrue();
        await Assert.That(source.ReadCount(other)).IsEqualTo(otherReads);
    }

    [Test]
    [Arguments("hierarchy")]
    [Arguments("qualified")]
    [Arguments("binding")]
    public async Task InvalidationRejectsBlockedProviderValueBeforeReturnAndPublication(string route)
    {
        SecretBinding binding = Binding(Guid.CreateVersion7());
        await using var source = new ControlledSource([binding]);
        using ServiceProvider provider = Provider(new BindingStore([binding]), source);
        using IServiceScope readerScope = provider.CreateScope();
        using IServiceScope writerScope = provider.CreateScope();
        var reader = readerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var writer = writerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var barrier = new ReadBarrier();
        source.NextRead = barrier;

        Task<SecretResolutionResult> pending = Resolve(reader, binding, route);
        SecretResolutionResult fresh;
        string replacement;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Invalidation fences reads even when the immutable owner's revision has not changed.
            replacement = source.Values[binding.Id];
            source.ChangeConfiguredValue(binding);
            await writer.InvalidateAsync(Key, binding.Scope, binding.ScopeId);
            fresh = await Resolve(writer, binding, route);
            source.ChangeConfiguredValue(binding);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        SecretResolutionResult rejected = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        SecretResolutionResult cached = await Resolve(reader, binding, route);

        await Assert.That(rejected.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(rejected.Value).IsNull();
        await Assert.That(fresh.Value).IsEqualTo(replacement);
        await Assert.That(cached.Value).IsEqualTo(fresh.Value);
        await Assert.That(ReferenceEquals(cached.Secret, fresh.Secret)).IsTrue();
        await Assert.That(source.ReadCount(binding)).IsEqualTo(2);
    }

    [Test]
    [Arguments("hierarchy")]
    [Arguments("qualified")]
    [Arguments("binding")]
    public async Task InvalidationDuringBindingSelectionCannotReuseWarmValue(string route)
    {
        SecretBinding binding = Binding(Guid.CreateVersion7());
        await using var source = new ControlledSource([binding]);
        using ServiceProvider provider = Provider(new BindingStore([binding]), source);
        using IServiceScope readerScope = provider.CreateScope();
        using IServiceScope writerScope = provider.CreateScope();
        var reader = readerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var writer = writerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var warm = await Resolve(writer, binding, route);
        var repository = readerScope.ServiceProvider.GetRequiredService<CacheTestRepository>();
        var barrier = new ReadBarrier();
        repository.NextRead = barrier;

        Task<SecretResolutionResult> pending = Resolve(reader, binding, route);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            source.ChangeConfiguredValue(binding);
            await writer.InvalidateAsync(Key, binding.Scope, binding.ScopeId);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        SecretResolutionResult rejected = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        SecretResolutionResult fresh = await Resolve(writer, binding, route);

        await Assert.That(rejected.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(rejected.Value).IsNull();
        await Assert.That(fresh.Value).IsEqualTo(source.Values[binding.Id]);
        await Assert.That(ReferenceEquals(fresh.Secret, warm.Secret)).IsFalse();
        await Assert.That(source.ReadCount(binding)).IsEqualTo(2);
    }

    [Test]
    [Arguments(SecretScope.Tenant)]
    [Arguments(SecretScope.Instance)]
    public async Task HierarchyFallbackRejectsChangesToEitherSelectionDependency(SecretScope changedScope)
    {
        Guid tenant = Guid.CreateVersion7();
        SecretBinding instance = Binding(null);
        SecretBinding replacement = Binding(tenant);
        var store = new BindingStore([instance]);
        await using var source = new ControlledSource([instance, replacement]);
        using ServiceProvider provider = Provider(store, source);
        using IServiceScope readerScope = provider.CreateScope();
        using IServiceScope writerScope = provider.CreateScope();
        var reader = readerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var writer = writerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var barrier = new ReadBarrier();
        source.NextRead = barrier;

        Task<SecretResolutionResult> pending = reader.ResolveAsync(Key, tenant);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (changedScope == SecretScope.Tenant)
            {
                store.Bindings[replacement.Id] = replacement;
            }
            else
            {
                source.ChangeConfiguredValue(instance);
            }
            await writer.InvalidateAsync(Key, changedScope, changedScope == SecretScope.Tenant ? tenant : null);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        SecretResolutionResult rejected = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        SecretResolutionResult fresh = await writer.ResolveAsync(Key, tenant);
        SecretBinding winner = store.Bindings.Values.Single(binding => binding.Scope == changedScope);

        await Assert.That(rejected.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(rejected.Value).IsNull();
        await Assert.That(fresh.Value).IsEqualTo(source.Values[winner.Id]);
        await Assert.That(fresh.Scope).IsEqualTo(changedScope);
    }

    [Test]
    public async Task AnotherTenantInvalidationDoesNotRejectUnrelatedPendingRead()
    {
        SecretBinding binding = Binding(Guid.CreateVersion7());
        await using var source = new ControlledSource([binding]);
        using ServiceProvider provider = Provider(new BindingStore([binding]), source);
        using IServiceScope readerScope = provider.CreateScope();
        using IServiceScope writerScope = provider.CreateScope();
        var reader = readerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var writer = writerScope.ServiceProvider.GetRequiredService<ISecretResolver>();
        var barrier = new ReadBarrier();
        source.NextRead = barrier;
        Task<SecretResolutionResult> pending = reader.ResolveAsync(Key, binding.ScopeId);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await writer.InvalidateAsync(Key, SecretScope.Tenant, Guid.CreateVersion7());
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        SecretResolutionResult result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(result.Value).IsEqualTo(source.Values[binding.Id]);
    }

    private static Task<SecretResolutionResult> Resolve(ISecretResolver resolver, SecretBinding binding, string route) =>
        route switch
        {
            "hierarchy" => resolver.ResolveAsync(Key, binding.ScopeId),
            "qualified" => resolver.ResolveQualifiedAsync(Key, binding.Scope, binding.ScopeId, binding.Qualifier),
            "binding" => resolver.ResolveTenantBindingAsync(binding.ScopeId!.Value, binding.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

    private static SecretBinding Binding(Guid? tenant, string qualifier = "")
    {
        SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
            Key, tenant.HasValue ? SecretScope.Tenant : SecretScope.Instance, tenant,
            $"STORAGE_ACCESS_{Guid.NewGuid():N}", qualifier: qualifier);
        binding.Id = Guid.CreateVersion7();
        return binding;
    }

    private static ServiceProvider Provider(BindingStore store, ControlledSource source)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(source.Configuration);
        services.AddSingleton(source.Owner);
        services.AddScoped(_ => new CacheTestRepository(store));
        services.AddScoped<ISecretBindingRepository>(provider => provider.GetRequiredService<CacheTestRepository>());
        services.AddNativeSecretResolution();
        services.RemoveAll<ISecretSource>();
        services.AddSingleton<ISecretSource>(source);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class ReadBarrier
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task Hold(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private sealed class ControlledSource : ISecretSource, ISecretRevisionOwner, IAsyncDisposable
    {
        private readonly EnvironmentSecretSource _source;
        private readonly Dictionary<Guid, (string Name, string Value)> _replacements = [];
        private readonly ConcurrentDictionary<Guid, int> _reads = new();
        public Dictionary<Guid, string> Values { get; } = [];
        public ConfigurationRoot Configuration { get; }
        public SecretAuthorityAcquisition Owner { get; }
        public ReadBarrier? NextRead { get; set; }
        public SecretSourceType SourceType => SecretSourceType.EnvironmentVariable;

        public ControlledSource(IEnumerable<SecretBinding> bindings)
        {
            var values = new Dictionary<string, string?>();
            foreach (var binding in bindings)
            {
                string initial = Guid.NewGuid().ToString("N");
                string nextName = $"STORAGE_ACCESS_{Guid.NewGuid():N}";
                string nextValue = Guid.NewGuid().ToString("N");
                Values.Add(binding.Id, initial);
                values.Add(binding.EnvironmentVariableName!, initial);
                values.Add(nextName, nextValue);
                _replacements.Add(binding.Id, (nextName, nextValue));
            }
            Configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            Owner = SecretAuthorityAcquisition.OpenLocal(
                new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), Configuration);
            _source = new EnvironmentSecretSource(Owner);
        }

        public string Retarget(SecretBinding binding)
        {
            var replacement = _replacements[binding.Id];
            binding.SwitchToEnvironmentVariable(replacement.Name);
            Values[binding.Id] = replacement.Value;
            return replacement.Value;
        }

        // Changing configuration cannot mutate the already selected local generation.
        public void ChangeConfiguredValue(SecretBinding binding) =>
            Configuration[binding.EnvironmentVariableName!] = Guid.NewGuid().ToString("N");

        public int ReadCount(SecretBinding binding) => _reads.GetValueOrDefault(binding.Id);

        public async Task<SecretResolutionResult> GetSecretAsync(SecretBinding binding, CancellationToken cancellationToken = default)
        {
            _reads.AddOrUpdate(binding.Id, 1, static (_, count) => count + 1);
            SecretResolutionResult result = await _source.GetSecretAsync(binding, cancellationToken);
            ReadBarrier? barrier = NextRead;
            NextRead = null;
            if (barrier is not null)
                await barrier.Hold(cancellationToken);
            return result;
        }

        public bool IsRevisionCurrent(SecretBinding binding, string? revision) =>
            _source.IsRevisionCurrent(binding, revision);

        public Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default) =>
            _source.ValidateAsync(binding, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await Owner.DisposeAsync();
            Configuration.Dispose();
        }
    }

    private sealed class BindingStore(IEnumerable<SecretBinding> bindings)
    {
        public ConcurrentDictionary<Guid, SecretBinding> Bindings { get; } =
            new(bindings.ToDictionary(binding => binding.Id));
    }

    private sealed class CacheTestRepository(BindingStore store) : ISecretBindingRepository
    {
        public ReadBarrier? NextRead { get; set; }
        private IReadOnlyList<SecretBinding> Bindings => store.Bindings.Values.ToArray();

        private async Task<SecretBinding?> Read(SecretBinding? binding, CancellationToken cancellationToken)
        {
            ReadBarrier? barrier = NextRead;
            NextRead = null;
            if (barrier is not null)
                await barrier.Hold(cancellationToken);
            return binding;
        }

        public Task<SecretBinding?> GetByTenantAndIdAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Read(Bindings.SingleOrDefault(binding => binding.ScopeId == tenantId && binding.Id == bindingId), cancellationToken);
        public Task<SecretBinding?> GetByKeyAndScopeAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            GetByKeyScopeAndQualifierAsync(settingKey, scope, scopeId, "", cancellationToken);
        public Task<SecretBinding?> GetByKeyScopeAndQualifierAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) =>
            Read(Bindings.SingleOrDefault(binding => binding.SettingKey == settingKey && binding.Scope == scope && binding.ScopeId == scopeId && binding.Qualifier == qualifier), cancellationToken);
        public Task<IReadOnlyList<SecretBinding>> GetByScopeAsync(SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SecretBinding>>([.. Bindings.Where(binding => binding.Scope == scope && binding.ScopeId == scopeId)]);
        public Task<IReadOnlyList<SecretBinding>> GetAllForKeyAsync(string settingKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SecretBinding>>([.. Bindings.Where(binding => binding.SettingKey == settingKey)]);
        public async Task<bool> ExistsForScopeAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            await GetByKeyAndScopeAsync(settingKey, scope, scopeId, cancellationToken) is not null;
        public async Task<bool> ExistsForScopeAndQualifierAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) =>
            await GetByKeyScopeAndQualifierAsync(settingKey, scope, scopeId, qualifier, cancellationToken) is not null;
        public Task<SecretBinding?> GetById(Guid id) => Task.FromResult(Bindings.SingleOrDefault(binding => binding.Id == id));
        public Task<IReadOnlyList<SecretBinding>> GetAll() => Task.FromResult(Bindings);
        public Task<(IReadOnlyList<SecretBinding> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => Task.FromResult((Bindings, Bindings.Count));
        public Task<bool> Exists(Guid id) => Task.FromResult(store.Bindings.ContainsKey(id));
        public Task<SecretBinding> Create(SecretBinding entity) => throw new NotSupportedException();
        public Task Update(SecretBinding entity) => throw new NotSupportedException();
        public Task Delete(SecretBinding entity) => throw new NotSupportedException();
    }
}
