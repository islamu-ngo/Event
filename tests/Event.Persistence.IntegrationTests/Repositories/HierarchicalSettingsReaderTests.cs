using Explore.Persistence.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class HierarchicalSettingsReaderTests
{
    [Test]
    public async Task ReaderWithoutHumanWriterPreservesCachedAndAuthoritativeReads()
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ConnectionString);
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection)
            .UseSnakeCaseNamingConvention();
        await using var context = new ExploreDbContext(options.Options);
        await context.Database.EnsureCreatedAsync();
        context.Set<SettingValueTypeLookup>().Add(new()
        {
            Id = (int)SettingValueType.String,
            MasterCode = "string",
            FullName = "String"
        });
        var scalar = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = "reader.regression.scalar",
            Value = "\"original\"",
            ValueType = SettingValueType.String,
            CreatedAt = DateTime.UnixEpoch,
            IsLocked = true
        };
        var smtp = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.Email.SmtpHost,
            Value = "\"first.invalid\"",
            ValueType = SettingValueType.String,
            CreatedAt = DateTime.UnixEpoch
        };
        context.SystemSettings.AddRange(scalar, smtp);
        await context.SaveChangesAsync();
        var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var services = new ServiceCollection();
        services.AddSingleton<ISystemSettingRepository>(SystemSettingRepository.CreateReadOnly(context, mutationLock));
        services.AddSingleton<ITenantSettingRepository>(TenantSettingRepository.CreateReadOnly(context, mutationLock));
        services.AddSingleton<IOrganizationSettingRepository>(new OrganizationSettingRepository(context));
        services.AddSingleton<IGroupSettingRepository>(new GroupSettingRepository(context));
        services.AddSingleton<IUserPreferenceRepository>(UserPreferenceRepository.CreateReadOnly(context));
        services.AddSingleton(context);
        services.AddSingleton<IInstanceTenantSettingsReader, InstanceTenantSettingsReader>();
        services.AddSingleton<IMemoryCache>(cache);
        services.AddHierarchicalSettingsReader();
        using var provider = services.BuildIsolatedServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        IHierarchicalSettingsReader reader = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsReader>();

        await Assert.That(await reader.ResolveAsync<string>(scalar.SettingKey, new SettingContext())).IsEqualTo("original");
        var metadata = await reader.ResolveWithMetadataAsync(scalar.SettingKey, new SettingContext());
        await Assert.That(metadata!.Source).IsEqualTo(SettingSource.SystemLocked);
        await Assert.That(metadata.IsLocked).IsTrue();
        await Assert.That(await reader.ResolveAsync<string>(smtp.SettingKey, new SettingContext())).IsEqualTo("first.invalid");

        scalar.Value = "\"updated\"";
        smtp.Value = "\"second.invalid\"";
        await context.SaveChangesAsync();
        await Assert.That(await reader.ResolveAsync<string>(scalar.SettingKey, new SettingContext())).IsEqualTo("original");
        await Assert.That(await reader.ResolveAsync<string>(smtp.SettingKey, new SettingContext())).IsEqualTo("second.invalid");
        await Assert.That(await reader.ResolveAsync<string>(scalar.SettingKey,
            new SettingContext(RequireAuthoritativeReads: true))).IsEqualTo("updated");
        // Authoritative reads must not silently replace the ordinary cached snapshot.
        await Assert.That(await reader.ResolveAsync<string>(scalar.SettingKey, new SettingContext())).IsEqualTo("original");
        reader.InvalidateCache();
        await Assert.That(await reader.ResolveAsync<string>(scalar.SettingKey, new SettingContext())).IsEqualTo("updated");
        var batch = await reader.ResolveBatchAsync(["reader.regression.missing", scalar.SettingKey, smtp.SettingKey],
            new SettingContext());
        await Assert.That(batch.Count).IsEqualTo(2);
        await Assert.That(batch[0].Key).IsEqualTo(scalar.SettingKey);
        await Assert.That(batch[1].Key).IsEqualTo(smtp.SettingKey);
        await Assert.That(await reader.ResolveAsync<string>("reader.regression.missing", new SettingContext())).IsNull();
    }
}
