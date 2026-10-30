using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Diagnostics.Metrics;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitResolverOwnershipTests
{
    [Test]
    public async Task NativeRegistrationSharesTheCacheAcrossScopedRepositoriesWithoutOwningTheAuthority()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["MAIL_SMTP_PASSWORD"] = Guid.NewGuid().ToString("N") }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<ISecretBindingRepository>(_ => Substitute.For<ISecretBindingRepository>());
        services.AddSingleton(owner);
        services.AddNativeSecretResolution();
        await using (var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        {
            await using var firstScope = provider.CreateAsyncScope();
            await using var secondScope = provider.CreateAsyncScope();
            await Assert.That(ReferenceEquals(
                firstScope.ServiceProvider.GetRequiredService<ISecretBindingRepository>(),
                secondScope.ServiceProvider.GetRequiredService<ISecretBindingRepository>())).IsFalse();
            var first = firstScope.ServiceProvider.GetRequiredService<ISecretResolver>();
            var second = secondScope.ServiceProvider.GetRequiredService<ISecretResolver>();
            var status = await firstScope.ServiceProvider.GetRequiredService<ISecretAuthorityStatusReader>().ReadAsync();
            await Assert.That(status.Status).IsEqualTo("configured");
            var cached = await first.ResolveAsync(SecretDefinitionRegistry.Keys.Smtp.Password, null);
            var shared = await second.ResolveAsync(SecretDefinitionRegistry.Keys.Smtp.Password, null);
            await Assert.That(cached.IsResolved).IsTrue();
            await Assert.That(ReferenceEquals(cached.Secret, shared.Secret)).IsTrue();
            await first.InvalidateAsync(SecretDefinitionRegistry.Keys.Smtp.Password, SecretScope.Instance, null);
            var refreshed = await second.ResolveAsync(SecretDefinitionRegistry.Keys.Smtp.Password, null);
            await Assert.That(refreshed.IsResolved).IsTrue();
            await Assert.That(ReferenceEquals(cached.Secret, refreshed.Secret)).IsFalse();
            await Assert.That(refreshed.Secret!.BindingRevision).IsEqualTo(cached.Secret!.BindingRevision);
        }
        var afterContainerDisposal = await owner.PrepareGroupAsync(
            [new("/smtp", "MAIL_SMTP_PASSWORD", "runtime")]);
        await Assert.That(afterContainerDisposal.IsSuccess).IsTrue();
        using var retained = afterContainerDisposal.Value;
    }

    [Test]
    public async Task RegistryDefaultKeepsItsRevisionButRetiredOwnerCannotServeTheCache()
    {
        await using var fixture = new Fixture();
        var first = await fixture.Resolver.ResolveAsync(SecretDefinitionRegistry.Keys.Smtp.Password, null);
        var cached = await fixture.Resolver.ResolveAsync(SecretDefinitionRegistry.Keys.Smtp.Password, null);
        await Assert.That(first.IsResolved).IsTrue();
        await Assert.That(cached.Secret!.BindingRevision).IsEqualTo(first.Secret!.BindingRevision);
        await fixture.Owner.DisposeAsync();
        var retired = await fixture.Resolver.ResolveAsync(SecretDefinitionRegistry.Keys.Smtp.Password, null);
        await Assert.That(retired.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(retired.Secret).IsNull();
    }

    [Test]
    public async Task RevisionInventoryEvictionFencesAnOtherwiseLiveCachedCredential()
    {
        await using var fixture = new Fixture();
        SecretBinding binding = Binding();
        fixture.Repository.GetByKeyAndScopeAsync(binding.SettingKey, SecretScope.Instance, null,
            Arg.Any<CancellationToken>()).Returns(binding);
        var first = await fixture.Resolver.ResolveAsync(binding.SettingKey, null);
        for (int index = 0; index < 4096; index++)
            await fixture.Source.GetSecretAsync(Binding());
        await Assert.That(fixture.Source.IsRevisionCurrent(binding, first.Secret!.BindingRevision)).IsFalse();
        var refreshed = await fixture.Resolver.ResolveAsync(binding.SettingKey, null);
        await Assert.That(refreshed.IsResolved).IsTrue();
        await Assert.That(refreshed.Secret!.BindingRevision).IsNotEqualTo(first.Secret.BindingRevision);
    }

    [Test]
    public async Task SameBindingIdCannotServeCachedMaterialAfterCoordinateRetargeting()
    {
        await using var fixture = new Fixture();
        SecretBinding binding = Binding();
        fixture.Repository.GetByKeyAndScopeAsync(binding.SettingKey, SecretScope.Instance, null,
            Arg.Any<CancellationToken>()).Returns(binding);
        var first = await fixture.Resolver.ResolveAsync(binding.SettingKey, null);
        binding.EnvironmentVariableName = "TENANT_MAIL_PASSWORD";
        var retargeted = await fixture.Resolver.ResolveAsync(binding.SettingKey, null);
        await Assert.That(retargeted.Value).IsEqualTo(fixture.Alternate);
        await Assert.That(retargeted.Secret!.BindingRevision).IsNotEqualTo(first.Secret!.BindingRevision);
    }

    private static SecretBinding Binding()
    {
        var binding = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.Smtp.Password,
            SecretScope.Instance, null, "MAIL_SMTP_PASSWORD");
        binding.Id = Guid.CreateVersion7();
        return binding;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ConfigurationRoot _configuration;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        private readonly SecretResolverMetrics _metrics;
        internal string Alternate { get; } = Guid.NewGuid().ToString("N");
        internal ISecretBindingRepository Repository { get; } = Substitute.For<ISecretBindingRepository>();
        internal SecretAuthorityAcquisition Owner { get; }
        internal EnvironmentSecretSource Source { get; }
        internal SecretResolver Resolver { get; }

        internal Fixture()
        {
            _configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["MAIL_SMTP_PASSWORD"] = Guid.NewGuid().ToString("N"),
                    ["TENANT_MAIL_PASSWORD"] = Alternate
                }).Build();
            Owner = SecretAuthorityAcquisition.OpenLocal(
                new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), _configuration);
            Source = new EnvironmentSecretSource(Owner);
            _metrics = new SecretResolverMetrics(_services.GetRequiredService<IMeterFactory>());
            Resolver = new SecretResolver(Repository, [Source], new SecretResolverCacheState(_cache),
                _metrics, NullLogger<SecretResolver>.Instance, Owner);
        }

        public async ValueTask DisposeAsync()
        {
            await Owner.DisposeAsync();
            _metrics.Dispose();
            await _services.DisposeAsync();
            _cache.Dispose();
            _configuration.Dispose();
        }
    }
}
