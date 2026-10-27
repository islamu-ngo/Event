using System.Collections.Immutable;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationQualificationWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    internal sealed partial class SourceDatabase : IAsyncDisposable, ITenantContext
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ExploreDbContext> _options;
        public ModerationNoticePrivacyFixture Privacy { get; private set; } = null!;

        private SourceDatabase(SqliteConnection connection)
        {
            _connection = connection;
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplicationModel(builder, new Explore.Secrets.Database.PrimaryDatabaseConnectionOptions
            {
                Role = Explore.Secrets.Database.PrimaryDatabaseRole.Runtime,
                Provider = Explore.Secrets.Database.PrimaryDatabaseProvider.Sqlite
            });
            _options = builder.UseSqlite(connection)
                .UseSnakeCaseNamingConvention().AddInterceptors(new ExpireGovernanceOnSave(this)).Options;
            Instance = AuthorityScope.Instance(
                AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
                Guid.CreateVersion7());
            Scope = AuthorityScope.Tenant(Instance, TenantId);
            CaseScope = Scope;
        }

        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid HostId { get; } = Guid.CreateVersion7();
        public Guid EpochId { get; } = Guid.CreateVersion7();
        public Guid AdministratorId { get; } = Guid.CreateVersion7();
        public Guid UrgencyAssessorId { get; } = Guid.CreateVersion7();
        public Guid ReviewerId { get; } = Guid.CreateVersion7();
        public Guid SubjectId { get; } = Guid.CreateVersion7();
        public AuthorityScope Instance { get; }
        public AuthorityScope Scope { get; }
        public AuthorityScope CaseScope { get; private set; }
        public ModerationCapabilityRegistry Inventory { get; } =
            ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        public TimeSpan ReviewerLifetime { get; set; } = TimeSpan.FromHours(1);
        public TimeSpan UrgencyLifetime { get; set; } = TimeSpan.FromHours(1);
        public TimeSpan ApprovedReviewerLifetime { get; set; } = TimeSpan.FromHours(1);
        public TimeSpan ApprovedUrgencyLifetime { get; set; } = TimeSpan.FromHours(1);
        public bool GovernanceAvailable { get; set; } = true;
        public bool GovernanceOwnerAvailable { get; set; } = true;
        public Guid ProceduralSourceId { get; } = Guid.CreateVersion7();
        public Guid ProceduralApprovalId { get; } = Guid.CreateVersion7();
        public Guid CaseId { get; private set; }
        public bool FlushBeforeUnavailable { get; set; }
        public bool PartialFlushObserved { get; set; }
        public bool ExpireGovernanceAfterSave { get; set; }
        public DateTimeOffset EvaluationTime { get; set; } = Now;
        public IEnumerable<IModerationConsumerRuntimeSource> ConsumerRuntimeSources { get; set; } = [];
        public Action? QualificationSaved { get; set; }
        public Guid PartialEvidenceId { get; } = Guid.CreateVersion7();
        public ExploreDbContext Context(IInterceptor? interceptor = null)
        {
            var context = new ExploreDbContext(interceptor is null ? _options
                : new DbContextOptionsBuilder<ExploreDbContext>(_options).AddInterceptors(interceptor).Options)
            { TenantContext = this };
            context.BindTenantLease();
            return context;
        }

        public static async Task<SourceDatabase> CreateAsync()
        {
            using var progress = new StreamWriter(Environment.GetEnvironmentVariable("GRANT_TEST_PROGRESS_PATH") is { Length: > 0 } progressFile
                ? new FileStream(progressFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                : Console.OpenStandardError())
            { AutoFlush = true };
            progress.WriteLine("[DEBUG-GRANT-SETTLEMENT] source-template:start");
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, CancellationToken.None);
            progress.WriteLine("[DEBUG-GRANT-SETTLEMENT] source-template:complete");
            var database = new SourceDatabase(connection);
            progress.WriteLine("[DEBUG-GRANT-SETTLEMENT] source-privacy:start");
            database.Privacy = await ModerationNoticePrivacyFixture.CreateAsync();
            progress.WriteLine("[DEBUG-GRANT-SETTLEMENT] source-privacy:complete");
            await using var context = database.Context();
            var status = await context.TenantStatuses.SingleAsync(row => row.IsActiveState);
            context.Tenants.Add(new Tenant
            {
                Id = database.TenantId,
                FullName = "Qualification source tenant",
                Slug = $"source-{database.TenantId:N}",
                TenantStatusId = status.Id,
                TenantStatus = status
            });
            var role = await context.Roles.SingleAsync(row =>
                row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
            foreach (var id in new[] { database.AdministratorId, database.UrgencyAssessorId, database.ReviewerId, database.SubjectId })
            {
                var user = new User
                {
                    Id = id,
                    Pii = new UserPii
                    {
                        UserId = id,
                        Email = $"{id:N}@example.invalid",
                        FirstName = "Source",
                        LastName = "Witness"
                    }
                };
                context.Users.Add(user);
                if (id != database.SubjectId)
                    context.PlatformUserRoles.Add(new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = id,
                        User = user,
                        RoleId = role.Id,
                        Role = role
                    });
                context.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = database.TenantId,
                    Tenant = null!,
                    UserId = id,
                    User = user,
                    StatusId = (int)TenantUserStatusEnum.Active
                });
            }
            var principal = new ServicePrincipal
            {
                Id = Guid.CreateVersion7(),
                Code = $"maintenance-{database.TenantId:N}",
                DisplayName = "Source maintenance"
            };
            context.ServicePrincipals.Add(principal);
            progress.WriteLine("[DEBUG-GRANT-SETTLEMENT] source-seed-save:start");
            await context.SaveChangesAsync();
            progress.WriteLine("[DEBUG-GRANT-SETTLEMENT] source-seed-save:complete");
            context.ChangeTracker.Clear();
            var identity = Identity(context, database.AdministratorId, false);
            var setup = await new EstablishModerationAuthoritySourcesCommandHandler(
                new ModerationAuthoritySourceRepository(context, identity.Admin, identity.Machine))
                .ExecuteAsync(new EstablishModerationAuthoritySourcesCommand
                {
                    HostBindingId = database.HostId,
                    TrustDomainId = database.Instance.TrustDomainId,
                    SolutionId = database.Instance.Parent!.Id,
                    InstanceId = database.Instance.Id,
                    MaintenanceServicePrincipalId = principal.Id,
                    TenantId = database.TenantId,
                    EpochId = database.EpochId
                });
            if (!setup.IsSuccess)
                throw new InvalidOperationException("The real native authority fixture did not install.");
            progress.WriteLine("[DEBUG-GRANT-SETTLEMENT] source-fixture:ready");
            return database;
        }

        public OpenModerationCaseSource OpenCase(AuthorityScope? target = null)
        {
            CaseId = Guid.CreateVersion7();
            CaseScope = target ?? Scope;
            return new()
            {
                OperationId = Guid.CreateVersion7(),
                Target = CaseScope,
                ExpectedQualificationRevision = 1,
                CaseId = CaseId,
                SubjectKind = ModerationSubjectKind.Human,
                SubjectId = SubjectId,
                OccurrenceId = Guid.CreateVersion7()
            };
        }

        public ModerationPolicyPublicationRequest Publication(
            long generation, ModerationReferenceProfileId referenceProfile = ModerationReferenceProfileId.CFG06)
        {
            var reference = ModerationReferenceCatalogue.GetProfile(referenceProfile);
            var response = reference.Responses[0];
            ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(Scope.Id, code, 1);
            var classification = Rule("native-source-class");
            var reason = Rule("native-source-reason");
            var profile = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule("native-source-witness"), reason,
                reference.Id, reference.Revision, response.Grade, [ModerationScopeKind.Tenant],
                [ModerationSubjectKind.Human], reference.Effects.ToImmutableArray(), reference.Bundle,
                reference.ProtectionPurpose, [], new(response.Timing.Kind, TimeSpan.FromHours(1)),
                response.RequiredApprovals, 6, ModerationReferenceRecognition.Never);
            var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), Scope,
                ModerationReferenceCatalogue.Revision, Inventory.Revision, Inventory.Fingerprint,
                ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
                [new(classification, "Native source classification.", reference.Ground, reference.Basis)],
                [new(reason, "Current source authority and protected response.", classification)], [profile], [profile.Rule]);
            return new(Guid.CreateVersion7(), HostId, EpochId, generation,
                ModerationPolicyPublicationProjection.WriterGeneration, null, proposed);
        }

        public async Task<ModerationPolicyPublicationResult> PublishAsync(
            Guid actor, ModerationPolicyPublicationRequest request)
        {
            await using var context = Context();
            var identity = Identity(context, actor, false);
            return await new ModerationPolicyPublicationRepository(
                context, identity.Admin, identity.Machine, Inventory, null, new FixedClock()).PublishAsync(request);
        }

        private static (AdminContext Admin, MachinePrincipalAccessor Machine) Identity(
            ExploreDbContext context, Guid userId, bool machineCaller)
        {
            var http = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "platform.admin")],
                        "source-witness"))
                }
            };
            var admin = new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null), NullLogger<AdminContext>.Instance);
            var machine = new MachinePrincipalAccessor(http);
            if (machineCaller)
                machine.SetPrincipal(new ApiKeyPrincipalContext(Guid.CreateVersion7().ToString(), context.TenantFilterTenantId,
                    ExternalApiKeyOwnerType.User, userId, []));
            return (admin, machine);
        }

        public async ValueTask DisposeAsync()
        {
            await Privacy.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class ExpireGovernanceOnSave(SourceDatabase database) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ModerationQualificationMutationReceipt>().Any())
                database.QualificationSaved?.Invoke();
            if (database.ExpireGovernanceAfterSave
                && eventData.Context!.ChangeTracker.Entries<ModerationQualificationMutationReceipt>().Any())
                database.EvaluationTime = Now.AddHours(1);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
