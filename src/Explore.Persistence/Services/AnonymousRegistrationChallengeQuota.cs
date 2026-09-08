
using System.Data;
using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Settings;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Models;
using Explore.Persistence.Schema.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

public sealed class AnonymousRegistrationChallengeQuota(ExploreDbContext context) : IAnonymousRegistrationChallengeQuota
{
    public async Task<bool> TryAcquireAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken)
    {
        // The issuer holds visitor/quota setting leases before this transaction, and the complete
        // issuance runs under ExecuteBootstrapConvergenceAsync. Conflicts retry the whole snapshot.
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Challenge quota requires the issuer's serializable transaction.");

        if (context.TenantContext?.TenantId != tenantId
            || !await context.Events.AsNoTracking().AnyAsync(
                entity => entity.TenantId == tenantId && entity.Id == eventId && !entity.IsDeleted, cancellationToken))
            return false;

        string[] keys = AnonymousRegistrationChallengeSettingDefinitions.All.Select(definition => definition.Key).ToArray();
        var system = await context.SystemSettings.AsNoTracking().Where(row => keys.Contains(row.SettingKey))
            .ToDictionaryAsync(row => row.SettingKey, cancellationToken);
        var tenant = await context.TenantSettingOverrides.AsNoTracking()
            .Where(row => row.TenantId == tenantId && keys.Contains(row.SettingKey))
            .ToDictionaryAsync(row => row.SettingKey, cancellationToken);
        int Limit(SettingDefinition definition)
        {
            string? value = JsonSerializer.Deserialize<string>(HierarchicalSettingMerge.Resolve(definition.Key, system, tenant)!.Value);
            if (value is null || !definition.AllowedValues!.Contains(value))
                throw new InvalidOperationException("The canonical anonymous challenge quota setting is invalid.");
            return int.Parse(value, CultureInfo.InvariantCulture);
        }
        int tenantLimit = Limit(AnonymousRegistrationChallengeSettingDefinitions.TenantPermitsPerMinute);
        int eventLimit = Limit(AnonymousRegistrationChallengeSettingDefinitions.EventPermitsPerMinute);
        DateTime now = await RelationalDatabaseClock.GetUtcNowAsync(context, cancellationToken);
        long minute = new DateTimeOffset(now).ToUnixTimeSeconds() / 60;

        const string savepoint = "anonymous_challenge_quota";
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        var tenantRows = context.Set<AnonymousChallengeTenantQuota>().Where(row => row.TenantId == tenantId);
        if (!await tenantRows.AnyAsync(cancellationToken))
        {
            var row = new AnonymousChallengeTenantQuota { TenantId = tenantId, WindowMinute = minute, Issued = 0 };
            context.Add(row);
            await context.SaveChangesAsync(cancellationToken);
            context.Entry(row).State = EntityState.Detached;
        }
        int tenantCharged = await tenantRows
            .Where(row => row.WindowMinute < minute || row.WindowMinute == minute && row.Issued < tenantLimit)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.Issued, row => row.WindowMinute < minute ? 1 : row.Issued + 1)
                .SetProperty(row => row.WindowMinute, minute), cancellationToken);

        if (tenantCharged == 1)
        {
            var eventRows = context.Set<AnonymousChallengeEventQuota>()
                .Where(row => row.TenantId == tenantId && row.EventId == eventId);
            if (!await eventRows.AnyAsync(cancellationToken))
            {
                var row = new AnonymousChallengeEventQuota { TenantId = tenantId, EventId = eventId, WindowMinute = minute, Issued = 0 };
                context.Add(row);
                await context.SaveChangesAsync(cancellationToken);
                context.Entry(row).State = EntityState.Detached;
            }
            int eventCharged = await eventRows
                .Where(row => row.WindowMinute < minute || row.WindowMinute == minute && row.Issued < eventLimit)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.Issued, row => row.WindowMinute < minute ? 1 : row.Issued + 1)
                    .SetProperty(row => row.WindowMinute, minute), cancellationToken);
            if (eventCharged == 1)
            {
                await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
                return true;
            }
        }

        // A false result is a normal issuer response, so it must not leave a charge in the
        // transaction that the caller will commit. Exceptional failures use the outer UoW rollback.
        await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
        await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
        return false;
    }
}
