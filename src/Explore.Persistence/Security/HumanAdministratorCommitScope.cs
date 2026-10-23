using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Closed connection purposes for the existing human administrator ports.</summary>
public enum HumanAdministratorCommitPurpose
{
    RuntimeTenant = 1,
    GlobalInstance = 2,
    EmailInstance = 3,
    EmailTenant = 4,
    ConfigurationImport = 5,
    MixedSettingsInstance = 6,
    MixedSettingsTenant = 7,
    TenantCreation = 8
}

/// <summary>
/// Binds a trusted account to an already selected context before transaction acquisition.
/// The scope neither opens a connection nor starts a business transaction.
/// </summary>
public sealed class HumanAdministratorCommitScope
{
    private readonly ExploreDbContext _context;
    private readonly RelationalConnectionIdentity _connection;
    private readonly DbContextId _contextLease;

    public HumanAdministratorCommitScope(
        ExploreDbContext context, HumanCommitSubject subject, HumanAdministratorCommitPurpose purpose,
        PostgresNativeQueueEnrollment? enrollment = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subject);
        if (!Enum.IsDefined(purpose) || !context.Database.IsRelational()
            || context.Database.CurrentTransaction is not null || context.IsTenantFilterBypassed)
            throw new InvalidOperationException("human_administrator_scope_requires_selected_relational_owner");
        Guid? tenant = context.BindTenantLease();
        if ((purpose is HumanAdministratorCommitPurpose.RuntimeTenant or HumanAdministratorCommitPurpose.EmailTenant
                or HumanAdministratorCommitPurpose.MixedSettingsTenant)
                != tenant.HasValue
            || (purpose == HumanAdministratorCommitPurpose.ConfigurationImport) != context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("human_administrator_scope_purpose_mismatch");
        if (context.Database.IsNpgsql())
        {
            ArgumentNullException.ThrowIfNull(enrollment);
            enrollment.Validate();
        }
        _context = context;
        _connection = RelationalConnectionIdentity.Capture(context);
        _contextLease = context.ContextId;
        Subject = subject;
        Purpose = purpose;
        TenantId = tenant;
        Enrollment = enrollment;
    }

    public HumanCommitSubject Subject { get; }
    public HumanAdministratorCommitPurpose Purpose { get; }
    public Guid? TenantId { get; }
    internal PostgresNativeQueueEnrollment? Enrollment { get; }

    internal void RequireOwner(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!ReferenceEquals(context, _context) || context.ContextId != _contextLease
            || !_connection.Matches(context)
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != TenantId
            || (Purpose == HumanAdministratorCommitPurpose.ConfigurationImport) != context.IsConfigurationInstanceImportOwner
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !_connection.Owns(transaction))
            throw new InvalidOperationException("human_administrator_requires_original_serializable_owner");
    }
}
