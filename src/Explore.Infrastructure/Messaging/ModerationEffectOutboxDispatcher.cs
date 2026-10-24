using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Configuration;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Messaging;

/// <summary>
/// Drains originally inert work and executes qualified original resource-quarantine causes
/// for the explicit maintenance tenant. Partial substantive recipes are never acknowledged.
/// </summary>
public sealed class ModerationEffectOutboxDispatcher(
    IServiceScopeFactory scopes, IOptionsMonitor<ModerationReconciliationOptions> options) : IModerationEffectDispatcher
{
    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.EventType != ModerationEffectOutboxMessageFactory.EventType
            || message.AggregateType != ModerationEffectOutboxMessageFactory.AggregateType
            || message.AggregateId == Guid.Empty || message.AggregateId.Version != 7
            || message.AggregateId.Variant is < 8 or > 11
            || message.Id != message.AggregateId || message.Payload is not null)
            throw new InvalidOperationException("The moderation effect handoff is not a canonical pointer.");
        var settings = options.CurrentValue;
        if (!settings.Enabled || settings.TenantId == Guid.Empty)
            throw new InvalidOperationException("Moderation maintenance requires an enabled explicit tenant.");

        await using var execution = scopes.CreateAsyncScope();
        var services = execution.ServiceProvider;
        var tenant = services.GetRequiredService<ITenantContextAccessor>();
        var machine = services.GetRequiredService<IMachinePrincipalExecutionAccessor>();
        tenant.SetTenant(settings.TenantId);
        try
        {
            var credential = await services.GetRequiredService<ModerationReconciliationCredentialProvider>()
                .AuthenticateAsync(settings.TenantId, cancellationToken);
            if (credential is null)
                throw new InvalidOperationException("Moderation maintenance credential is unavailable.");
            machine.SetPrincipal(credential);
            var effects = services.GetRequiredService<IModerationEffectLeaseRepository>();
            var inert = await effects.ReadWithoutSubstantiveWorkAsync(message.AggregateId, cancellationToken);
            if (inert.Outcome == ModerationEffectLeaseOutcome.Completed) return;
            bool originallyInert = inert.Outcome == ModerationEffectLeaseOutcome.Known;
            var original = originallyInert ? inert : await effects.ReadAsync(message.AggregateId, cancellationToken);
            if (!originallyInert && original.Outcome == ModerationEffectLeaseOutcome.Completed)
            {
                var verified = await effects.ReadOriginalCompletionAsync(message.AggregateId, cancellationToken);
                if (verified.Outcome == ModerationEffectLeaseOutcome.Completed) return;
                throw new InvalidOperationException("The original moderation completion receipt cannot be independently verified.");
            }
            if (original is not { Outcome: ModerationEffectLeaseOutcome.Known, Effect: { } pending })
                throw new InvalidOperationException("The original moderation effect requires its qualified native consequence owner.");
            if (!originallyInert && (pending.ScopeKind != (int)ModerationScopeKind.Resource
                || pending.SubjectKind != (int)ModerationSubjectKind.Content
                || pending.OperationKind is not ((int)ModerationOperationKind.Activate)
                    and not ((int)ModerationOperationKind.Reconcile) and not ((int)ModerationOperationKind.Supersede)))
                throw new InvalidOperationException("The original moderation effect has no registered substantive owner.");
            var claim = await effects.ClaimAsync(pending.Id, pending.Revision, cancellationToken);
            if (claim.Outcome == ModerationEffectLeaseOutcome.Completed)
            {
                var completed = await effects.ReadWithoutSubstantiveWorkAsync(pending.Id, cancellationToken);
                if (completed.Outcome == ModerationEffectLeaseOutcome.Completed) return;
                var originalCompletion = await effects.ReadOriginalCompletionAsync(pending.Id, cancellationToken);
                if (originalCompletion.Outcome == ModerationEffectLeaseOutcome.Completed) return;
                throw new InvalidOperationException("The completed moderation effect cannot be independently verified.");
            }
            if (claim is not { Outcome: ModerationEffectLeaseOutcome.Known, Effect: { LeaseId: { } leaseId } owned })
                throw new InvalidOperationException("Moderation effect ownership is not currently available.");
            try
            {
                if (!originallyInert)
                {
                    var accounting = await services.GetRequiredService<IModerationResourceQuarantineRepository>()
                        .ExecuteOriginalAsync(owned.Id, leaseId, owned.Revision, cancellationToken);
                    if (accounting.Outcome != ModerationOriginalQuarantineOutcome.Accounted)
                        throw new InvalidOperationException("The original moderation cause set remains incomplete.");
                    var completion = await services.GetRequiredService<IModerationOriginalEffectCompletionRepository>()
                        .CompleteAsync(owned.Id, leaseId, owned.Revision, cancellationToken);
                    if (completion.Outcome != ModerationOriginalEffectCompletionOutcome.Completed)
                        throw new InvalidOperationException(
                            $"The original moderation recipe remains incomplete ({completion.PendingReason}).");
                    return;
                }
                var result = await effects.CompleteWithoutSubstantiveWorkAsync(
                    owned.Id, leaseId, owned.Revision, cancellationToken);
                if (result.Outcome != ModerationEffectLeaseOutcome.Completed)
                    throw new InvalidOperationException("The moderation effect remains incomplete.");
            }
            finally
            {
                await effects.ReleaseAsync(owned.Id, leaseId, owned.Revision, cancellationToken);
            }
        }
        finally
        {
            machine.Clear();
            tenant.Clear();
        }
    }
}
