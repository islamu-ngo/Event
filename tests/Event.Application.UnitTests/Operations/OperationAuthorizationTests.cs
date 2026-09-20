using Explore.Application;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class OperationAuthorizationTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    public async Task DeniedAndUnavailableNeverReachBusinessState(int shape, bool unavailable)
    {
        var policy = new Policy(AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos,
            unavailable ? AuthorizationDecisionReasonCodes.ProviderUnavailable : AuthorizationDecisionReasonCodes.Denied));
        var services = CreateServices(policy);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var state = scope.ServiceProvider.GetRequiredService<ProtectedHandler>();
        if (unavailable)
            await Assert.That(() => Invoke(scope.ServiceProvider, shape)).Throws<AuthorizationProviderUnavailableException>();
        else
            await Assert.That(() => Invoke(scope.ServiceProvider, shape)).Throws<AuthorizationException>();
        await Assert.That(state.Executions).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task PersistedFactsReplaceForgedRequestAndEnricherFacts(int shape)
    {
        var policy = new Policy(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local));
        var services = CreateServices(policy);
        var member = Member();
        services.AddSingleton<IOrganizationMemberRepository>(new MemberStore(member));
        services.AddSingleton<ITenantContext>(new TenantContext(member.TenantId));
        services.AddScoped<AuthorizationResourceContextResolver>();
        services.AddScoped<IAuthorizationContextEnricher<ProtectedWrite>, Enricher<ProtectedWrite>>();
        services.AddScoped<IAuthorizationContextEnricher<ProtectedResult>, Enricher<ProtectedResult>>();
        services.AddScoped<IAuthorizationContextEnricher<ProtectedRead>, Enricher<ProtectedRead>>();
        using var cancellation = new CancellationTokenSource();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await Invoke(scope.ServiceProvider, shape, cancellation.Token);
        await Assert.That(policy.Observed!.ResourceId).IsEqualTo(MemberId.ToString("D"));
        await Assert.That(policy.Observed.Facts).IsEqualTo(new OrganizationMemberAuthorizationFacts(
            member.TenantId, member.OrganizationTenant.OrganizationId, member.Id, member.UserId));
        await Assert.That(policy.Token).IsEqualTo(cancellation.Token);
        await Assert.That(scope.ServiceProvider.GetRequiredService<ProtectedHandler>().Token).IsEqualTo(cancellation.Token);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CrossTenantPersistedStateCannotBeReplacedByEnrichedAuthority(int shape)
    {
        var policy = new Policy(AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local));
        var services = CreateServices(policy);
        services.AddSingleton<IOrganizationMemberRepository>(new MemberStore(Member()));
        services.AddSingleton<ITenantContext>(new TenantContext(Guid.CreateVersion7()));
        services.AddScoped<AuthorizationResourceContextResolver>();
        services.AddScoped<IAuthorizationContextEnricher<ProtectedWrite>, Enricher<ProtectedWrite>>();
        services.AddScoped<IAuthorizationContextEnricher<ProtectedResult>, Enricher<ProtectedResult>>();
        services.AddScoped<IAuthorizationContextEnricher<ProtectedRead>, Enricher<ProtectedRead>>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await Assert.That(() => Invoke(scope.ServiceProvider, shape)).Throws<AuthorizationException>();
        await Assert.That(policy.Observed!.Facts).IsNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ProtectedHandler>().Executions).IsEqualTo(0);
    }

    [Test]
    public async Task ReviewedUnannotatedOperationsRemainHandlerOwnedWithoutInventedPdpCheck()
    {
        var policy = new Policy(AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local));
        var services = OperationCompositionTests.Services(typeof(OperationCompositionTests.Write), typeof(OperationCompositionTests.FirstHandler));
        services.AddSingleton<IAuthorizationProvider>(policy);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICommandHandler<OperationCompositionTests.Write>>().ExecuteAsync(new(), default);
        await Assert.That(policy.Observed).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task TenantScopesKeepPersistedAuthorityAndBusinessStateSeparate(int shape)
    {
        var services = CreateServices(new Policy(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local), requirePersistedFacts: true));
        var member = Member();
        services.AddSingleton<IOrganizationMemberRepository>(new MemberStore(member));
        services.AddScoped<TenantScope>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantScope>());
        services.AddScoped<AuthorizationResourceContextResolver>();
        services.AddScoped<IAuthorizationContextEnricher<ProtectedWrite>, Enricher<ProtectedWrite>>();
        services.AddScoped<IAuthorizationContextEnricher<ProtectedResult>, Enricher<ProtectedResult>>();
        services.AddScoped<IAuthorizationContextEnricher<ProtectedRead>, Enricher<ProtectedRead>>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var owner = provider.CreateAsyncScope();
        await using var foreign = provider.CreateAsyncScope();
        owner.ServiceProvider.GetRequiredService<TenantScope>().TenantId = member.TenantId;
        foreign.ServiceProvider.GetRequiredService<TenantScope>().TenantId = Guid.CreateVersion7();
        await Invoke(owner.ServiceProvider, shape);
        await Assert.That(() => Invoke(foreign.ServiceProvider, shape)).Throws<AuthorizationException>();
        await Assert.That(owner.ServiceProvider.GetRequiredService<ProtectedHandler>().Executions).IsEqualTo(1);
        await Assert.That(foreign.ServiceProvider.GetRequiredService<ProtectedHandler>().Executions).IsEqualTo(0);
    }

    internal static IServiceCollection CreateServices(Policy policy)
    {
        var services = OperationCompositionTests.Services(typeof(ProtectedWrite), typeof(ProtectedResult), typeof(ProtectedRead), typeof(ProtectedHandler));
        services.AddSingleton<IAuthorizationProvider>(policy);
        return services;
    }

    internal static Task Invoke(IServiceProvider provider, int shape, CancellationToken token = default) => shape switch
    {
        0 => provider.GetRequiredService<ICommandHandler<ProtectedWrite>>().ExecuteAsync(new(), token),
        1 => provider.GetRequiredService<ICommandHandler<ProtectedResult, int>>().ExecuteAsync(new(), token),
        2 => provider.GetRequiredService<IQueryHandler<ProtectedRead, int>>().QueryAsync(new(), token),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static readonly Guid MemberId = Guid.Parse("01900000-0000-7000-8000-000000000001");

    [AuthorizeResource(ResourceKinds.OrganizationMember, AuthorizationActions.Update)]
    public sealed record ProtectedWrite : ICommand, ISecureRequest
    {
        public string ResourceId => "forged-request-id";
        public IAuthorizationFacts AuthorizationFacts => new TenantScopedAuthorizationFacts(Guid.Empty);
    }
    [AuthorizeResource(ResourceKinds.OrganizationMember, AuthorizationActions.Update)]
    public sealed record ProtectedResult : ICommand<int>, ISecureRequest
    {
        public string ResourceId => "forged-request-id";
        public IAuthorizationFacts AuthorizationFacts => new TenantScopedAuthorizationFacts(Guid.Empty);
    }
    [AuthorizeResource(ResourceKinds.OrganizationMember, AuthorizationActions.View)]
    public sealed record ProtectedRead : IQuery<int>, ISecureRequest
    {
        public string ResourceId => "forged-request-id";
        public IAuthorizationFacts AuthorizationFacts => new TenantScopedAuthorizationFacts(Guid.Empty);
    }
    public sealed class ProtectedHandler : ICommandHandler<ProtectedWrite>, ICommandHandler<ProtectedResult, int>, IQueryHandler<ProtectedRead, int>
    {
        public int Executions { get; private set; }
        public CancellationToken Token { get; private set; }
        private Task<int> Apply(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Token = cancellationToken;
            return Task.FromResult(++Executions);
        }
        public Task ExecuteAsync(ProtectedWrite command, CancellationToken cancellationToken) => Apply(cancellationToken);
        public Task<int> ExecuteAsync(ProtectedResult command, CancellationToken cancellationToken) => Apply(cancellationToken);
        public Task<int> QueryAsync(ProtectedRead query, CancellationToken cancellationToken) => Apply(cancellationToken);
    }
    public sealed class Enricher<T> : IAuthorizationContextEnricher<T> where T : notnull
    {
        public Task<AuthorizationContext> ResolveAsync(T request, CancellationToken cancellationToken) =>
            Task.FromResult(new AuthorizationContext(MemberId.ToString("D"), new TenantScopedAuthorizationFacts(Guid.Empty)));
    }
    public sealed class Policy(AuthorizationDecision decision, bool requirePersistedFacts = false) : IAuthorizationProvider
    {
        public AuthorizationRequest? Observed { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Observed = request;
            Token = cancellationToken;
            return Task.FromResult(requirePersistedFacts && request.Facts is not OrganizationMemberAuthorizationFacts
                ? AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local) : decision);
        }
        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
    public sealed class TenantScope : ITenantContext
    {
        public Guid TenantId { get; set; }
    }
    private sealed record TenantContext(Guid TenantId) : ITenantContext;
    private static OrganizationMember Member() => new()
    {
        Id = MemberId,
        TenantId = Guid.Parse("01900000-0000-7000-8000-000000000002"),
        UserId = Guid.Parse("01900000-0000-7000-8000-000000000003"),
        OrganizationTenant = new OrganizationTenant
        {
            OrganizationId = Guid.Parse("01900000-0000-7000-8000-000000000004"),
            Tenant = null!,
            Organization = null!,
            ApprovalStatus = null!
        },
        User = null!,
        Role = null!,
        Tenant = null!
    };
    private sealed class MemberStore(OrganizationMember member) : IOrganizationMemberRepository
    {
        public Task<OrganizationMember?> GetOrganizationMemberWithDetails(Guid id) => Task.FromResult(id == member.Id ? member : null);
        public Task<OrganizationMember?> GetById(Guid id) => Task.FromResult(id == member.Id ? member : null);
        public Task<IReadOnlyList<OrganizationMember>> GetAll() => Task.FromResult<IReadOnlyList<OrganizationMember>>([member]);
        public Task<(IReadOnlyList<OrganizationMember> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => throw new NotSupportedException();
        public Task<bool> Exists(Guid id) => Task.FromResult(id == member.Id);
        public Task<OrganizationMember> Create(OrganizationMember entity) => throw new NotSupportedException();
        public Task Update(OrganizationMember entity) => throw new NotSupportedException();
        public Task Delete(OrganizationMember entity) => throw new NotSupportedException();
        public Task<List<User>> GetUsersByOrganization(Guid organizationId) => throw new NotSupportedException();
        public Task<List<Organization>> GetOrganizationsByUser(Guid userId) => throw new NotSupportedException();
        public Task<bool> Exists(Guid organizationId, Guid userId) => throw new NotSupportedException();
        public Task<List<OrganizationMember>> GetOrganizationMembersWithDetails() => throw new NotSupportedException();
        public Task<List<OrganizationMember>> GetMembersByOrganizationId(Guid organizationId) => throw new NotSupportedException();
        public Task<List<OrganizationMember>> GetInvitesByEmail(string email) => throw new NotSupportedException();
        public Task<OrganizationMember?> GetByOrganizationAndUser(Guid organizationId, Guid userId) => throw new NotSupportedException();
        public Task<bool> HasPermissionInOrganization(Guid organizationId, Guid userId, string permissionMasterCode) => throw new NotSupportedException();
        public Task<List<Guid>> GetOrganizationIdsWhereUserHasPermission(Guid userId, string permissionMasterCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<OrganizationMember>> GetMembershipsByUser(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
