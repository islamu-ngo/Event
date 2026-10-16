using System.Data;
using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    internal bool HumanCommitAttemptOwnsTransaction { get; set; }
    // Execution mode only, never action authority. ContextId includes the EF pool lease.
    internal DbContextId? HumanCommitContextLease { get; set; }
    internal bool HasHumanCommitContextLease => HumanCommitContextLease == ContextId;
    internal HumanCommitSubject? HumanCommitAuditSubject { get; set; }
    private readonly List<(DbTransaction Transaction, DbContextId ContextLease, Guid? Tenant,
        Func<CancellationToken, Task> ValidateSources, Action ValidateTime)> _humanCommitAdmissions = [];

    internal void RegisterHumanCommitAdmission(HumanCommitSubject subject,
        HumanAuthenticationLifetime lifetime, TimeProvider clock,
        Func<CancellationToken, Task> validateSources, Action<DateTimeOffset> validateAssignments)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(validateAssignments);
        if (subject.AccountKey != lifetime.AccountKey)
            throw new UnauthorizedAccessException("The authenticated lifetime belongs to another account.");
        RegisterHumanCommitAdmission(validateSources, () =>
        {
            DateTimeOffset now = clock.GetUtcNow();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original human credential lifetime has ended.");
            validateAssignments(now);
        });
    }

    internal void RegisterHumanCommitAdmission(
        Func<CancellationToken, Task> validateSources, Action validateTime)
    {
        ArgumentNullException.ThrowIfNull(validateSources);
        ArgumentNullException.ThrowIfNull(validateTime);
        ValidateTenantLease();
        if (!HumanCommitAttemptOwnsTransaction || IsTenantFilterBypassed
            || Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !RelationalConnectionIdentity.Capture(this).Owns(transaction))
            throw new InvalidOperationException("Human final admission requires its original Serializable owner.");
        _humanCommitAdmissions.Add((transaction, ContextId, TenantFilterTenantId, validateSources, validateTime));
    }

    internal async Task ValidateHumanCommitAdmissionsAsync(CancellationToken cancellationToken)
    {
        foreach (var admission in _humanCommitAdmissions)
        {
            RequireOriginalAdmission(admission.Transaction, admission.ContextLease, admission.Tenant);
            await admission.ValidateSources(cancellationToken);
        }
        // All blocking source admission completes before any authentic-lifetime or
        // timed-assignment sampling. The owner commits immediately after this method.
        foreach (var admission in _humanCommitAdmissions)
        {
            RequireOriginalAdmission(admission.Transaction, admission.ContextLease, admission.Tenant);
            admission.ValidateTime();
        }
    }

    internal void RequireHumanCommitAdmission()
    {
        if (!HasHumanCommitContextLease || !HumanCommitAttemptOwnsTransaction
            || _humanCommitAdmissions.Count == 0)
            throw new UnauthorizedAccessException("A held original human admission is required.");
        foreach (var admission in _humanCommitAdmissions)
            RequireOriginalAdmission(admission.Transaction, admission.ContextLease, admission.Tenant);
    }

    private void RequireOriginalAdmission(DbTransaction transaction, DbContextId contextLease, Guid? tenant)
    {
        ValidateTenantLease();
        if (ContextId != contextLease || TenantFilterTenantId != tenant || IsTenantFilterBypassed
            || !ReferenceEquals(Database.CurrentTransaction?.GetDbTransaction(), transaction)
            || !RelationalConnectionIdentity.Capture(this).Owns(transaction))
            throw new InvalidOperationException("Human final admission cannot move to another transaction.");
    }

    internal void ResetHumanCommitAdmissions()
    {
        _humanCommitAdmissions.Clear();
        HumanCommitAttemptOwnsTransaction = false;
    }
}
