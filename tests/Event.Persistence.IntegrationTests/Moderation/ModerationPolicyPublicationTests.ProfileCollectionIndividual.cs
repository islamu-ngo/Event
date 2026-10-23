using System.Data;
using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    public enum IndividualAcquisitionChange
    {
        None, ProviderIdentity, Machine, Anonymous, DeletedHuman, ErasedHuman,
        WrongProfile, WrongMode, MissingActiveSelection, DraftOnly,
        MissingProcedureReceipt, MissingParentReceipt, ParentWithdrawn, OwnerRoleLost,
        ReviewerRoleLost, ControllerDeleted, LatestUnadoptedSource,
        HumanSubstitution, HumanDeletedAfterAcquisition, HumanErasedAfterAcquisition,
        MachineAfterAcquisition, TenantDeactivated, ProviderBindingSubstitution, ProviderLoginReplacement,
        ProviderPrincipalSubstitution, TenantSubstitution, TrustSubstitution, HostSubstitution,
        EpochChanged, PolicyUnselected, ProcedureReceiptChanged, ParentReceiptChanged,
        ParentChanged, ProcedureChanged, ExactExpiry, ClockRegression, Disposed,
        NoTransaction, ArbitraryTransaction, ForeignContext, CompletedTransaction
    }

    [Test]
    [Arguments(IndividualAcquisitionChange.None)]
    [Arguments(IndividualAcquisitionChange.ProviderIdentity)]
    [Arguments(IndividualAcquisitionChange.Machine)]
    [Arguments(IndividualAcquisitionChange.Anonymous)]
    [Arguments(IndividualAcquisitionChange.DeletedHuman)]
    [Arguments(IndividualAcquisitionChange.ErasedHuman)]
    [Arguments(IndividualAcquisitionChange.WrongProfile)]
    [Arguments(IndividualAcquisitionChange.WrongMode)]
    [Arguments(IndividualAcquisitionChange.MissingActiveSelection)]
    [Arguments(IndividualAcquisitionChange.DraftOnly)]
    [Arguments(IndividualAcquisitionChange.MissingProcedureReceipt)]
    [Arguments(IndividualAcquisitionChange.MissingParentReceipt)]
    [Arguments(IndividualAcquisitionChange.ParentWithdrawn)]
    [Arguments(IndividualAcquisitionChange.OwnerRoleLost)]
    [Arguments(IndividualAcquisitionChange.ReviewerRoleLost)]
    [Arguments(IndividualAcquisitionChange.ControllerDeleted)]
    [Arguments(IndividualAcquisitionChange.LatestUnadoptedSource)]
    [Arguments(IndividualAcquisitionChange.HumanSubstitution)]
    [Arguments(IndividualAcquisitionChange.HumanDeletedAfterAcquisition)]
    [Arguments(IndividualAcquisitionChange.HumanErasedAfterAcquisition)]
    [Arguments(IndividualAcquisitionChange.MachineAfterAcquisition)]
    [Arguments(IndividualAcquisitionChange.TenantDeactivated)]
    [Arguments(IndividualAcquisitionChange.ProviderBindingSubstitution)]
    [Arguments(IndividualAcquisitionChange.ProviderLoginReplacement)]
    [Arguments(IndividualAcquisitionChange.ProviderPrincipalSubstitution)]
    [Arguments(IndividualAcquisitionChange.TenantSubstitution)]
    [Arguments(IndividualAcquisitionChange.TrustSubstitution)]
    [Arguments(IndividualAcquisitionChange.HostSubstitution)]
    [Arguments(IndividualAcquisitionChange.EpochChanged)]
    [Arguments(IndividualAcquisitionChange.PolicyUnselected)]
    [Arguments(IndividualAcquisitionChange.ProcedureReceiptChanged)]
    [Arguments(IndividualAcquisitionChange.ParentReceiptChanged)]
    [Arguments(IndividualAcquisitionChange.ParentChanged)]
    [Arguments(IndividualAcquisitionChange.ProcedureChanged)]
    [Arguments(IndividualAcquisitionChange.ExactExpiry)]
    [Arguments(IndividualAcquisitionChange.ClockRegression)]
    [Arguments(IndividualAcquisitionChange.Disposed)]
    [Arguments(IndividualAcquisitionChange.NoTransaction)]
    [Arguments(IndividualAcquisitionChange.ArbitraryTransaction)]
    [Arguments(IndividualAcquisitionChange.ForeignContext)]
    [Arguments(IndividualAcquisitionChange.CompletedTransaction)]
    public async Task ProfileIndividual_BorrowsOnlyOriginalHumanPolicyEvidence(IndividualAcquisitionChange change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var context = fixture.Context;
        var provider = change is IndividualAcquisitionChange.ProviderIdentity
            or IndividualAcquisitionChange.ProviderBindingSubstitution
            or IndividualAcquisitionChange.ProviderLoginReplacement
            or IndividualAcquisitionChange.ProviderPrincipalSubstitution;
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext
        {
            User = provider ? fixture.ProviderPrincipal() : IndividualPrincipal(fixture.SubjectId)
        }};
        var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
            new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        if (provider)
        {
            await Assert.That(admin.UserId).IsNull();
            await Assert.That(await admin.ResolveUserIdAsync()).IsEqualTo(fixture.SubjectId);
        }
        Task<ModerationProfileCollectionIndividualAcquisition?> Acquire(ExploreDbContext primary) =>
            ModerationProfileCollectionIndividualAcquisition.AcquireAsync(primary, admin, machine, http,
                fixture.Privacy, fixture.Database.Inventory, fixture.Clock,
                change == IndividualAcquisitionChange.WrongProfile ? Guid.CreateVersion7() : fixture.Age.ProfileId,
                change == IndividualAcquisitionChange.WrongMode ? ModerationNativeMode.RemoteStrict : fixture.Age.Mode,
                default);

        if (change == IndividualAcquisitionChange.NoTransaction)
        {
            await Assert.That(await Acquire(context)).IsNull();
            return;
        }
        if (change == IndividualAcquisitionChange.ArbitraryTransaction)
        {
            await using var arbitrary = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.That(await Acquire(context)).IsNull();
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(arbitrary);
            return;
        }
        if (change == IndividualAcquisitionChange.Machine)
            machine.SetPrincipal(new("profile-machine", fixture.Database.TenantId,
                ExternalApiKeyOwnerType.User, fixture.SubjectId, []));
        if (change == IndividualAcquisitionChange.Anonymous)
            http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        if (change == IndividualAcquisitionChange.DeletedHuman)
            await context.Users.Where(row => row.Id == fixture.SubjectId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
        if (change == IndividualAcquisitionChange.ErasedHuman)
        {
            await using var authority = new EmbeddedPrivacyErasureAuthorityDbContext(fixture.PrivacyOptions);
            authority.ErasureIntents.Add(PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
                PrivacyErasureSubjectKind.User, fixture.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest,
                1, Now.UtcDateTime, Now.UtcDateTime));
            await authority.SaveChangesAsync();
        }
        if (change is IndividualAcquisitionChange.MissingActiveSelection or IndividualAcquisitionChange.DraftOnly)
            await context.ModerationPolicySelections.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ActivePublicationId, (Guid?)null));
        if (change == IndividualAcquisitionChange.DraftOnly)
            await context.ModerationPolicyPublications.Where(row => row.Id == fixture.Age.PublicationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedActive, false));
        if (change is IndividualAcquisitionChange.MissingProcedureReceipt or IndividualAcquisitionChange.MissingParentReceipt)
        {
            var receipt = change == IndividualAcquisitionChange.MissingProcedureReceipt
                ? fixture.Adopt.OperationId : fixture.ParentAdoption.OperationId;
            await context.ModerationQualificationMutationReceipts.Where(row => row.Id == receipt).ExecuteDeleteAsync();
        }
        if (change == IndividualAcquisitionChange.ParentWithdrawn)
            await Assert.That((await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(new WithdrawModerationChildAgePolicyAdoption
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Database.Scope, ExpectedQualificationRevision = 6,
                AdoptionId = fixture.ParentAdoption.AdoptionId, ExpectedAdoptionRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change is IndividualAcquisitionChange.OwnerRoleLost or IndividualAcquisitionChange.ReviewerRoleLost)
        {
            var human = change == IndividualAcquisitionChange.OwnerRoleLost ? fixture.OwnerId : fixture.ReviewerId;
            await context.PlatformUserRoles.Where(row => row.UserId == human).ExecuteDeleteAsync();
        }
        if (change == IndividualAcquisitionChange.ControllerDeleted)
            await context.Users.Where(row => row.Id == fixture.Database.AdminId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
        if (change == IndividualAcquisitionChange.LatestUnadoptedSource)
            await Assert.That((await fixture.Writer(fixture.OwnerId).ExecuteAsync(fixture.Record with
            {
                OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = 6
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);

        ModerationProfileCollectionIndividualAcquisition? retained = null;
        var expectedAcquisition = change is IndividualAcquisitionChange.None or IndividualAcquisitionChange.ProviderIdentity
            or >= IndividualAcquisitionChange.HumanSubstitution and <= IndividualAcquisitionChange.Disposed
            or IndividualAcquisitionChange.CompletedTransaction;
        try
        {
            await fixture.Privacy.ExecuteEnrollmentSerializedAsync(async token =>
            {
                var originalTransaction = context.Database.CurrentTransaction!;
                var originalConnection = context.Database.GetDbConnection();
                await Assert.That(context.IdentityFenceOwnsTransaction).IsTrue();
                await Assert.That(await new ModerationQualificationAuthority(context, admin, machine)
                    .AcquireAsync(fixture.Database.Scope, fixture.Age.PublicationId, token)).IsNull();
                await using var foreign = fixture.Database.Context();
                retained = await Acquire(change == IndividualAcquisitionChange.ForeignContext ? foreign : context);
                await Assert.That(retained is not null).IsEqualTo(expectedAcquisition);
                if (retained is null) return true;
                await Assert.That(retained.SubjectId).IsEqualTo(fixture.SubjectId);
                await Assert.That(retained.Procedure.Purpose).IsEqualTo(ModerationIndividualPurpose.ProfileCompletion);
                await Assert.That(retained.SourceId).IsEqualTo(fixture.Record.SourceId);
                await Assert.That(retained.AdoptionId).IsEqualTo(fixture.Adopt.AdoptionId);
                await Assert.That(retained.ObservedAt).IsEqualTo(Now);
                await Assert.That(retained.ValidUntil).IsEqualTo(fixture.Adopt.ValidUntil);
                await Assert.That(await retained.RevalidateAsync(token)).IsTrue();
                switch (change)
                {
                    case IndividualAcquisitionChange.HumanSubstitution:
                        http.HttpContext.User = IndividualPrincipal(fixture.OtherSubjectId);
                        break;
                    case IndividualAcquisitionChange.HumanDeletedAfterAcquisition:
                        await context.Users.Where(row => row.Id == fixture.SubjectId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true), token);
                        break;
                    case IndividualAcquisitionChange.HumanErasedAfterAcquisition:
                        await using (var authority = new EmbeddedPrivacyErasureAuthorityDbContext(fixture.PrivacyOptions))
                        {
                            await authority.Database.UseTransactionAsync(originalTransaction.GetDbTransaction(), token);
                            authority.ErasureIntents.Add(PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
                                PrivacyErasureSubjectKind.User, fixture.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest,
                                1, Now.UtcDateTime, Now.UtcDateTime));
                            await authority.SaveChangesAsync(token);
                        }
                        break;
                    case IndividualAcquisitionChange.MachineAfterAcquisition:
                        machine.SetPrincipal(new("profile-machine", fixture.Database.TenantId,
                            ExternalApiKeyOwnerType.User, fixture.SubjectId, []));
                        break;
                    case IndividualAcquisitionChange.TenantDeactivated:
                        var inactive = await context.TenantStatuses.FirstAsync(row => !row.IsActiveState, token);
                        await context.Tenants.Where(row => row.Id == fixture.Database.TenantId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TenantStatusId, inactive.Id), token);
                        break;
                    case IndividualAcquisitionChange.ProviderBindingSubstitution:
                        await context.UserExternalLogins.Where(row => row.Id == fixture.LoginId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UserId, fixture.OtherSubjectId), token);
                        break;
                    case IndividualAcquisitionChange.ProviderLoginReplacement:
                        await context.UserExternalLogins.Where(row => row.Id == fixture.LoginId).ExecuteDeleteAsync(token);
                        context.UserExternalLogins.Add(fixture.Login(Guid.CreateVersion7()));
                        await context.SaveChangesAsync(token);
                        break;
                    case IndividualAcquisitionChange.ProviderPrincipalSubstitution:
                        http.HttpContext.User = IndividualPrincipal(fixture.SubjectId);
                        break;
                    case IndividualAcquisitionChange.TenantSubstitution:
                        context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
                        break;
                    case IndividualAcquisitionChange.TrustSubstitution:
                        context.ModerationTrustDomainId = Guid.CreateVersion7();
                        break;
                    case IndividualAcquisitionChange.HostSubstitution:
                        await context.ModerationHostBindings.ExecuteUpdateAsync(
                            setters => setters.SetProperty(row => row.InstanceId, Guid.CreateVersion7()), token);
                        break;
                    case IndividualAcquisitionChange.EpochChanged:
                        await context.ModerationQualificationEpochs.ExecuteUpdateAsync(
                            setters => setters.SetProperty(row => row.Revision, 7), token);
                        break;
                    case IndividualAcquisitionChange.PolicyUnselected:
                        await context.ModerationPolicySelections.ExecuteUpdateAsync(
                            setters => setters.SetProperty(row => row.ActivePublicationId, (Guid?)null), token);
                        break;
                    case IndividualAcquisitionChange.ProcedureReceiptChanged:
                    case IndividualAcquisitionChange.ParentReceiptChanged:
                        var receipt = change == IndividualAcquisitionChange.ProcedureReceiptChanged
                            ? fixture.Adopt.OperationId : fixture.ParentAdoption.OperationId;
                        await context.ModerationQualificationMutationReceipts.Where(row => row.Id == receipt)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RequestDigest, new string('a', 64)), token);
                        break;
                    case IndividualAcquisitionChange.ParentChanged:
                        await context.Set<ModerationChildAgePolicySource>().Where(row => row.Id == fixture.Age.SourceId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DefinitionId, Guid.CreateVersion7()), token);
                        break;
                    case IndividualAcquisitionChange.ProcedureChanged:
                        await context.Set<ModerationProfileCollectionProcedureSource>().Where(row => row.Id == fixture.Record.SourceId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Procedure, ProcedureVocabulary(true)), token);
                        break;
                    case IndividualAcquisitionChange.ExactExpiry:
                        fixture.Clock.UtcNow = retained.ValidUntil;
                        break;
                    case IndividualAcquisitionChange.ClockRegression:
                        fixture.Clock.UtcNow = retained.ObservedAt.AddTicks(-1);
                        break;
                    case IndividualAcquisitionChange.Disposed:
                        retained.Dispose();
                        break;
                    default:
                        fixture.Clock.UtcNow = Now.AddMinutes(1);
                        break;
                }
                var staysCurrent = change is IndividualAcquisitionChange.None or IndividualAcquisitionChange.ProviderIdentity
                    or IndividualAcquisitionChange.CompletedTransaction;
                await Assert.That(await retained.RevalidateAsync(token)).IsEqualTo(staysCurrent);
                await Assert.That(retained.ObservedAt).IsEqualTo(Now);
                await Assert.That(retained.ValidUntil).IsEqualTo(fixture.Adopt.ValidUntil);
                await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(originalTransaction);
                await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(originalConnection);
                return true;
            }, async token =>
            {
                // The reader survives through the real outer identity owner's final primary hook.
                if (change is IndividualAcquisitionChange.None or IndividualAcquisitionChange.ProviderIdentity
                    or IndividualAcquisitionChange.CompletedTransaction)
                    await Assert.That(await retained!.RevalidateAsync(token)).IsTrue();
            }, default);
            if (retained is not null)
                await Assert.That(await retained.RevalidateAsync(default)).IsFalse();
        }
        finally { retained?.Dispose(); }
    }

    private static ClaimsPrincipal IndividualPrincipal(Guid human) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, human.ToString())], "individual-policy-witness"));

    private sealed class IndividualPolicyFixture : IAsyncDisposable
    {
        public required Database Database { get; init; }
        public required ExploreDbContext Context { get; init; }
        public required EmbeddedPrivacyErasureAuthorityRepository Privacy { get; init; }
        public required DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> PrivacyOptions { get; init; }
        public required DeploymentClock Clock { get; init; }
        public required RecordModerationChildAgePolicySource Age { get; init; }
        public required AdoptModerationChildAgePolicySource ParentAdoption { get; init; }
        public required RecordModerationProfileCollectionProcedureSource Record { get; init; }
        public required AdoptModerationProfileCollectionProcedureSource Adopt { get; init; }
        public required Guid SubjectId { get; init; }
        public required Guid OtherSubjectId { get; init; }
        public required Guid OwnerId { get; init; }
        public required Guid ReviewerId { get; init; }
        public required Guid LoginId { get; init; }
        private const string Issuer = "https://profile-policy.example.invalid";
        private const string ProviderSubject = "existing-profile-human";

        public ClaimsPrincipal ProviderPrincipal() => new(new ClaimsIdentity(
            [new Claim("sub", ProviderSubject), new Claim("iss", Issuer)], "individual-provider-witness"));

        public UserExternalLogin Login(Guid id)
        {
            var account = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(Issuer, ProviderSubject);
            return new() { Id = id, UserId = SubjectId, User = null!,
                AuthenticationProviderId = (int)account.ProviderKind, AuthenticationProvider = null!, ProviderKey = account.Value };
        }

        public ModerationQualificationRepository Writer(Guid human)
        {
            var (admin, machine) = DeploymentIdentity(Context, human);
            return new(Context, admin, machine,
                new ModerationPolicyPublicationRepository(Context, admin, machine, Database.Inventory, null, Clock),
                new ModerationNativeActivationQualification(Context, Database.Inventory, null, null, Clock),
                Clock, Privacy, deploymentInventory: Database.Inventory);
        }

        public static async Task<IndividualPolicyFixture> CreateAsync(
            params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        {
            var database = await Database.CreateAsync(implementedInventory: true, physicalFile: true);
            var context = database.Context(interceptors);
            var clock = new DeploymentClock();
            var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(context.Database.GetDbConnection()).UseSnakeCaseNamingConvention().Options;
            await using (var schema = new EmbeddedPrivacyErasureAuthorityDbContext(options))
                await schema.Database.ExecuteSqlRawAsync(schema.Database.GenerateCreateScript());
            var privacy = new EmbeddedPrivacyErasureAuthorityRepository(new ProcedureAuthorityFactory(options),
                clock, Options.Create(new PrivacyErasureOptions()), applicationContext: context);
            var request = database.Request(ModerationReferenceProfileId.CFG28);
            var published = await database.Writer(context).PublishAsync(request);
            await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            await SeedSelectedActiveReadPreconditionAsync(context, request, published.Publication!, database);
            var owner = Guid.CreateVersion7();
            var reviewer = Guid.CreateVersion7();
            var subject = Guid.CreateVersion7();
            var other = Guid.CreateVersion7();
            var role = await context.Roles.SingleAsync(row =>
                row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
            foreach (var id in new[] { owner, reviewer, subject, other })
            {
                var human = new User { Id = id, Pii = new UserPii
                { UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Profile", LastName = "Witness" } };
                context.Users.Add(human);
                if (id == owner || id == reviewer)
                    context.PlatformUserRoles.Add(new PlatformUserRole
                    { Id = Guid.CreateVersion7(), UserId = id, User = human, RoleId = role.Id, Role = role });
            }
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var age = new RecordModerationChildAgePolicySource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
                SourceId = Guid.CreateVersion7(), PolicyId = request.Proposed.PolicyId, ExpectedPolicyRevision = 1,
                PublicationId = request.Proposed.PublicationId, ProfileId = request.Proposed.Profiles.Single().Id,
                Mode = ModerationNativeMode.Embedded, ValidUntil = Now.AddMinutes(8), Terms = ChildAgeTerms(),
                Ownership = new(reviewer, reviewer, owner, owner, database.AdminId,
                    "Distinct controllers.", "Independent privacy mandate.", "Child-risk mandate.", "Collection owner mandate.")
            };
            var parent = new AdoptModerationChildAgePolicySource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3,
                SourceId = age.SourceId, ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = 3,
                AdoptionId = Guid.CreateVersion7(), ValidUntil = Now.AddMinutes(7)
            };
            var record = new RecordModerationProfileCollectionProcedureSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 4,
                SourceId = Guid.CreateVersion7(), ChildAgeSourceId = age.SourceId,
                ExpectedChildAgeSourceRevision = 1, ExpectedChildAgeSourceWriterGeneration = 3,
                ChildAgeAdoptionId = parent.AdoptionId, ExpectedChildAgeAdoptionRevision = 1,
                ExpectedChildAgeAdoptionWriterGeneration = 4, Procedure = ProcedureVocabulary(), ValidUntil = Now.AddMinutes(6)
            };
            var adopt = new AdoptModerationProfileCollectionProcedureSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 5,
                SourceId = record.SourceId, ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = 5,
                AdoptionId = Guid.CreateVersion7(), ValidUntil = Now.AddMinutes(5)
            };
            var fixture = new IndividualPolicyFixture
            {
                Database = database, Context = context, Clock = clock, Privacy = privacy, PrivacyOptions = options,
                Age = age, ParentAdoption = parent, Record = record, Adopt = adopt,
                SubjectId = subject, OtherSubjectId = other, OwnerId = owner, ReviewerId = reviewer, LoginId = Guid.CreateVersion7()
            };
            context.UserExternalLogins.Add(fixture.Login(fixture.LoginId));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await Assert.That((await fixture.Writer(database.AdminId).ExecuteAsync(age)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await fixture.Writer(reviewer).ExecuteAsync(parent)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await fixture.Writer(owner).ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await fixture.Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            context.ChangeTracker.Clear();
            return fixture;
        }

        /// <summary>
        /// A declared persisted read-unit precondition, NOT a publication activation witness.
        /// The native Draft writer supplies the complete immutable graph. Only its historical
        /// selected ACTIVE state and retained provenance are seeded here. These rows are never
        /// returned as native observations, conformance grants or current operation permission.
        /// Child-age/procedure sources and receipts are subsequently created by their real writers.
        /// </summary>
        private static async Task SeedSelectedActiveReadPreconditionAsync(ExploreDbContext context,
            ModerationPolicyPublicationRequest request, ModerationPolicyPublication publication, Database database)
        {
            var active = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active };
            var draft = ModerationPolicyPublicationProjection.Restore(publication, database.Inventory)!;
            var proposed = ModerationPolicyPublicationProjection.CanonicalProposed(active);
            var obligations = active.Modes.Select(mode =>
                new ModerationPolicyPublicationObligation(draft.Publication, mode, draft.Bounds, proposed)).ToList();
            foreach (var requirement in draft.Prerequisites)
            {
                obligations.AddRange(requirement.ActivationGates.Select(gate =>
                    new ModerationPolicyPublicationObligation(draft.Publication, requirement.Mode, draft.Bounds,
                        proposed, requirement with { ActivationGates = [gate], NativePrerequisites = [] })));
                obligations.AddRange(requirement.NativePrerequisites.Select(prerequisite =>
                    new ModerationPolicyPublicationObligation(draft.Publication, requirement.Mode, draft.Bounds,
                        proposed, requirement with { ActivationGates = [], NativePrerequisites = [prerequisite] })));
            }
            obligations.AddRange(draft.Effects.Select(effect => new ModerationPolicyPublicationObligation(
                draft.Publication, effect.Mode, draft.Bounds, proposed, Effect: effect)));
            obligations.AddRange(draft.Consequences.Select(consequence => new ModerationPolicyPublicationObligation(
                draft.Publication, consequence.Mode, draft.Bounds, proposed, Consequence: consequence)));
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            foreach (var obligation in obligations)
                context.Set<ModerationPolicyPublicationEvidence>().Add(new()
                {
                    PublicationId = publication.Id, RequirementDigest = ModerationPolicyPublicationProjection.Digest(obligation),
                    ApprovalId = Guid.CreateVersion7(), ApprovalRevision = 1, SourceId = Guid.CreateVersion7(),
                    SourceRevision = 1, WriterGeneration = "persisted-read-precondition",
                    ObservedAt = Now, ValidUntil = Now.AddMinutes(10)
                });
            foreach (var requirement in draft.Consumers)
                foreach (var consumer in requirement.Consumers)
                    context.Set<ModerationPolicyPublicationEvidence>().Add(new()
                    {
                        PublicationId = publication.Id, RequirementDigest = ModerationPolicyPublicationProjection.Digest(new
                        { requirement.Binding, ConsumerId = consumer.Id, draft.Proposed.InventoryFingerprint }),
                        ApprovalId = null, ApprovalRevision = null, SourceId = Guid.CreateVersion7(), SourceRevision = 1,
                        WriterGeneration = "persisted-read-precondition", ObservedAt = Now, ValidUntil = Now.AddMinutes(10)
                    });
            await context.SaveChangesAsync();
            await context.ModerationPolicyPublications.Where(row => row.Id == publication.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.PublishedActive, true)
                .SetProperty(row => row.RequestDigest, ModerationPolicyPublicationProjection.Digest(request with { Proposed = active }))
                .SetProperty(row => row.DeclaredStateDigest, ModerationPolicyPublicationProjection.ProposedDigest(active))
                .SetProperty(row => row.EvidenceValidUntil, Now.AddMinutes(10)));
            await context.ModerationPolicySelections.Where(row => row.Id == publication.PolicyId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActivePublicationId, publication.Id));
            context.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
