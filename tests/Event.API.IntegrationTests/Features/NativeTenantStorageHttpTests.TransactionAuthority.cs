using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeTenantStorageHttpTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("grant")]
    [Arguments("binding")]
    [Arguments("support-setting")]
    public async Task Patch_RechecksOriginalAuthorityAfterIdleSecretAwait(string change)
    {
        await using var factory = await StorageFactory.CreateAsync();
        var data = await SeedAsync(factory);
        await PrepareSecretInferenceAsync(factory);
        using var admin = Client(factory, data.AdminId);
        var before = await SettingsAsync(admin);
        var persistedBefore = await TenantSettingsAsync(factory, PlatformDefaults.DefaultTenantId);
        string bucketBefore = (await S3Async(factory)).BucketName;
        var controls = factory.AuthorityControls;
        controls.BlockNextSecret = true;
        controls.RecordTransactions = true;
        var pending = admin.PatchAsJsonAsync(Root, AuthorityPatch());
        try
        {
            await controls.SecretEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(controls.Transactions).IsEmpty();
            if (change != "unchanged")
            {
                using var scope = factory.Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
                await ChangeAuthorityAsync(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), data.AdminId, change);
            }
        }
        finally { controls.ReleaseSecret.TrySetResult(); }
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        if (change == "unchanged")
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var after = await SettingsAsync(admin);
            await Assert.That(after.S3BucketName).IsEqualTo("authority-attempt");
            await Assert.That(after.MaxUploadBytes).IsEqualTo(4096L);
            await Assert.That((await S3Async(factory)).BucketName).IsEqualTo("authority-attempt");
            await Assert.That(controls.Transactions).Count().IsEqualTo(1);
        }
        else
        {
            await ProblemAsync(response, HttpStatusCode.Unauthorized);
            await Assert.That(await TenantSettingsAsync(factory, PlatformDefaults.DefaultTenantId)).IsEquivalentTo(persistedBefore);
            await Assert.That((await S3Async(factory)).BucketName).IsEqualTo(bucketBefore);
            using var reader = Client(factory, data.InstanceAdminId);
            await Assert.That((await SettingsAsync(reader)).MaxUploadBytes).IsEqualTo(before.MaxUploadBytes);
            await Assert.That(controls.Transactions).IsEmpty();
        }
        await Assert.That(factory.Boundary.Operations).IsEmpty();
    }

    [Test]
    [Arguments("grant")]
    [Arguments("binding")]
    [Arguments("support-setting")]
    public async Task Patch_RollsBackLeavesWhenOriginalTransactionChangesAuthorityBeforeAdmission(string change)
    {
        await using var factory = await StorageFactory.CreateAsync();
        var data = await SeedAsync(factory);
        await EnsureSupportSettingAsync(factory);
        using var admin = Client(factory, data.AdminId);
        var before = await SettingsAsync(admin);
        var persistedBefore = await TenantSettingsAsync(factory, PlatformDefaults.DefaultTenantId);
        string bucketBefore = (await S3Async(factory)).BucketName;
        factory.AuthorityControls.RecordTransactions = true;
        factory.AuthorityControls.AfterStorageSaved = async (database, token) =>
            await ChangeAuthorityAsync(database, data.AdminId, change, token);

        using var response = await admin.PatchAsJsonAsync(Root, AuthorityPatch());

        await Assert.That(factory.AuthorityControls.SavedTransaction).IsNotNull();
        await Assert.That(factory.AuthorityControls.Transactions).Count().IsEqualTo(1);
        await Assert.That(factory.AuthorityControls.SavedTransaction)
            .IsSameReferenceAs(factory.AuthorityControls.Transactions.Single());
        await ProblemAsync(response, HttpStatusCode.Unauthorized);
        await Assert.That(await TenantSettingsAsync(factory, PlatformDefaults.DefaultTenantId)).IsEquivalentTo(persistedBefore);
        await Assert.That((await SettingsAsync(admin)).MaxUploadBytes).IsEqualTo(before.MaxUploadBytes);
        await Assert.That((await S3Async(factory)).BucketName).IsEqualTo(bucketBefore);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.TenantUserRoleGrants.AnyAsync(row => row.TenantUser.UserId == data.AdminId && row.RevokedAt == null)).IsTrue();
        await Assert.That(await db.UserExternalLogins.AnyAsync(row => row.UserId == data.AdminId)).IsTrue();
        await Assert.That(await db.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.SupportAccess.AllowWriteMode)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("false");
        await Assert.That(factory.Boundary.Operations).IsEmpty();
    }

    [Test]
    public async Task Patch_RejectsFailedObservationReplayAndAcquiresFreshOwnerForNewObservation()
    {
        await using var factory = await StorageFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.AdminId);
        var before = await SettingsAsync(admin);
        var persistedBefore = await TenantSettingsAsync(factory, PlatformDefaults.DefaultTenantId);
        string bucketBefore = (await S3Async(factory)).BucketName;
        using var scope = factory.Services.CreateScope();
        await SetPrincipalAsync(scope, factory, data.AdminId, PlatformDefaults.DefaultTenantId, HttpMethods.Patch);
        var service = scope.ServiceProvider.GetRequiredService<ITenantStorageSettingService>();
        var command = new PatchTenantStorageSettingsCommand { UserId = data.AdminId, Settings = AuthorityPatch() };
        await using var first = await service.ObserveConfigurationAsync(command);
        factory.AuthorityControls.RecordTransactions = true;
        factory.AuthorityControls.AfterStorageSaved = (_, _) => throw new StorageAuthorityAttemptFault();

        await Assert.ThrowsAsync<StorageAuthorityAttemptFault>(() => service.ApplyPatchAsync(first));

        var originalTransaction = factory.AuthorityControls.SavedTransaction;
        await Assert.That(originalTransaction).IsNotNull();
        await Assert.That(await TenantSettingsAsync(factory, PlatformDefaults.DefaultTenantId)).IsEquivalentTo(persistedBefore);
        await Assert.That((await SettingsAsync(admin)).MaxUploadBytes).IsEqualTo(before.MaxUploadBytes);
        await Assert.That((await S3Async(factory)).BucketName).IsEqualTo(bucketBefore);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyPatchAsync(first));
        await Assert.That(factory.AuthorityControls.Transactions).Count().IsEqualTo(1);
        factory.AuthorityControls.AfterStorageSaved = null;
        await using var fresh = await service.ObserveConfigurationAsync(command);
        await service.ApplyPatchAsync(fresh);

        await Assert.That(factory.AuthorityControls.Transactions).Count().IsEqualTo(2);
        await Assert.That(ReferenceEquals(factory.AuthorityControls.Transactions.Last(), originalTransaction)).IsFalse();
        await Assert.That((await SettingsAsync(admin)).S3BucketName).IsEqualTo("authority-attempt");
        await Assert.That((await SettingsAsync(admin)).MaxUploadBytes).IsEqualTo(4096L);
        await Assert.That((await S3Async(factory)).BucketName).IsEqualTo("authority-attempt");
    }

    private static PatchTenantStorageSettingsDto AuthorityPatch() => new()
    {
        Policy = new() { MaxUploadBytes = OptionalUpdate<long>.Set(4096) },
        S3 = new() { BucketName = OptionalUpdate<string>.Set("authority-attempt") }
    };

    private static async Task PrepareSecretInferenceAsync(StorageFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await db.TenantSettingOverrides.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.Provider).ExecuteDeleteAsync();
        await using var global = await scope.ServiceProvider.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync();
        await global.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.Provider).ExecuteDeleteAsync();
        foreach (string key in new[] { SecretDefinitionRegistry.Keys.Storage.AccessKeyId, SecretDefinitionRegistry.Keys.Storage.SecretAccessKey })
        {
            if (!await global.SecretBindings.AnyAsync(row => row.SettingKey == key && row.SettingScopeId == 2
                && row.ScopeId == PlatformDefaults.DefaultTenantId))
                global.SecretBindings.Add(SecretBinding.CreateEnvironmentVariable(key, SecretScope.Tenant,
                    PlatformDefaults.DefaultTenantId, key == SecretDefinitionRegistry.Keys.Storage.AccessKeyId
                        ? "STORAGE_S3_ACCESS_KEY_ID" : "STORAGE_S3_SECRET_ACCESS_KEY"));
        }
        await global.SaveChangesAsync();
        await EnsureSupportSettingAsync(factory);
    }

    private static async Task EnsureSupportSettingAsync(StorageFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await using var global = await scope.ServiceProvider.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync();
        var row = await global.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == GovernanceSettingKeys.SupportAccess.AllowWriteMode);
        if (row is null)
            global.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.SupportAccess.AllowWriteMode,
                Value = "false", ValueType = SettingValueType.Boolean, CreatedAt = DateTime.UtcNow
            });
        else row.Value = "false";
        await global.SaveChangesAsync();
    }

    private static async Task ChangeAuthorityAsync(ExploreDbContext database, Guid actor, string change, CancellationToken token = default)
    {
        int affected = change switch
        {
            "grant" => await database.TenantUserRoleGrants.Where(row => row.TenantUser.UserId == actor && row.RevokedAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, DateTime.UtcNow), token),
            "binding" => await database.UserExternalLogins.Where(row => row.UserId == actor).ExecuteDeleteAsync(token),
            "support-setting" => await database.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.SupportAccess.AllowWriteMode)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "true"), token),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        await Assert.That(affected).IsEqualTo(1);
    }

    private sealed class TransactionAuthorityControls
    {
        private int secretBlocked;
        private int storageSaved;
        internal bool BlockNextSecret { get; set; }
        internal bool RecordTransactions { get; set; }
        internal TaskCompletionSource SecretEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSecret { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<DbTransaction> Transactions { get; } = [];
        internal DbTransaction? SavedTransaction { get; private set; }
        internal Func<ExploreDbContext, CancellationToken, Task>? AfterStorageSaved { get; set; }

        internal async Task BeforeSecretReadAsync(SecretBinding binding, CancellationToken token)
        {
            if (!BlockNextSecret || binding.SettingKey != SecretDefinitionRegistry.Keys.Storage.AccessKeyId
                || Interlocked.Exchange(ref secretBlocked, 1) != 0) return;
            SecretEntered.TrySetResult();
            await ReleaseSecret.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        }

        internal async Task AfterSavedAsync(SaveChangesCompletedEventData eventData, CancellationToken token)
        {
            if (AfterStorageSaved is null || eventData.Context is not ExploreDbContext database
                || database.TenantFilterTenantId != PlatformDefaults.DefaultTenantId || database.Database.CurrentTransaction is null
                || Volatile.Read(ref storageSaved) != 0) return;
            var leaves = await database.TenantSettingOverrides.AsNoTracking().Where(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && (row.SettingKey == GovernanceSettingKeys.Storage.BucketName
                    || row.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes))
                .ToDictionaryAsync(row => row.SettingKey, row => row.Value, token);
            if (leaves.GetValueOrDefault(GovernanceSettingKeys.Storage.BucketName) != "\"authority-attempt\""
                || leaves.GetValueOrDefault(GovernanceSettingKeys.Storage.DefaultMaxUploadBytes) != "4096"
                || Interlocked.Exchange(ref storageSaved, 1) != 0) return;
            SavedTransaction = database.Database.CurrentTransaction.GetDbTransaction();
            await AfterStorageSaved(database, token);
        }
    }

    private sealed partial class CommitFailure : ISaveChangesInterceptor
    {
        internal TransactionAuthorityControls? AuthorityControls { get; set; }

        public async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (AuthorityControls is not null) await AuthorityControls.AfterSavedAsync(eventData, cancellationToken);
            return result;
        }

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (AuthorityControls?.RecordTransactions == true && eventData.Context is ExploreDbContext database
                && database.TenantFilterTenantId == PlatformDefaults.DefaultTenantId)
                AuthorityControls.Transactions.Add(result);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StorageAuthorityAttemptFault : Exception;
}
