using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Security;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Explore.Persistence.Database;

internal sealed class ExploreDbContextExecutionLease
{
    private RelationalConnectionIdentity? _connection;
    private DbContextId _contextId;
    internal ExploreDbContext? Context { get; private set; }
    internal Guid? SelectedTenantId { get; private set; }
    internal PolicyMutationPurpose? PolicyPurpose { get; private set; }
    internal HumanAdministratorCommitScope? HumanScope { get; private set; }
    internal HumanAuthenticationLifetime? HumanLifetime { get; private set; }
    private HttpContext? _humanRequest;
    private ClaimsPrincipal? _humanPrincipal;
    private ClaimsIdentity[]? _humanIdentities;

    internal void BindOriginalCredential(HttpContext request)
    {
        if (_humanRequest is not null || HumanLifetime is null || HumanScope is null
            || Context is null || Context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Original credential custody requires one validated human binding.");
        _humanRequest = request;
        _humanPrincipal = request.User;
        _humanIdentities = request.User.Identities.ToArray();
        if (!_humanIdentities.Any(identity => identity.IsAuthenticated))
            throw new UnauthorizedAccessException("The original credential has no validating identity.");
    }

    internal void RequireOriginalCredential(HttpContext? request, HumanAuthenticationLifetime lifetime)
    {
        if (request is null || _humanRequest is null || !ReferenceEquals(request, _humanRequest)
            || !ReferenceEquals(request.User, _humanPrincipal) || !ReferenceEquals(lifetime, HumanLifetime)
            || _humanIdentities is null
            || !request.User.Identities.SequenceEqual(_humanIdentities, ReferenceEqualityComparer.Instance)
            || request.User.GetProviderIdentity()?.AccountKey != lifetime.AccountKey
            || HumanScope?.Subject.AccountKey != lifetime.AccountKey)
            throw new UnauthorizedAccessException("The original selected request, principal or credential changed.");
        RequireOriginalOwner(Context ?? throw new UnauthorizedAccessException("The original owner is missing."));
    }

    internal void BindHuman(HumanCommitSubject subject, HumanAdministratorCommitPurpose purpose,
        PostgresNativeQueueEnrollment? enrollment, HumanAuthenticationLifetime? lifetime = null)
    {
        if (Context is null || HumanScope is not null)
            throw new InvalidOperationException("A human commit scope requires one original database lease.");
        if (lifetime is not null && lifetime.AccountKey != subject.AccountKey)
            throw new UnauthorizedAccessException("The validated credential must belong to the resolved human.");
        HumanScope = new(Context, subject, purpose, enrollment);
        HumanLifetime = lifetime;
        Context.HumanCommitAuditSubject = subject;
        Context.HumanCommitContextLease = Context.ContextId;
    }

    internal void Bind(ExploreDbContext context, Guid? selectedTenantId, PolicyMutationPurpose? policyPurpose = null)
    {
        if (Context is not null)
            throw new InvalidOperationException("An execution scope cannot replace its database lease.");
        context.ValidateTenantLease();
        Context = context;
        SelectedTenantId = selectedTenantId;
        PolicyPurpose = policyPurpose;
        _contextId = context.ContextId;
        _connection = context.Database.IsRelational() ? RelationalConnectionIdentity.Capture(context) : null;
    }

    internal void RequireOriginalOwner(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!ReferenceEquals(Context, context) || context.ContextId != _contextId
            || context.TenantFilterTenantId != SelectedTenantId
            || (_connection is not null && !_connection.Matches(context))
            || (_connection is not null && context.Database.CurrentTransaction?.GetDbTransaction() is { } transaction
                && !_connection.Owns(transaction))
            || (HumanScope is { } human && (human.TenantId != SelectedTenantId
                || context.IsTenantFilterBypassed
                || (human.Purpose == HumanAdministratorCommitPurpose.ConfigurationImport)
                    != context.IsConfigurationInstanceImportOwner)))
            throw new InvalidOperationException("An operation must retain its original context, connection and tenant.");
    }
}
