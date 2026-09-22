
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Infrastructure.Mail;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Explore.Application.Configuration;

namespace Explore.Infrastructure.Authentication;

public sealed class LocalIdentityLifecycleDeliveryProcessor(
    ILocalIdentityLifecycleStore lifecycle,
    ILocalIdentityLifecycleDeliveryStore deliveries,
    ISettingMutationLock mutationLock,
    IHierarchicalSettingsResolver settings,
    EmailDeliveryCapabilityResolver capability,
    ILocalIdentityLifecycleSmtpTransport transport,
    IConfiguration configuration,
    ISystemSettingRepository systemSettings,
    IOptions<EmailDispatchProcessorSettings> processorOptions)
{
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        var origin = await PublicAddressResolver.ResolveAsync(configuration, systemSettings, cancellationToken);
        if (origin is not { Scheme: "https" }) return;
        var callbackUri = new Uri(origin, "auth/local-account-recovery");
        var pending = await deliveries.ReadPendingAsync(32, cancellationToken);
        foreach (var pointer in pending)
        {
            var admitted = await mutationLock.ExecuteOrderedGroupsAsync<(Guid AttemptId, SmtpConfiguration Transport)?>(
                [EmailDeliverySettingKeys.All], async token =>
            {
                // Acquire before native transactions; release before network I/O. Already admitted work may finish after disable.
                settings.InvalidateCache();
                var resolved = await capability.ResolveTransportAsync(null, token);
                if (resolved.Configuration is not { } snapshot) return null;
                if (!await deliveries.TryReserveGlobalSmtpAsync(processorOptions.Value.GlobalSmtpRateLimitPerMinute, token)) return null;
                Guid? attemptId = await deliveries.TryAdmitAsync(pointer, token);
                return attemptId is { } id ? (id, snapshot) : null;
            }, cancellationToken);
            if (admitted is not { } admission) continue;
            LocalIdentityLifecycleTransport? handoff;
            try { handoff = await lifecycle.IssueTransportTokenAsync(pointer, cancellationToken); }
            catch
            {
                // This process knows SMTP was never invoked. Preserve that evidence even on request cancellation.
                using var settlement = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await deliveries.CompleteAsync(pointer, admission.AttemptId, SmtpDeliveryOutcome.TransientFailure, settlement.Token);
                throw;
            }
            if (handoff is null)
            {
                await deliveries.CompleteAsync(pointer, admission.AttemptId, SmtpDeliveryOutcome.ConfigurationFailure, cancellationToken);
                continue;
            }
            // Transport exceptions/crashes after invocation retain durable Unknown and cannot be automatically replayed.
            var result = await transport.SendAsync(handoff, admission.AttemptId, callbackUri, admission.Transport, cancellationToken);
            await deliveries.CompleteAsync(pointer, admission.AttemptId, result.Outcome, cancellationToken);
        }
    }

}
