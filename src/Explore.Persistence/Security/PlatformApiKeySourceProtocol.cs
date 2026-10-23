using System.Data;
using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

public sealed record PlatformApiKeySourceOptions(IdentityDatabaseTopology IdentityTopology);

public static class PlatformApiKeySourceRegistration
{
    public static IServiceCollection AddPlatformApiKeySources(this IServiceCollection services,
        IdentityDatabaseTopology identityTopology)
    {
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        services.AddSingleton(new PlatformApiKeySourceOptions(identityTopology));
        return services;
    }
}

/// <summary>
/// One producer belonging to the actual dedicated scope. No public source/lease implementation,
/// request actor, context flag or caller-selected transaction can manufacture this custody.
/// </summary>
internal sealed class PlatformApiKeySourceProtocol
{
    private readonly ExploreDbContext context;
    private readonly RelationalConnectionIdentity connection;
    private readonly PlatformApiKeySourceProviderQueries queries;
    private readonly DbContextId contextLease;
    private readonly PlatformApiKeySourceEnrollment? enrollment;
    private readonly PlatformApiKeySourcePurpose purpose;
    private DbTransaction? active;
    private int state;

    internal PlatformApiKeySourceProtocol(ExploreDbContext context, PlatformApiKeySourcePurpose purpose,
        HumanCommitSubject subject, HumanAuthenticationLifetime lifetime, HumanSupportForwardingObservation forwarding,
        PlatformApiKeySourceOptions options, PlatformApiKeySourceEnrollment? enrollment)
    {
        this.context = context;
        this.purpose = purpose;
        Subject = subject;
        Lifetime = lifetime;
        Forwarding = forwarding;
        this.enrollment = enrollment;
        connection = RelationalConnectionIdentity.Capture(context);
        queries = new(context);
        contextLease = context.ContextId;
        if (!Enum.IsDefined(purpose) || !Enum.IsDefined(options.IdentityTopology))
            throw new ArgumentException("A closed platform source purpose and topology are required.");
        if (lifetime.LocalSession is not null && options.IdentityTopology == IdentityDatabaseTopology.External)
            throw new NotSupportedException("platform_key_external_local_identity_source_not_supported");
        queries.RequireSupported();
        if (queries.RequiresEnrollment)
            (enrollment ?? throw new InvalidOperationException("platform_key_source_enrollment_required")).Validate();
        RequireSelection();
        RequireIdle();
    }

    internal HumanCommitSubject Subject { get; }
    internal HumanAuthenticationLifetime Lifetime { get; }
    internal HumanSupportForwardingObservation Forwarding { get; }

    internal async Task<PlatformApiKeySourceFacts> PrepareAsync(CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
            throw new InvalidOperationException("platform_key_source_already_prepared");
        try
        {
            RequireIdle();
            var facts = await ReadAsync(null, token);
            RequireIdle();
            return facts;
        }
        catch { state = 4; throw; }
    }

    internal async Task<PlatformApiKeySourceFacts> AcquireAsync(CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref state, 2, 1) != 1)
            throw new InvalidOperationException("platform_key_source_already_acquired");
        try
        {
            var transaction = RequireTransaction();
            var facts = await ReadAsync(transaction, token);
            if (!ReferenceEquals(transaction, RequireTransaction()))
                throw new UnauthorizedAccessException("platform_key_source_transaction_changed");
            active = transaction;
            state = 3;
            return facts;
        }
        catch { state = 4; throw; }
    }

    internal async Task<PlatformApiKeySourceFacts> ReadOwnedAsync(CancellationToken token)
    {
        try
        {
            if (state != 3 || !ReferenceEquals(active, RequireTransaction()))
                throw new UnauthorizedAccessException("platform_key_source_not_owned");
            var facts = await ReadAsync(active, token);
            if (!ReferenceEquals(active, RequireTransaction()))
                throw new UnauthorizedAccessException("platform_key_source_transaction_changed");
            return facts;
        }
        catch { state = 4; throw; }
    }

    private void RequireSelection()
    {
        context.ValidateTenantLease();
        if (context.ContextId != contextLease || !connection.Matches(context)
            || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner
            || context.HumanCommitContextLease != contextLease || !ReferenceEquals(context.HumanCommitAuditSubject, Subject)
            || !ReferenceEquals(Forwarding.Subject, Subject) || Subject.AccountKey is null
            || Subject.AccountKey != Lifetime.AccountKey
            || (Subject.AccountKey.ProviderKind == AuthenticationProviderKind.Local) != (Lifetime.LocalSession is not null)
            || Lifetime.LocalSession is { } local && (local.LocalSubjectId != Subject.PrincipalId
                || Subject.AccountKey.Value != local.LocalSubjectId.ToString("D")))
            throw new UnauthorizedAccessException("platform_key_original_source_selection_changed");
    }

    private void RequireIdle()
    {
        RequireSelection();
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("platform_key_preparation_requires_idle_owner");
    }

    private DbTransaction RequireTransaction()
    {
        RequireSelection();
        if (System.Transactions.Transaction.Current is not null || connection.State != ConnectionState.Open
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable || !connection.Owns(transaction))
            throw new InvalidOperationException("platform_key_original_serializable_owner_required");
        return transaction;
    }

    private async Task<PlatformApiKeySourceFacts> ReadAsync(DbTransaction? transaction, CancellationToken token)
    {
        RequireSelection();
        PlatformApiKeySourceFacts facts;
        if (!queries.RequiresEnrollment)
            facts = await PlatformApiKeySourceProjection.ReadSqliteAsync(context, transaction, Subject, Lifetime, Forwarding, token);
        else
        {
            string executor = purpose switch
            {
                PlatformApiKeySourcePurpose.Read => PostgresInstanceApiKeyManagementContract.ReadExecutor,
                PlatformApiKeySourcePurpose.Write => PostgresInstanceApiKeyManagementContract.WriteExecutor,
                PlatformApiKeySourcePurpose.Report => PostgresPlatformApiKeyUsageReportContract.ExecutorRole,
                _ => throw new InvalidOperationException("platform_key_source_purpose_invalid")
            };
            var effects = purpose == PlatformApiKeySourcePurpose.Report
                ? new[] { PostgresPlatformApiKeyUsageReportContract.Function(context) }
                : PostgresInstanceApiKeyManagementContract.Functions(context).Where(function => function.Executor == executor).ToArray();
            var admission = new InstanceApiKeyCapabilitySql(context);
            await admission.RequireReadyAsync(executor, effects, token, enrollment);
            var payload = await queries.ReadPostgresAsync(connection, transaction, purpose, Subject, Lifetime, Forwarding, token);
            facts = PlatformApiKeySourceProjection.Read(payload, Subject, Lifetime, Forwarding);
            await admission.RequireReadyAsync(executor, effects, token, enrollment);
        }
        RequireSelection();
        token.ThrowIfCancellationRequested();
        return facts;
    }
}
