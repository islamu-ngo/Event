using System.Data;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests;

/// <summary>
/// Exercises persisted overlay facts, not production publication readiness or consumer conformance.
/// Each test owns a complete native EF model and an isolated SQLite database.
/// </summary>
public sealed class EventResourceModerationSnapshotReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Installation_without_adoption_preserves_native_evaluation()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();

        var facts = await database.ReadAsync(database.Request());

        await Assert.That(facts).IsEqualTo(EventResourceModerationFacts.NotAdopted);
        await Assert.That(facts.PermitsNativeEvaluation).IsTrue();
    }

    [Test]
    public async Task Inactive_adoption_still_requires_current_canonical_provenance()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.StorePolicyAsync(database.TenantScope, active: false);

        var facts = await database.ReadAsync(database.Request());

        await AssertUnavailable(facts);
    }

    [Test]
    public async Task Inactive_adoption_does_not_create_a_restrictive_effect_for_a_qualified_subject()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        await database.StorePolicyAsync(database.TenantScope, active: false);
        var facts = await database.ReadAsync(database.Request());
        await Assert.That(facts.IsAdopted).IsTrue();
        await Assert.That(facts.Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts.PermitsNativeEvaluation).IsTrue();
    }

    [Test]
    public async Task Adopted_read_without_canonical_binding_is_unavailable()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.StorePolicyAsync(database.TenantScope);

        await AssertUnavailable(await database.ReadAsync(database.Request()));
    }

    [Test]
    public async Task Adopted_read_without_qualification_epoch_is_unavailable()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        await database.StorePolicyAsync(database.TenantScope);
        await using (var writer = database.Context())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await writer.ModerationQualificationEpochs.Where(row => row.HostBindingId == database.Host.Id)
                .ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }

        await AssertUnavailable(await database.ReadAsync(database.Request()));
    }

    [Test]
    public async Task Current_binding_returns_exact_canonical_service_and_binding_revision()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        var binding = await database.EstablishBindingAsync();
        await database.StorePolicyAsync(database.TenantScope);

        var facts = await database.ReadAsync(database.Request());

        await Assert.That(facts.IsAdopted).IsTrue();
        await Assert.That(facts.Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts.BindingId).IsEqualTo((Guid?)binding.Id);
        await Assert.That(facts.BindingRevision).IsEqualTo((long?)1);
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts.PermitsNativeEvaluation).IsTrue();
        await Assert.That(facts.Generation).IsNotEqualTo("not-adopted");
        await Assert.That(facts.Subject!.Id).IsNotEqualTo(database.AdministratorId);
        await Assert.That(facts.Subject!.Id).IsNotEqualTo(database.Host.MaintenanceServicePrincipalId);
    }

    [Test]
    public async Task Persisted_service_sanction_denies_the_canonical_service()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        var binding = await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope);
        var before = await database.ReadAsync(database.Request());
        await database.StoreSanctionAsync(database.ServiceSubject, policy);

        var facts = await database.ReadAsync(database.Request());

        await Assert.That(before.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts.Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts.BindingId).IsEqualTo((Guid?)binding.Id);
        await Assert.That(facts.BindingRevision).IsEqualTo((long?)1);
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(facts.PermitsNativeEvaluation).IsFalse();
        await Assert.That(facts.Generation).IsNotEqualTo(before.Generation);
    }

    [Test]
    public async Task Retiring_activation_support_does_not_erase_an_existing_resource_sanction()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope);
        await database.StoreSanctionAsync(database.ServiceSubject, policy);
        var before = await database.ReadAsync(database.Request());
        await using (var writer = database.Context())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            // Update the persisted snapshot through its checked complete-state retirement transition.
            // Setting current native publication readiness is outside this reader fixture.
            writer.ModerationTrustDomainId = database.Host.TrustDomainId;
            var current = await writer.ModerationPolicies.Include(row => row.Rules)
                .SingleAsync(row => row.Id == policy.Id);
            var retired = current.ReplaceAdoption(current.Jurisdiction, current.Revision,
                current.Rules, Guid.CreateVersion7(), Now);
            writer.Entry(current).CurrentValues.SetValues(retired);
            foreach (var rule in current.Rules)
                writer.Entry(rule).CurrentValues.SetValues(retired.Rules.Single(row => row.Id == rule.Id));
            await writer.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        var after = await database.ReadAsync(database.Request());
        await Assert.That(before.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(after.IsAdopted).IsTrue();
        await Assert.That(after.Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(after.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(after.PermitsNativeEvaluation).IsFalse();
        await Assert.That(after.Generation).IsNotEqualTo(before.Generation);
    }

    [Test]
    public async Task Human_owner_sanction_does_not_replace_the_service_subject()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope);
        await database.StoreSanctionAsync(
            SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.InstanceScope),
            policy);

        var facts = await database.ReadAsync(database.Request());

        await Assert.That(facts.Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts.PermitsNativeEvaluation).IsTrue();
    }

    [Test]
    public async Task Binding_revocation_prevents_subsequent_qualification()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        var binding = await database.EstablishBindingAsync();
        await database.StorePolicyAsync(database.TenantScope);
        // Reuse the same reader to prove that no tracked or cached positive binding survives revocation.
        await using var context = database.Context();
        var reader = database.Reader(context);
        EventResourceModerationFacts before;
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            before = await reader.ReadAsync(database.Request(), Now, CancellationToken.None);
            await transaction.CommitAsync();
        }
        await using (var writer = database.Context())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var current = await writer.ModerationMachineSubjectBindings.SingleAsync(row => row.Id == binding.Id);
            current.Revoke(binding.Revision, Now);
            await writer.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using var subsequent = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var after = await reader.ReadAsync(database.Request(), Now, CancellationToken.None);

        await Assert.That(before.Subject).IsEqualTo(database.ServiceSubject);
        await AssertUnavailable(after);
        await Assert.That(after.Generation).IsNotEqualTo(before.Generation);
        await subsequent.CommitAsync();
    }

    [Test]
    [Arguments("owner")]
    [Arguments("tenant")]
    [Arguments("source")]
    public async Task Mismatched_authenticated_coordinates_cannot_qualify_a_binding(string coordinate)
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        await database.StorePolicyAsync(database.TenantScope);
        var request = database.Request();
        var principal = request.MachinePrincipal!;
        request = request with
        {
            MachinePrincipal = coordinate switch
            {
                "owner" => principal with { OwnerId = Guid.CreateVersion7() },
                "tenant" => principal with { AuthorityTenantId = database.TenantId },
                "source" => principal with { CredentialSource = ApiKeyCredentialSource.Configured },
                _ => throw new ArgumentOutOfRangeException(nameof(coordinate))
            }
        };

        await AssertUnavailable(await database.ReadAsync(request));
    }

    [Test]
    [Arguments("resource")]
    [Arguments("tenant")]
    [Arguments("instance")]
    [Arguments("solution")]
    [Arguments("trust-domain")]
    public async Task Policy_outside_target_jurisdiction_preserves_native_evaluation(string coordinate)
    {
        await using var database = await ReaderDatabase.CreateAsync();
        // Deliberately leave the caller unbound: an unrelated active policy must not fail it closed.
        var trust = AuthorityScope.TrustDomain(
            coordinate == "trust-domain" ? Guid.CreateVersion7() : database.Host.TrustDomainId);
        var solution = AuthorityScope.Solution(trust,
            coordinate == "solution" ? Guid.CreateVersion7() : database.Host.SolutionId);
        var instance = AuthorityScope.Instance(solution,
            coordinate == "instance" ? Guid.CreateVersion7() : database.Host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance,
            coordinate == "tenant" ? Guid.CreateVersion7() : database.TenantId);
        var scope = coordinate == "resource" ? AuthorityScope.Resource(tenant, Guid.CreateVersion7()) : tenant;
        await database.StorePolicyAsync(scope);

        var facts = await database.ReadAsync(database.Request());

        await Assert.That(facts).IsEqualTo(EventResourceModerationFacts.NotAdopted);
        await Assert.That(facts.PermitsNativeEvaluation).IsTrue();
    }

    [Test]
    public async Task Batch_preserves_unadopted_targets_beside_a_sanctioned_resource()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(AuthorityScope.Resource(database.TenantScope, database.ResourceId));
        await database.StoreSanctionAsync(database.ServiceSubject, policy);
        var adopted = database.Request();
        var unrelated = adopted with { ResourceId = Guid.CreateVersion7() };
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var facts = await database.Reader(context).ReadBatchAsync(
            [unrelated, adopted, unrelated], Now, CancellationToken.None);

        await Assert.That(facts.Count).IsEqualTo(3);
        await Assert.That(facts[0]).IsEqualTo(EventResourceModerationFacts.NotAdopted);
        await Assert.That(facts[1].Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts[1].Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(facts[2]).IsEqualTo(EventResourceModerationFacts.NotAdopted);
        await transaction.CommitAsync();
    }

    private static async Task AssertUnavailable(EventResourceModerationFacts facts)
    {
        await Assert.That(facts.IsAdopted).IsTrue();
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unavailable);
        await Assert.That(facts.Subject).IsNull();
        await Assert.That(facts.BindingId).IsNull();
        await Assert.That(facts.BindingRevision).IsNull();
        await Assert.That(facts.PermitsNativeEvaluation).IsFalse();
        await Assert.That(facts.Generation).IsNotEqualTo("not-adopted");
    }

    private sealed class ReaderDatabase : IAsyncDisposable, ITenantContext
    {
        private const string KeyId = "resource-reader-service";
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<ExploreDbContext> options;

        private ReaderDatabase(SqliteConnection connection)
        {
            this.connection = connection;
            options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection)
                .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
            Host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        }

        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid AdministratorId { get; } = Guid.CreateVersion7();
        public Guid ControllerId { get; } = Guid.CreateVersion7();
        public Guid IssuerId { get; } = Guid.CreateVersion7();
        public Guid ServiceId { get; } = Guid.CreateVersion7();
        public Guid ResourceId { get; } = Guid.CreateVersion7();
        public ModerationHostBinding Host { get; }
        public AuthorityScope InstanceScope => AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Host.TrustDomainId), Host.SolutionId), Host.InstanceId);
        public AuthorityScope TenantScope => AuthorityScope.Tenant(InstanceScope, TenantId);
        public SubjectReference ServiceSubject => SubjectReference.Create(ModerationSubjectKind.Service, ServiceId, InstanceScope);
        public ExploreDbContext Context() => new(options) { TenantContext = this };
        public EventResourceAuthorityRequest Request() => new(TenantId, ResourceId, null, true, "download")
        {
            MachinePrincipal = new(KeyId, null, ExternalApiKeyOwnerType.InstanceAdmin,
                AdministratorId, ApiKeyCredentialSource.Persisted)
        };

        public EventResourceModerationSnapshotReader Reader(ExploreDbContext context) => new(context, Repository(context));

        private ModerationMachineSubjectBindingRepository Repository(ExploreDbContext context)
        {
            var http = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, AdministratorId.ToString())], "source-witness"))
                }
            };
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            return new(context, admin, new MachinePrincipalAccessor(http), new ExternalApiKeyRepository(context),
                new UnconfiguredKeys(), new FixtureClock());
        }

        public async Task<ModerationMachineSubjectBinding> EstablishBindingAsync()
        {
            await using var context = Context();
            var result = await Repository(context).EstablishAsync(new(
                Guid.CreateVersion7(), Host.Id, ModerationMachineCredentialSource.Persisted, KeyId,
                null, ExternalApiKeyOwnerType.InstanceAdmin, AdministratorId, ServiceId));
            await Assert.That(result.Outcome).IsEqualTo(ModerationAuthoritySourceOutcome.Established);
            await Assert.That(result.Binding).IsNotNull();
            return result.Binding!;
        }

        public async Task<EventResourceModerationFacts> ReadAsync(EventResourceAuthorityRequest request)
        {
            await using var context = Context();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var facts = await Reader(context).ReadAsync(request, Now, CancellationToken.None);
            await transaction.CommitAsync();
            return facts;
        }

        public async Task<ModerationPolicy> StorePolicyAsync(AuthorityScope scope, bool active = true)
        {
            var policyId = Guid.CreateVersion7();
            var publicationId = Guid.CreateVersion7();
            var terms = ModerationPolicyRule.Define(Guid.CreateVersion7(),
                ModerationRuleReference.Create(scope.Id, "resource-read", 1), "Restricted resource disclosure.",
                [ModerationPolicyOperationPurpose.Propose, ModerationPolicyOperationPurpose.Activate],
                [ModerationCapability.ReadResource], TimeSpan.FromDays(1), false, TimeSpan.FromHours(1), 1, 2);
            // These explicit fixture receipts satisfy the checked Domain publication structure only.
            // They are not native consumer registrations, readiness evidence, or proof of conformance.
            var evidence = new ModerationPolicyEffectEvidence(policyId, 1, scope, terms.Rule,
                ModerationCapability.ReadResource, publicationId, Now,
                ["fixture-adoption"],
                [new ModerationPolicyNativeEvidence("fixture-adoption", Guid.CreateVersion7(), 1, scope, Now)],
                ["fixture-persisted-reader"],
                [new ModerationPolicyNativeEvidence("fixture-persisted-reader", Guid.CreateVersion7(), 1, scope, Now)]);
            var policy = active
                ? ModerationPolicy.Publish(policyId, scope, [terms], [evidence], publicationId, Now)
                : ModerationPolicy.Adopt(policyId, scope, [terms], publicationId, Now);
            policy.ValidatePersistedState();
            await using var context = Context();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            context.ModerationPolicies.Add(policy);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return policy;
        }

        public async Task StoreSanctionAsync(SubjectReference subject, ModerationPolicy policy)
        {
            var scope = policy.Jurisdiction;
            var rule = policy.Rules.Single().Rule;
            var issuer = SubjectReference.Create(ModerationSubjectKind.Human, IssuerId, InstanceScope);
            var controller = SubjectReference.Create(ModerationSubjectKind.Human, ControllerId, InstanceScope);
            var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);
            // Ledger fixture facts exercise persisted restrictions, not the native finding writer's authority.
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1, rule, rule, controller,
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, 1, Now),
                "Fixture finding for persisted resource-read restriction.", Now);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
                scope, scope, rule, [ModerationCapability.ReadResource], Now, Now.AddHours(1), null, 1, Now);
            decision.RecordResponse(decision.Revision, Now);
            decision.AcceptFinding(finding.Id, decision.Revision, Now);
            decision.Approve(controller, scope, decision.Revision, Now);
            var sanction = decision.Activate(Guid.CreateVersion7(), decision.Revision, Now);
            decision.ValidatePersistedState();
            sanction.ValidatePersistedState();
            await using var context = Context();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            context.AddRange(allegation, finding, decision, sanction);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public static async Task<ReaderDatabase> CreateAsync()
        {
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            var database = new ReaderDatabase(connection);
            await using var context = database.Context();
            await context.Database.EnsureCreatedAsync();
            var status = new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active, MasterCode = "ACTIVE",
                FullName = "Active", IsActiveState = true
            };
            var tenant = new Tenant
            {
                Id = database.TenantId, FullName = "Resource reader tenant", Slug = $"reader-{database.TenantId:N}",
                TenantStatusId = status.Id, TenantStatus = status
            };
            var user = NativeUser(database.AdministratorId);
            var role = new Role
            {
                Id = (int)RoleEnum.Admin, MasterCode = "platform.admin", FullName = "Admin",
                IsSystem = true, RoleScopeId = (int)RoleScopeEnum.Platform,
                RoleScope = new RoleScope
                {
                    Id = (int)RoleScopeEnum.Platform, MasterCode = "PLATFORM", FullName = "Platform"
                }
            };
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            context.AddRange(tenant, user, NativeUser(database.ControllerId), NativeUser(database.IssuerId),
                new ServicePrincipal
                {
                    Id = database.Host.MaintenanceServicePrincipalId,
                    Code = $"maintenance-{database.TenantId:N}", DisplayName = "Maintenance"
                },
                new ServicePrincipal
                {
                    Id = database.ServiceId, Code = $"reader-{database.TenantId:N}", DisplayName = "Explicit reader service"
                },
                database.Host,
                new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(), UserId = user.Id, User = user, RoleId = role.Id, Role = role
                },
                ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), database.Host, tenant),
                new ExternalApiKeyStatus
                {
                    Id = (int)ExternalApiKeyStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active", IsUsable = true
                },
                new ExternalApiKeyCreditPeriod
                {
                    Id = (int)ExternalApiKeyCreditPeriodEnum.None, MasterCode = "NONE", FullName = "None"
                },
                new ExternalApiKeyOwnerTypeLookup
                {
                    Id = (int)ExternalApiKeyOwnerType.InstanceAdmin, MasterCode = "INSTANCE_ADMIN", FullName = "Instance Admin"
                });
            await context.SaveChangesAsync();
            context.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(), KeyId = KeyId, Name = "Native resource reader credential",
                SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()), Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin, OwnerId = database.AdministratorId,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active, ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None, ExternalApiKeyCreditPeriod = null!
            });
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return database;
        }

        private static User NativeUser(Guid id) => new()
        {
            Id = id,
            Pii = new UserPii
            {
                UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Reader", LastName = "Fixture"
            }
        };

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class FixtureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class UnconfiguredKeys : IConfiguredApiKeyResourceAuthorityReader
    {
        public ValueTask<ApiKeyPrincipalContext?> ReadAsync(
            string keyId, DateTimeOffset evaluationUtc, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ApiKeyPrincipalContext?>(null);
    }
}
