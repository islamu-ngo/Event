// ABOUTME: Tests the tenant typed settings document resolver path during settings cutover.
// ABOUTME: Verifies tenant JSONB document resolution stays isolated from current scalar settings caches.

namespace Explore.Infrastructure.Tests.Settings;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Explore.Persistence.Repositories;
using Explore.Tests.Shared.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;

public sealed class TypedSettingsDocumentResolverTests : IDisposable
{
    private readonly ITenantSettingsDocumentRepository _repository;
    private readonly MemoryCache _cache;
    private readonly TypedSettingsDocumentResolver _resolver;

    public TypedSettingsDocumentResolverTests()
    {
        _repository = Substitute.For<ITenantSettingsDocumentRepository>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _resolver = new TypedSettingsDocumentResolver(_repository, _cache);
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task ResolveTenantDocumentAsync_ReturnsTypedPayload()
    {
        var tenantId = Guid.NewGuid();
        var context = new SettingsResolutionContext(
            tenantId,
            RequestedDocuments: [SettingsDocumentKeys.Tenant.Branding]);
        SetupTenantDocuments(
            tenantId,
            TenantSettingsDocument.Create(
                tenantId,
                SettingsDocumentKeys.Tenant.Branding,
                schemaVersion: 2,
                defaultsVersion: "2026-05-branding",
                payloadJson: "{\"displayName\":\"Open Islamu\"}"));

        var resolved = await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(
            context,
            SettingsDocumentKeys.Tenant.Branding);

        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.DocumentKey).IsEqualTo(SettingsDocumentKeys.Tenant.Branding);
        await Assert.That(resolved.SchemaVersion).IsEqualTo(2);
        await Assert.That(resolved.DefaultsVersion).IsEqualTo("2026-05-branding");
        await Assert.That(resolved.Source).IsEqualTo(SettingsDocumentSource.Tenant);
        await Assert.That(resolved.SourceScopeId).IsEqualTo(tenantId);
        await Assert.That(resolved.Payload.DisplayName).IsEqualTo("Open Islamu");
    }

    [Test]
    public async Task ResolveTenantDocumentAsync_ReturnsNull_WhenDocumentMissing()
    {
        var tenantId = Guid.NewGuid();
        SetupTenantDocuments(tenantId);

        var resolved = await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(
            new SettingsResolutionContext(tenantId),
            SettingsDocumentKeys.Tenant.Branding);

        await Assert.That(resolved).IsNull();
    }

    [Test]
    public async Task ResolveTenantDocumentAsync_RejectsUnknownDocumentKey()
    {
        var context = new SettingsResolutionContext(Guid.NewGuid());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(context, "tenant.unknown"));
    }

    [Test]
    public async Task ResolveTenantDocumentAsync_RejectsKnownSecretKey()
    {
        var context = new SettingsResolutionContext(Guid.NewGuid());
        await Assert.That(SettingsDocumentTaxonomy.IsKnownSecretKey(
                InfrastructureSecretSettingKeys.Authentication.GoogleClientSecret))
            .IsTrue();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(
                context,
                InfrastructureSecretSettingKeys.Authentication.GoogleClientSecret));
    }

    [Test]
    public async Task ResolveTenantDocumentAsync_FailsClosed_WhenContextDidNotRequestDocument()
    {
        var context = new SettingsResolutionContext(
            Guid.NewGuid(),
            RequestedDocuments: [SettingsDocumentKeys.Tenant.PublicExperience]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(
                context,
                SettingsDocumentKeys.Tenant.Branding));
    }

    [Test]
    public async Task ResolveTenantDocumentAsync_RejectsEmptyTenantContext()
    {
        var context = new SettingsResolutionContext(Guid.Empty);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(
                context,
                SettingsDocumentKeys.Tenant.Branding));
    }

    [Test]
    public async Task ResolveTenantDocumentAsync_ReturnsNull_WhenDocumentKeyIsBlank()
    {
        var resolved = await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(
            new SettingsResolutionContext(Guid.NewGuid()),
            " ");

        await Assert.That(resolved).IsNull();
    }

    [Test]
    public async Task ResolveTenantDocumentAsync_Throws_WhenPayloadCannotDeserializeToRequestedType()
    {
        var tenantId = Guid.NewGuid();
        var context = new SettingsResolutionContext(
            tenantId,
            RequestedDocuments: [SettingsDocumentKeys.Tenant.Branding]);
        SetupTenantDocuments(
            tenantId,
            new TenantSettingsDocument
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Tenant = null!,
                DocumentKey = SettingsDocumentKeys.Tenant.Branding,
                SchemaVersion = 1,
                DefaultsVersion = "2026-05-branding",
                PayloadJson = "{\"displayName\":123}"
            });

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(async () =>
            await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(
                context,
                SettingsDocumentKeys.Tenant.Branding));
    }

    [Test]
    public async Task ResolveTenantDocumentAsync_CachesTenantDocumentBatch()
    {
        var tenantId = Guid.NewGuid();
        var context = new SettingsResolutionContext(tenantId);
        SetupTenantDocuments(
            tenantId,
            TenantSettingsDocument.Create(
                tenantId,
                SettingsDocumentKeys.Tenant.Branding,
                schemaVersion: 1,
                defaultsVersion: "2026-05-branding",
                payloadJson: "{\"displayName\":\"Cached\"}"));

        await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(context, SettingsDocumentKeys.Tenant.Branding);
        await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(context, SettingsDocumentKeys.Tenant.Branding);

        await _repository.Received(1).GetManyForTenant(
            tenantId,
            Arg.Is<IEnumerable<string>>(keys => ContainsSameKeys(keys, SettingsDocumentKeys.Tenant.All)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InvalidateTenantDocumentCache_CausesNextResolutionToReloadDocuments()
    {
        var tenantId = Guid.NewGuid();
        var context = new SettingsResolutionContext(tenantId);
        SetupTenantDocuments(
            tenantId,
            TenantSettingsDocument.Create(
                tenantId,
                SettingsDocumentKeys.Tenant.Branding,
                schemaVersion: 1,
                defaultsVersion: "2026-05-branding",
                payloadJson: "{\"displayName\":\"Cached\"}"));

        await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(context, SettingsDocumentKeys.Tenant.Branding);
        _resolver.InvalidateTenantDocumentCache(tenantId, SettingsDocumentKeys.Tenant.Branding);
        await _resolver.ResolveTenantDocumentAsync<BrandingSettings>(context, SettingsDocumentKeys.Tenant.Branding);

        await _repository.Received(2).GetManyForTenant(
            tenantId,
            Arg.Is<IEnumerable<string>>(keys => ContainsSameKeys(keys, SettingsDocumentKeys.Tenant.All)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InvalidateTenantDocumentCache_DoesNotRemoveCurrentScalarCacheEntries()
    {
        var tenantId = Guid.NewGuid();
        var scalarCacheKey = $"HierSettings:Tenant:{tenantId}";
        _cache.Set(scalarCacheKey, "scalar-value");

        _resolver.InvalidateTenantDocumentCache(tenantId, SettingsDocumentKeys.Tenant.Branding);

        await Assert.That(_cache.TryGetValue(scalarCacheKey, out string? value)).IsTrue();
        await Assert.That(value).IsEqualTo("scalar-value");
    }

    [Test]
    public async Task ConfigureInfrastructureServices_RegistersTypedResolverAlongsideCurrentScalarResolverDuringCutover()
    {
        await using var fixture = await SmtpSettingsDatabase.CreateAsync();
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        services.AddLogging();
        services.AddScoped<ISystemSettingRepository>(_ => new SystemSettingRepository(fixture.Context, fixture.MutationLock));
        services.AddScoped<ITenantSettingRepository>(_ => new TenantSettingRepository(fixture.Context, fixture.MutationLock));
        services.AddScoped<IOrganizationSettingRepository>(_ => new OrganizationSettingRepository(fixture.Context));
        services.AddScoped<IGroupSettingRepository>(_ => new GroupSettingRepository(fixture.Context));
        services.AddScoped<IGroupTenantRepository>(_ => new GroupTenantRepository(fixture.Context));
        services.AddScoped<IUserPreferenceRepository>(_ => new UserPreferenceRepository(fixture.Context));
        services.AddSingleton<ITenantContext>(fixture);
        services.AddSingleton(fixture.MutationLock);
        services.AddScoped<IEmailDeliverySettingsWriter>(_ => fixture.Writer);
        services.AddScoped<ITenantSettingsDocumentRepository>(_ => new TenantSettingsDocumentRepository(fixture.Context));

        services.ConfigureInfrastructureServices(configuration);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var scalarResolver = scope.ServiceProvider.GetService<IHierarchicalSettingsResolver>();
        var typedResolver = scope.ServiceProvider.GetService<ITypedSettingsDocumentResolver>();

        await Assert.That(scalarResolver).IsNotNull();
        await Assert.That(typedResolver).IsNotNull();
        await Assert.That(scalarResolver).IsTypeOf<HierarchicalSettingsResolver>();
        await Assert.That(typedResolver).IsTypeOf<TypedSettingsDocumentResolver>();
    }

    private void SetupTenantDocuments(Guid tenantId, params TenantSettingsDocument[] documents)
    {
        _repository.GetManyForTenant(
                tenantId,
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TenantSettingsDocument>>(documents));
    }

    private static bool ContainsSameKeys(IEnumerable<string> actual, IEnumerable<string> expected) =>
        actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected);
}
