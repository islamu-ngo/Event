using System.Collections.Immutable;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Handlers.Queries;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static (GetModerationCurrentPolicyQueryHandler Handler, HttpContextAccessor Http, MachinePrincipalAccessor Machine)
        CurrentAuthoringHandler(Database database, ExploreDbContext context, ModerationCapabilityRegistry? inventory = null)
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, database.AdminId.ToString())], "current-authoring-witness"))
            }
        };
        var identity = new AdminContext(http, new PlatformUserRoleRepository(context),
            new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new UserExternalLoginRepository(context),
            NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        var repository = new ModerationPolicyPublicationRepository(
            context, identity, machine, inventory ?? database.Inventory, null, new FixedClock());
        return (new(repository, identity, context.TenantContext!), http, machine);
    }

    public enum EvidenceFailure
    {
        None, StaleSource, WrongTransaction, MissingSemanticOwner, MissingConsequenceOwner,
        MissingNativePrerequisite, NotMeasured, PartialConsumerSet, StaleConsumerGeneration, FlushThenReject,
        ExpiredMeasurement, UnboundedMeasurement, NonUtcMeasurement
    }

    private sealed class Database : IAsyncDisposable, ITenantContext
    {
        private readonly SqliteConnection _connection;
        public ModerationNoticePrivacyFixture Privacy { get; private set; } = null!;
        private readonly string? physicalPath;
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid AdminId { get; } = Guid.CreateVersion7();
        public Guid HostId { get; } = Guid.CreateVersion7();
        public Guid EpochId { get; } = Guid.CreateVersion7();
        public AuthorityScope Scope { get; }
        public ModerationCapabilityRegistry Inventory { get; }

        private Database(SqliteConnection connection, bool implementedInventory, string? physicalPath)
        {
            _connection = connection;
            this.physicalPath = physicalPath;
            Scope = AuthorityScope.Tenant(AuthorityScope.Instance(
                AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
                Guid.CreateVersion7()), TenantId);
            var json = JsonNode.Parse(ModerationCapabilityRegistry.ReadInventory())!.AsObject();
            if (implementedInventory)
            {
                // Only source labels change. They deliberately do not produce any Passed receipt.
                foreach (var consumer in json["consumers"]!.AsArray())
                    consumer!["supportState"] = nameof(ModerationSourceSupport.ImplementedUnverified);
                foreach (var mode in json["modes"]!.AsArray())
                    mode!["supportState"] = nameof(ModerationSourceSupport.ImplementedUnverified);
            }
            Inventory = ModerationCapabilityRegistry.Parse(json.ToJsonString());
        }

        public ExploreDbContext Context(params IInterceptor[] interceptors)
        {
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, new Explore.Secrets.Database.PrimaryDatabaseConnectionOptions
            {
                Role = Explore.Secrets.Database.PrimaryDatabaseRole.Runtime,
                Provider = Explore.Secrets.Database.PrimaryDatabaseProvider.Sqlite,
                Database = _connection.DataSource
            });
            builder.UseSqlite(_connection)
                .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance);
            if (interceptors.Length != 0) builder.AddInterceptors(interceptors);
            return new(builder.Options) { TenantContext = this };
        }

        public static async Task<Database> CreateAsync(bool implementedInventory = false, bool physicalFile = false)
        {
            var path = physicalFile
                ? Path.Combine(Path.GetTempPath(), $"moderation-recognition-{Guid.CreateVersion7():N}.sqlite") : null;
            var connection = path is null ? await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync()
                : new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            if (path is not null) await connection.OpenAsync();
            await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, CancellationToken.None);
            var database = new Database(connection, implementedInventory, path);
            database.Privacy = await ModerationNoticePrivacyFixture.CreateAsync();
            await using var context = database.Context();
            var status = await context.TenantStatuses.SingleAsync(row => row.IsActiveState);
            context.Tenants.Add(new Tenant
            {
                Id = database.TenantId,
                FullName = "Rich publication witness",
                Slug = $"publication-{database.TenantId:N}",
                TenantStatusId = status.Id,
                TenantStatus = status
            });
            var role = await context.Roles.SingleAsync(row =>
                row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
            var user = new User
            {
                Id = database.AdminId,
                Pii = new UserPii
                {
                    UserId = database.AdminId,
                    Email = $"{database.AdminId:N}@example.invalid",
                    FirstName = "Publication",
                    LastName = "Witness"
                }
            };
            context.Users.Add(user);
            context.PlatformUserRoles.Add(new PlatformUserRole
            { Id = Guid.CreateVersion7(), UserId = user.Id, User = user, RoleId = role.Id, Role = role });
            context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                UserId = user.Id,
                User = user,
                StatusId = (int)TenantUserStatusEnum.Active
            });
            var service = new ServicePrincipal
            { Id = Guid.CreateVersion7(), Code = $"publication-{database.TenantId:N}", DisplayName = "Publication maintenance" };
            context.ServicePrincipals.Add(service);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var (admin, machine) = database.Identity(context);
            var result = await new EstablishModerationAuthoritySourcesCommandHandler(
                new ModerationAuthoritySourceRepository(context, admin, machine)).ExecuteAsync(new EstablishModerationAuthoritySourcesCommand
                {
                    HostBindingId = database.HostId,
                    TrustDomainId = database.Scope.TrustDomainId,
                    SolutionId = database.Scope.Parent!.Parent!.Id,
                    InstanceId = database.Scope.Parent.Id,
                    MaintenanceServicePrincipalId = service.Id,
                    TenantId = database.TenantId,
                    EpochId = database.EpochId
                });
            if (!result.IsSuccess) throw new InvalidOperationException("Native publication authority installation failed.");
            return database;
        }

        public ModerationPolicyPublicationRequest Request(params ModerationReferenceProfileId[] references)
        {
            if (references.Length == 0) references = [ModerationReferenceProfileId.CFG05];
            ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(Scope.Id, code, 1);
            var profiles = references.Select(id =>
            {
                var reference = ModerationReferenceCatalogue.GetProfile(id);
                var response = reference.Responses[0];
                return new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule($"profile.{reference.Code}"),
                    Rule($"reason.{reference.Code}"), reference.Id, reference.Revision, response.Grade,
                    [reference.ScopeKinds.Contains(ModerationScopeKind.Tenant) ? ModerationScopeKind.Tenant : reference.ScopeKinds.Max()],
                    [reference.SubjectKinds.Order().First()], reference.Effects.ToImmutableArray(),
                    reference.Bundle, reference.ProtectionPurpose, reference.Strategies.Order().ToImmutableArray(),
                    new(response.Timing.Kind, response.Timing.Duration), response.RequiredApprovals, 6,
                    ModerationReferenceRecognition.Never);
            }).ToImmutableArray();
            var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), Scope,
                ModerationReferenceCatalogue.Revision, Inventory.Revision, Inventory.Fingerprint,
                ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
                references.Select(id =>
                {
                    var reference = ModerationReferenceCatalogue.GetProfile(id);
                    return new ModerationPolicyClassDefinition(Rule($"class.{reference.Code}"),
                        $"Meaning of {reference.Code}.", reference.Ground, reference.Basis);
                }).ToImmutableArray(),
                references.Select(id =>
                {
                    var code = ModerationReferenceCatalogue.GetProfile(id).Code;
                    return new ModerationPolicyReasonDefinition(Rule($"reason.{code}"), $"Reason for {code}.", Rule($"class.{code}"));
                }).ToImmutableArray(), profiles, profiles.Select(row => row.Rule).ToImmutableArray());
            return new(Guid.CreateVersion7(), HostId, EpochId, 1,
                ModerationPolicyPublicationProjection.WriterGeneration, null, proposed);
        }

        public ModerationPolicyPublicationRequest Next(ModerationPolicyPublicationRequest prior) => prior with
        {
            OperationId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = prior.ExpectedQualificationRevision + 1,
            ExpectedPublicationId = prior.Proposed.PublicationId,
            Proposed = prior.Proposed with
            { PublicationId = Guid.CreateVersion7(), ExpectedRevision = prior.Proposed.ExpectedRevision + 1 }
        };

        public ModerationPolicyPublicationRepository Writer(
            ExploreDbContext context, IModerationPolicyPublicationEvidenceOwner? owner = null, bool machineCaller = false)
        {
            var (admin, machine) = Identity(context);
            if (machineCaller)
                machine.SetPrincipal(new Explore.Application.Authentication.ApiKeyPrincipalContext(
                    Guid.CreateVersion7().ToString(), TenantId, ExternalApiKeyOwnerType.User, AdminId, []));
            return new(context, admin, machine, Inventory, owner, new FixedClock());
        }

        public async Task<ModerationPolicyPublicationResult> PublishAsync(ModerationPolicyPublicationRequest request, bool machine = false)
        {
            await using var context = Context();
            return await Writer(context, machineCaller: machine).PublishAsync(request);
        }

        public async Task<IModerationConsumerEvidenceSession> ConsumerEvidenceAsync(
            ExploreDbContext context, ModerationPolicyPublicationRequest request)
        {
            var (admin, machine) = Identity(context);
            var source = await new ModerationQualificationAuthority(context, admin, machine)
                .AcquireAsync(request.Proposed.Jurisdiction, request.Proposed.PublicationId, default)
                ?? throw new InvalidOperationException("Publication evidence requires an actual native authority acquisition.");
            var writer = Writer(context);
            var publication = await writer.AcquireSelectedAsync(context.Database.CurrentTransaction!.TransactionId,
                HostId, Scope, request.Proposed.PolicyId, true)
                ?? throw new InvalidOperationException("Publication evidence requires the actual selected active graph.");
            var policy = ModerationPolicyPublicationProjection.Restore(publication, Inventory)
                ?? throw new InvalidOperationException("The selected active graph must retain its complete native inventory.");
            return writer.BorrowConsumerEvidence(source, policy);
        }

        public async Task<ModerationPolicyPublicationRequest> PrepareActiveAsync(ModerationPolicyPublicationRequest request)
        {
            await using var context = Context();
            var prepared = await Writer(context).PrepareAsync(request, Now.AddHours(2));
            if (prepared.Outcome != ModerationPolicyPublicationOutcome.Known)
                throw new InvalidOperationException("The active writer witness requires a committed immutable preparation.");
            return request with
            {
                OperationId = Guid.CreateVersion7(),
                ExpectedQualificationRevision = request.ExpectedQualificationRevision + 1
            };
        }

        public async Task AssertCountsAsync(int publications, int selections, long epoch)
        {
            await using var context = Context();
            context.ModerationTrustDomainId = Scope.TrustDomainId;
            await Assert.That(await context.ModerationPolicyPublications.CountAsync()).IsEqualTo(publications);
            await Assert.That(await context.ModerationPolicySelections.CountAsync()).IsEqualTo(selections);
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(epoch);
        }

        private (AdminContext Admin, MachinePrincipalAccessor Machine) Identity(ExploreDbContext context)
        {
            var http = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, AdminId.ToString())], "publication-witness"))
                }
            };
            return (new(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
                new OrganizationMemberRepository(context), new GroupMemberRepository(context),
                new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance), new(http));
        }

        public async ValueTask DisposeAsync()
        {
            await Privacy.DisposeAsync();
            await _connection.DisposeAsync();
            if (physicalPath is not null)
                foreach (var path in new[] { physicalPath, physicalPath + "-wal", physicalPath + "-shm" })
                    File.Delete(path);
        }
    }

    /// <summary>
    /// Controlled external measurement authority for writer contract tests only. Current source
    /// and prior approval coordinates are separate observations; each failure changes one side.
    /// This class is not installed in production and measures no real deployed consumer.
    /// </summary>
    private sealed class EvidenceOwner(
        Database database, ExploreDbContext context, EvidenceFailure failure = EvidenceFailure.None,
        TimeSpan? approvalLifetime = null, TimeSpan? measurementLifetime = null)
        : IModerationPolicyPublicationEvidenceOwner
    {
        private readonly Dictionary<string, Guid> _sources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> _approvals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> _consumers = new(StringComparer.Ordinal);
        private Guid Id(Dictionary<string, Guid> values, string key)
        {
            if (!values.TryGetValue(key, out var id)) values.Add(key, id = Guid.CreateVersion7());
            return id;
        }

        public async Task<ModerationPolicyPublicationCurrentSource?> AcquireSourceAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationPolicyPublicationObligation obligation, DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            if (failure == EvidenceFailure.FlushThenReject)
            {
                var epoch = await context.ModerationQualificationEpochs.SingleAsync(row => row.Id == database.EpochId, cancellationToken);
                epoch.Advance(epoch.Revision);
                await context.SaveChangesAsync(cancellationToken);
                return null;
            }
            var key = ModerationPolicyPublicationProjection.Digest(obligation);
            return new(transactionId, key, Id(_sources, key), failure == EvidenceFailure.StaleSource ? 2 : 1,
                "fixture-source-v1", observedAt);
        }

        public Task<ModerationPolicyPublicationApproval?> ReadApprovalAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationPolicyPublicationObligation obligation, DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            var missing = failure == EvidenceFailure.MissingSemanticOwner && obligation.Effect is not null
                || failure == EvidenceFailure.MissingConsequenceOwner && obligation.Consequence is not null
                || failure == EvidenceFailure.MissingNativePrerequisite && obligation.Prerequisite is { NativePrerequisites.Length: > 0 };
            var key = ModerationPolicyPublicationProjection.Digest(obligation);
            return Task.FromResult<ModerationPolicyPublicationApproval?>(missing ? null :
                new(failure == EvidenceFailure.WrongTransaction ? Guid.CreateVersion7() : transactionId,
                    key, Id(_approvals, key), 1, Id(_sources, key), 1, "fixture-source-v1", observedAt,
                    observedAt + (approvalLifetime ?? TimeSpan.FromHours(1))));
        }

        public Task<ModerationCapabilitySourceObservation?> AcquireConsumerAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult<ModerationCapabilitySourceObservation?>(new(binding, consumerId,
                Id(_consumers, consumerId), 1, failure == EvidenceFailure.StaleConsumerGeneration ? "fixture-new-generation" : "fixture-v1",
                observedAt, observedAt.AddHours(1)));

        public Task<ModerationCapabilityRegistration?> ReadConformanceAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult<ModerationCapabilityRegistration?>(failure == EvidenceFailure.PartialConsumerSet ? null :
                new(binding, database.Inventory.Fingerprint, database.Inventory.Consumers[consumerId].Contract,
                    Id(_consumers, consumerId), 1, "fixture-v1",
                    failure == EvidenceFailure.NotMeasured ? ModerationNativeConformance.NotRun : ModerationNativeConformance.Passed,
                    observedAt, failure switch
                    {
                        EvidenceFailure.ExpiredMeasurement => observedAt,
                        EvidenceFailure.UnboundedMeasurement => DateTimeOffset.MaxValue,
                        EvidenceFailure.NonUtcMeasurement => observedAt.AddHours(1).ToOffset(TimeSpan.FromHours(1)),
                        _ => observedAt + (measurementLifetime ?? TimeSpan.FromHours(1))
                    }));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class PublicationWriteInterrupted : Exception { }

    private sealed class LostCommitAcknowledgement : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Native publication committed but acknowledgement was lost.");
    }

    private sealed class RejectSavedGraph : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<ModerationPolicyPublication>().Any()
                ? throw new PublicationWriteInterrupted() : ValueTask.FromResult(result);
    }
}
