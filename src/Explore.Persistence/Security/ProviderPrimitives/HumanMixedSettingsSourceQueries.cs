using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

internal static class HumanMixedSettingsSourceQueries
{
    internal static async Task<(HumanEmailSourceFacts Facts, string Data, string? Native)> ReadAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment, HumanCommitSubject subject,
        HumanAuthenticationLifetime lifetime, HumanSupportForwardingObservation forwarding, Guid? tenant,
        UpdateSettingBatchCommand? original, TypedSettingsOriginal? typedOriginal, string? nativeOriginal,
        bool acquire, Action requireSelection, CancellationToken token)
    {
        if (context.Database.IsSqlite())
        {
            var facts = await HumanEmailSourceSqlite.ReadAsync(context,
                context.Database.CurrentTransaction?.GetDbTransaction(), subject, lifetime, forwarding,
                tenant, route: typedOriginal is not null, token);
            string image = typedOriginal is null
                ? await MixedSettingsDataImage.ReadSqliteAsync(context, original!, tenant, token)
                : await MixedSettingsDataImage.ReadTypedSqliteAsync(context, typedOriginal, token);
            requireSelection();
            return (facts, image, null);
        }
        string json = typedOriginal is null ? await PostgresMixedSettingsContract.ReadHumanAsync(context,
            enrollment ?? throw new UnauthorizedAccessException("Mixed native enrollment is required."),
            original!, subject, lifetime, forwarding, tenant, acquire ? nativeOriginal : null, token)
            : await PostgresMixedSettingsContract.ReadTypedHumanAsync(context,
                enrollment ?? throw new UnauthorizedAccessException("Typed native enrollment is required."),
                typedOriginal, subject, lifetime, forwarding, acquire ? nativeOriginal : null, token);
        using var document = JsonDocument.Parse(json);
        return (HumanEmailSourceProjection.Read(json, subject, tenant, forwarding,
            lifetime.LocalSession is not null, typedOriginal is not null), document.RootElement.GetProperty("Data").GetRawText(), json);
    }
}
