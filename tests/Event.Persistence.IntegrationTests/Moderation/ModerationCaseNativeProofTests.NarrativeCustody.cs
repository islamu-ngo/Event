using System.Security.Claims;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    public async Task Standalone_native_read_and_disclosure_do_not_require_event_ancestry(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind, standalone: true);
        var submission = await database.IssueAsync(ModerationCaseRelationshipKind.InstanceAdministrator);
        Guid caseId;
        await using (var context = database.Context())
        {
            var accepted = await IssuanceCommitter(context, database, database.Caller).SubmitAsync(new(
                ModerationCaseAccessPurpose.SubmitAllegation, Guid.Empty, Guid.CreateVersion7(), 0,
                submission.Receipt.Id, submission.Token) { Narrative = database.Request().Material }, default);
            await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
            caseId = accepted.Acknowledgement!.CaseId;
        }
        Guid grantId;
        await using (var context = database.Context())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var owner = database.Owner(context);
            var grant = await owner.GrantReadAsync(new(Guid.CreateVersion7(), caseId, 1,
                submission.Receipt.SourceId, database.CallerId, database.Clock.Now.AddMinutes(1)), default);
            await Assert.That(grant).IsNotNull();
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
            grantId = grant!.Id;
        }
        await using var read = database.Context();
        var issued = await IssuanceCommitter(read, database, database.Caller).IssueReadReceiptAsync(grantId, default);
        await Assert.That(issued).IsNotNull();
        var intake = new ModerationCaseIntakeRepository(read,
            new ModerationCaseTargetReceiptRepository(read, database.Caller, database.Machine, database.Clock),
            database.Owner(read), database.Clock);
        var custody = new ModerationCaseNarrativeRepository(read, NarrativeAdmin(read, database.Caller),
            database.Caller, database.Machine, intake, database.Clock, database.NarrativeProtection);
        ReadOnlyMemory<byte> borrowed = default;
        var deliveries = 0;
        await Assert.That(await custody.DiscloseAsync(new(ModerationCaseAccessPurpose.ReadAllegation,
            caseId, Guid.Empty, 1, issued!.Receipt.Id, issued.Token), async (material, _) =>
        {
            deliveries++;
            borrowed = material;
            await Assert.That(read.Database.CurrentTransaction).IsNotNull();
            await Assert.That(material.ToArray()).IsEquivalentTo(database.Request().Material.ToArray());
        }, default)).IsTrue();
        await Assert.That(deliveries).IsEqualTo(1);
        await Assert.That(borrowed.Length).IsGreaterThan(0);
        await Assert.That(borrowed.ToArray().All(value => value == 0)).IsTrue();
        await Assert.That(read.Database.CurrentTransaction).IsNull();
        await Assert.That(read.ModerationTrustDomainId).IsNull();
        await Assert.That(read.ChangeTracker.Entries().Any()).IsFalse();
        await using var inspect = database.Context();
        await Assert.That(await inspect.Events.AnyAsync()).IsFalse();
        var source = await inspect.ModerationCaseTargetSources.SingleAsync(row => row.Id == submission.Receipt.SourceId);
        await Assert.That(source.EventId).IsNull();
        await Assert.That(source.EventVersion).IsNull();
        if (kind == ModerationCaseTargetKind.Service)
            await Assert.That(await inspect.Actors.AnyAsync(row => row.ServicePrincipalId == database.ServiceId)).IsFalse();
    }

    [Test]
    public async Task TerminalNarrativeDisclosureKeepsNativeTransactionAndClearsBorrowedBytes()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var (caseId, grantId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var receipt = await IssuanceCommitter(context, database, database.Caller).IssueReadReceiptAsync(grantId, default);
        await Assert.That(receipt).IsNotNull();
        var owner = database.Owner(context);
        var intake = new ModerationCaseIntakeRepository(context,
            new ModerationCaseTargetReceiptRepository(context, database.Caller, database.Machine, database.Clock),
            owner, database.Clock);
        var custody = new ModerationCaseNarrativeRepository(context, NarrativeAdmin(context, database.Caller),
            database.Caller, database.Machine, intake, database.Clock, database.NarrativeProtection);
        ReadOnlyMemory<byte> borrowed = default;
        int deliveries = 0;
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.ReadAllegation,
            caseId, Guid.Empty, 1, receipt!.Receipt.Id, receipt.Token);
        var allowed = await custody.DiscloseAsync(request, async (material, _) =>
        {
            deliveries++;
            borrowed = material;
            await Assert.That(context.Database.CurrentTransaction).IsNotNull();
            await Assert.That(context.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(System.Data.IsolationLevel.Serializable);
            await Assert.That(material.ToArray()).IsEquivalentTo(database.Request().Material.ToArray());
        }, default);
        await Assert.That(allowed).IsTrue();
        await Assert.That(deliveries).IsEqualTo(1);
        await Assert.That(borrowed.Length).IsGreaterThan(0);
        await Assert.That(borrowed.ToArray().All(value => value == 0)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();

        var changedToken = receipt.Token.ToArray();
        changedToken[0] ^= 1;
        var denied = await custody.DiscloseAsync(request with { PresentedToken = changedToken }, (_, _) =>
        {
            deliveries++;
            return Task.CompletedTask;
        }, default);
        await Assert.That(denied).IsFalse();
        await Assert.That(deliveries).IsEqualTo(1);
    }

    [Test]
    public async Task FailedTerminalNarrativeDeliveryCannotRetryOrRetainPlaintext()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var (caseId, grantId) = await GrantedReadAsync(database);
        await using var context = database.RetryingContext();
        var receipt = await IssuanceCommitter(context, database, database.Caller).IssueReadReceiptAsync(grantId, default);
        await Assert.That(receipt).IsNotNull();
        var owner = database.Owner(context);
        var intake = new ModerationCaseIntakeRepository(context,
            new ModerationCaseTargetReceiptRepository(context, database.Caller, database.Machine, database.Clock),
            owner, database.Clock);
        var custody = new ModerationCaseNarrativeRepository(context, NarrativeAdmin(context, database.Caller),
            database.Caller, database.Machine, intake, database.Clock, database.NarrativeProtection);
        ReadOnlyMemory<byte> borrowed = default;
        int attempts = 0;
        await Assert.That(async () => await custody.DiscloseAsync(new(ModerationCaseAccessPurpose.ReadAllegation,
            caseId, Guid.Empty, 1, receipt!.Receipt.Id, receipt.Token), (material, _) =>
        {
            attempts++;
            borrowed = material;
            throw new SqliteException("Terminal delivery failure.", 5);
        }, default)).Throws<SqliteException>();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(borrowed.Length).IsGreaterThan(0);
        await Assert.That(borrowed.ToArray().All(value => value == 0)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
    }

    [Test]
    public async Task TypedCustodyWithdrawalPreservesOriginalCaptureAndExactAcknowledgement()
    {
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var receipt = await database.IssueAsync();
        await using var context = database.Context();
        var accepted = await Committer(context, database).SubmitAsync(new(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, receipt.Receipt.Id, receipt.Token)
        { Narrative = database.Request().Material }, default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        context.ModerationTrustDomainId = database.TrustId;
        var adoption = await context.ModerationCaseNarrativeAdoptions.AsNoTracking().SingleAsync();
        var narrative = await context.ModerationCaseNarratives.AsNoTracking().SingleAsync();
        var host = await context.ModerationHostBindings.AsNoTracking().SingleAsync();
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId),
            host.InstanceId), database.TenantId);
        var command = new WithdrawModerationCaseNarrativePolicyCommand
        {
            OperationId = Guid.CreateVersion7(), Target = scope, ExpectedQualificationRevision = 5,
            AdoptionId = adoption.Id, ExpectedAdoptionRevision = 1
        };
        var handler = new WithdrawModerationCaseNarrativePolicyCommandHandler(
            NativeNarrativePolicyWriter(context, database, database.Caller, privacy.Authority(context, database.Clock)));
        var withdrawn = await handler.ExecuteAsync(command);
        await Assert.That(withdrawn.IsSuccess).IsTrue();
        await Assert.That(withdrawn.Id).IsEqualTo(adoption.Id);
        await Assert.That(withdrawn.Revision).IsEqualTo((long?)2);
        await Assert.That(withdrawn.QualificationRevision).IsEqualTo((long?)4);
        var replay = await handler.ExecuteAsync(command);
        await Assert.That(replay).IsEqualTo(withdrawn);
        context.ModerationTrustDomainId = database.TrustId;
        var original = await context.ModerationCaseNarratives.AsNoTracking().SingleAsync();
        await Assert.That(original.PurgeAt).IsEqualTo(narrative.PurgeAt);
        await Assert.That(original.ProtectedMaterial.ToArray()).IsEquivalentTo(narrative.ProtectedMaterial.ToArray());
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    private static AdminContext NarrativeAdmin(ExploreDbContext context, NativeCaller caller)
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, caller.UserId!.Value.ToString())], "native-case-witness"))
            }
        };
        return new(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context),
            new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
    }

    private static async Task QualifyNarrativeCustodyAsync(NativeDatabase database, ModerationCaseTargetKind kind)
    {
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyOwnerId = Guid.CreateVersion7();
        var governanceControllerId = Guid.CreateVersion7();
        AuthorityScope scope;
        await using (var context = database.Context())
        {
            foreach (var id in new[] { privacyOwnerId, governanceControllerId })
                context.Users.Add(new User
                {
                    Id = id, Pii = new UserPii
                    {
                        UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Independent", LastName = "Controller"
                    }
                });
            context.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(), UserId = privacyOwnerId, User = null!, RoleId = (int)RoleEnum.Admin, Role = null!,
                GrantedAt = database.Clock.Now.UtcDateTime
            });
            await context.SaveChangesAsync();
            var host = await context.ModerationHostBindings.SingleAsync();
            scope = AuthorityScope.Tenant(AuthorityScope.Instance(
                AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId),
                host.InstanceId), database.TenantId);
        }
        var source = new RecordModerationCaseNarrativePolicyCommand
        {
            OperationId = Guid.CreateVersion7(), Target = scope, ExpectedQualificationRevision = 1,
            SourceId = Guid.CreateVersion7(), TargetKind = kind,
            Terms = new(ModerationCaseNarrativePurpose.ConfidentialAllegation, TimeSpan.FromMinutes(30), 4096),
            Ownership = new(privacyOwnerId, privacyOwnerId, governanceControllerId,
                "Separate canonical privacy and governance controllers.",
                "Finite confidential allegation custody; ordinary erasure remains independent."),
            ValidUntil = database.Clock.Now.AddHours(12)
        };
        await Assert.That((await ExecuteAsync(database.Caller, source)).IsSuccess).IsTrue();
        await Assert.That((await ExecuteAsync(new NativeCaller(privacyOwnerId), new AdoptModerationCaseNarrativePolicyCommand
        {
            OperationId = Guid.CreateVersion7(), Target = scope, ExpectedQualificationRevision = 2,
            SourceId = source.SourceId, ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = 2,
            AdoptionId = Guid.CreateVersion7(), ValidUntil = source.ValidUntil
        })).IsSuccess).IsTrue();

        var budget = new RecordModerationOperationalBudgetCommand
        {
            OperationId = Guid.CreateVersion7(), Target = scope, ExpectedQualificationRevision = 3,
            SourceId = Guid.CreateVersion7(), OperatorHumanId = privacyOwnerId,
            Terms = new(ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, 8, TimeSpan.FromHours(1)),
            ValidUntil = source.ValidUntil
        };
        await Assert.That((await ExecuteAsync(database.Caller, budget)).IsSuccess).IsTrue();
        await Assert.That((await ExecuteAsync(new NativeCaller(privacyOwnerId), new AdoptModerationOperationalBudgetCommand
        {
            OperationId = Guid.CreateVersion7(), Target = scope, ExpectedQualificationRevision = 4,
            SourceId = budget.SourceId, ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = 4,
            AdoptionId = Guid.CreateVersion7(), ValidUntil = budget.ValidUntil
        })).IsSuccess).IsTrue();

        async Task<ModerationQualificationResponse> ExecuteAsync(
            NativeCaller caller, ModerationQualificationCommand command)
        {
            await using var context = database.Context();
            context.CurrentUserService = caller;
            var sources = NativeNarrativePolicyWriter(context, database, caller, privacy.Authority(context, database.Clock));
            return command switch
            {
                RecordModerationCaseNarrativePolicyCommand recording =>
                    await new RecordModerationCaseNarrativePolicyCommandHandler(sources).ExecuteAsync(recording),
                AdoptModerationCaseNarrativePolicyCommand adopting =>
                    await new AdoptModerationCaseNarrativePolicyCommandHandler(sources).ExecuteAsync(adopting),
                RecordModerationOperationalBudgetCommand recording =>
                    await new RecordModerationOperationalBudgetCommandHandler(sources).ExecuteAsync(recording),
                AdoptModerationOperationalBudgetCommand adopting =>
                    await new AdoptModerationOperationalBudgetCommandHandler(sources).ExecuteAsync(adopting),
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
        }
    }

    private static ModerationQualificationRepository NativeNarrativePolicyWriter(
        ExploreDbContext context, NativeDatabase database, NativeCaller caller, IPrivacyIdentityFenceAuthority privacy)
    {
        var admin = NarrativeAdmin(context, caller);
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var publications = new ModerationPolicyPublicationRepository(
            context, admin, database.Machine, inventory, null, database.Clock);
        var activation = new ModerationNativeActivationQualification(context, inventory, null,
            new ModerationQualificationPolicySourceOwner(context, database.Clock), database.Clock);
        return new(context, admin, database.Machine, publications, activation, database.Clock, privacy);
    }
}
