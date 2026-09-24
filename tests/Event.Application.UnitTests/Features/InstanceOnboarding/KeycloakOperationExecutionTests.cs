using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.DTOs.Onboarding;
using Explore.Domain.Keycloak;

namespace Event.Application.UnitTests.Features.InstanceOnboarding;

public sealed class KeycloakOperationExecutionTests
{
    [Test]
    public async Task Plan_WhenNativeAndInheritedMappersAreEffective_ReturnsNoOperation()
    {
        KeycloakInspectionSnapshot snapshot = Snapshot(
        [
            Mapper(
                "native-subject",
                KeycloakMapperSemantic.Subject,
                KeycloakMapperOrigin.Native,
                audience: null,
                accessToken: true,
                idToken: true),
            Mapper(
                "scope-audience",
                KeycloakMapperSemantic.Audience,
                KeycloakMapperOrigin.Inherited,
                audience: "event-api",
                accessToken: true,
                idToken: false)
        ]);

        KeycloakChangeSet? plan = new KeycloakOperationService().Plan(snapshot);

        await Assert.That(plan).IsNull();
    }

    [Test]
    public async Task Plan_WhenMapperCollides_FailsClosed()
    {
        KeycloakInspectionSnapshot snapshot = Snapshot(
        [
            new KeycloakEffectiveMapperSnapshot(
                "wrong-subject",
                KeycloakMapperSemantic.Subject,
                KeycloakMapperOrigin.Direct,
                Audience: null,
                AddsToAccessToken: true,
                AddsToIdToken: false,
                IsEffective: false,
                IsConflicting: true)
        ]);

        Assert.Throws<InvalidOperationException>(() =>
            new KeycloakOperationService().Plan(snapshot));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Plan_WhenTargetAudienceFlagsDrift_BindsExactMapperAndFingerprint()
    {
        KeycloakEffectiveMapperSnapshot drifted = Mapper(
            "mapper-42",
            KeycloakMapperSemantic.Audience,
            KeycloakMapperOrigin.Direct,
            audience: "event-api",
            accessToken: false,
            idToken: false);
        KeycloakInspectionSnapshot snapshot = Snapshot(
        [
            Mapper(
                "native-subject",
                KeycloakMapperSemantic.Subject,
                KeycloakMapperOrigin.Native,
                audience: null,
                accessToken: true,
                idToken: true),
            drifted
        ]);

        KeycloakChangeSet plan =
            new KeycloakOperationService().Plan(snapshot)!;
        KeycloakChangeStep step = plan.Steps.Single();

        await Assert.That(step.Kind).IsEqualTo(KeycloakStep.UpdateMapper);
        await Assert.That(step.TargetId).IsEqualTo("mapper-42");
        await Assert.That(step.Precondition)
            .IsEqualTo(KeycloakStepPrecondition.MustMatchFingerprint);
        await Assert.That(step.ExpectedFingerprint)
            .IsEqualTo(KeycloakOperationService.MapperFingerprint(drifted));
    }

    [Test]
    public async Task Plan_WhenDifferentAudienceExists_CreatesWithoutEditingIt()
    {
        KeycloakEffectiveMapperSnapshot unrelated = Mapper(
            "mapper-42",
            KeycloakMapperSemantic.Audience,
            KeycloakMapperOrigin.Direct,
            audience: "operator-api",
            accessToken: true,
            idToken: false);
        KeycloakInspectionSnapshot snapshot = Snapshot(
        [
            Mapper(
                "native-subject",
                KeycloakMapperSemantic.Subject,
                KeycloakMapperOrigin.Native,
                audience: null,
                accessToken: true,
                idToken: true),
            unrelated
        ]);

        KeycloakChangeStep step =
            new KeycloakOperationService().Plan(snapshot)!.Steps.Single();

        await Assert.That(step.Kind).IsEqualTo(KeycloakStep.CreateMapper);
        await Assert.That(step.TargetId).IsNotEqualTo(unrelated.ProviderId);
        await Assert.That(step.Precondition)
            .IsEqualTo(KeycloakStepPrecondition.MustBeAbsent);
    }

    [Test]
    public async Task ComputeDigest_ChangesWhenReviewedProjectionChanges()
    {
        KeycloakOperationService service = new();
        KeycloakChangeSet original = service.Plan(Snapshot([]))!;
        KeycloakInspectionSnapshot changedTarget = new(
            "operators",
            "event-bff",
            "different-api",
            realmExists: true,
            effectiveMappers: []);
        KeycloakChangeSet altered = service.Plan(changedTarget)!;

        await Assert.That(KeycloakOperationService.ComputeDigest(original))
            .IsNotEqualTo(KeycloakOperationService.ComputeDigest(altered));
    }

    [Test]
    public async Task RealmFingerprint_BindsPlannedProviderUuid()
    {
        KeycloakDesiredProjection first =
            KeycloakDesiredProjection.Realm(
                "operators",
                "11111111-1111-7111-8111-111111111111");
        KeycloakDesiredProjection second =
            KeycloakDesiredProjection.Realm(
                "operators",
                "22222222-2222-7222-8222-222222222222");

        await Assert.That(
                KeycloakOperationService
                    .DesiredProvisioningFingerprint(first))
            .IsNotEqualTo(
                KeycloakOperationService
                    .DesiredProvisioningFingerprint(second));
    }

    [Test]
    public async Task Plan_WhenClientIdsConflict_RejectsBeforeCreatingSteps()
    {
        var snapshot = new KeycloakInspectionSnapshot(
            "operators",
            "event-client",
            "EVENT-CLIENT",
            realmExists: true,
            effectiveMappers: []);

        Assert.Throws<InvalidOperationException>(() =>
            new KeycloakOperationService().Plan(snapshot));
        await Task.CompletedTask;
    }

    [Test]
    public async Task PlanCreateRealm_RequiresProvenAbsenceAndOrdersCreateOnlySteps()
    {
        var snapshot = new KeycloakInspectionSnapshot(
            "operators", "event-bff", "event-api", realmExists: false,
            effectiveMappers: [],
            blazorClient: new("event-bff", 0, null, null),
            apiClient: new("event-api", 0, null, null));

        KeycloakChangeSet plan = new KeycloakOperationService().Plan(
            snapshot,
            KeycloakOperationIntent.CreateRealm,
            new Uri("https://event.example.test/"),
            "revision-1")!;

        await Assert.That(plan.Steps.Select(step => step.Kind)).IsEquivalentTo(
        [
            KeycloakStep.CreateRealm,
            KeycloakStep.CreateClient,
            KeycloakStep.CreateClient,
            KeycloakStep.CreateMapper,
            KeycloakStep.CreateMapper
        ]);
        await Assert.That(plan.Steps[1].Desired.RedirectUris.Single())
            .IsEqualTo("https://event.example.test/signin-oidc");
        await Assert.That(plan.Steps[2].Desired.Kind)
            .IsEqualTo(KeycloakDesiredKind.BearerOnlyApiClient);
    }

    [Test]
    public async Task PlanCreateClients_RejectsMixedExistingAndAbsentClients()
    {
        var snapshot = new KeycloakInspectionSnapshot(
            "operators", "event-bff", "event-api", realmExists: true,
            effectiveMappers: [],
            blazorClient: new(
                "event-bff",
                1,
                "bff-uuid",
                new(true, false, false, true, false, false)),
            apiClient: new("event-api", 0, null, null));

        Assert.Throws<InvalidOperationException>(() =>
            new KeycloakOperationService().Plan(
                snapshot,
                KeycloakOperationIntent.CreateClients,
                new Uri("https://event.example.test/"),
                "revision-1"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task PlanCreateRealm_AllowsLoopbackHttpPublicOrigin()
    {
        var snapshot = new KeycloakInspectionSnapshot(
            "operators",
            "event-bff",
            "event-api",
            realmExists: false,
            effectiveMappers: [],
            blazorClient: new(
                "event-bff",
                0,
                ProviderId: null,
                Shape: null),
            apiClient: new(
                "event-api",
                0,
                ProviderId: null,
                Shape: null));

        KeycloakChangeSet plan =
            new KeycloakOperationService().Plan(
                snapshot,
                KeycloakOperationIntent.CreateRealm,
                new Uri("http://localhost:7002/"),
                "revision-1");

        await Assert.That(plan.Steps.Count)
            .IsGreaterThan(0);
    }

    [Test]
    public async Task CreateRealm_AcceptedThenTimeout_StopsRemainingResources()
    {
        var repository = new RecordingRepository();
        var coordinator = new RecordingCoordinator();
        var admin = new RecordingAdminClient(null, null);
        admin.ProvisioningApplyResults.Enqueue(
            new KeycloakProvisioningOperationResult(
                KeycloakStepOutcomeKind.OutcomeUnknown,
                "keycloak_resource_outcome_unknown"));
        var service = new KeycloakOperationService(
            repository,
            coordinator,
            admin);
        var snapshot = new KeycloakInspectionSnapshot(
            "operators",
            "event-bff",
            "event-api",
            realmExists: false,
            effectiveMappers: [],
            blazorClient: new(
                "event-bff",
                0,
                ProviderId: null,
                Shape: null),
            apiClient: new(
                "event-api",
                0,
                ProviderId: null,
                Shape: null));
        Uri origin = new("https://event.example.test/");
        KeycloakOperation operation =
            (await service.CreateAsync(
                Guid.Parse("77777777-7777-7777-8777-777777777777"),
                new Uri(
                    "https://identity.example.test/realms/operators"),
                snapshot,
                "actor",
                7,
                Now,
                Now.AddMinutes(15),
                KeycloakOperationIntent.CreateRealm,
                origin,
                "revision-1"))!;
        var context = new KeycloakOperationApplyContext(
            operation.Id,
            "actor",
            7,
            operation.Target,
            operation.Digest,
            "event-api",
            origin,
            "revision-1",
            "runtime-secret-canary",
            "admin",
            "password",
            Now.AddMinutes(1));

        KeycloakOperation result = await service.ApplyAsync(context);

        await Assert.That(result.State)
            .IsEqualTo(KeycloakOperationState.OutcomeUnknown);
        await Assert.That(admin.ApplyCount).IsEqualTo(1);
        await Assert.That(result.StepOutcomes.Items).HasCount().EqualTo(1);
    }

    [Test]
    public async Task Reconcile_CreateApplyingWithoutOutcome_UsesPlannedIds()
    {
        var repository = new RecordingRepository();
        var coordinator = new RecordingCoordinator();
        var admin = new RecordingAdminClient(null, null);
        var service = new KeycloakOperationService(
            repository,
            coordinator,
            admin);
        var snapshot = new KeycloakInspectionSnapshot(
            "operators",
            "event-bff",
            "event-api",
            realmExists: false,
            effectiveMappers: [],
            blazorClient: new(
                "event-bff",
                0,
                ProviderId: null,
                Shape: null),
            apiClient: new(
                "event-api",
                0,
                ProviderId: null,
                Shape: null));
        Uri origin = new("https://event.example.test/");
        KeycloakOperation operation =
            (await service.CreateAsync(
                Guid.Parse(
                    "77777777-7777-7777-8777-777777777777"),
                new Uri(
                    "https://identity.example.test/realms/operators"),
                snapshot,
                "actor",
                7,
                Now,
                Now.AddMinutes(15),
                KeycloakOperationIntent.CreateRealm,
                origin,
                "revision-1"))!;
        Guid expectedStamp = operation.ConcurrencyStamp;
        operation.AuthorizeApply(
            "actor",
            7,
            operation.Target,
            operation.Digest,
            Now.AddMinutes(1));
        await repository.SaveAsync(
            operation,
            expectedStamp);
        var context = new KeycloakOperationApplyContext(
            operation.Id,
            "actor",
            7,
            operation.Target,
            operation.Digest,
            "event-api",
            origin,
            "revision-1",
            "runtime-secret-canary",
            "admin",
            "password",
            Now.AddMinutes(2));
        string[] plannedIds = operation.ChangeSet.Steps
            .Where(step => step.Kind is
                KeycloakStep.CreateRealm
                or KeycloakStep.CreateClient)
            .Select(step =>
                step.Desired!.ProviderResourceId!)
            .ToArray();

        KeycloakOperation result =
            await service.ReconcileAsync(context);

        await Assert.That(result.State)
            .IsEqualTo(KeycloakOperationState.Verified);
        await Assert.That(admin.ApplyCount).IsEqualTo(0);
        await Assert.That(
                admin.InspectedProvisioningProviderIds)
            .IsEquivalentTo(plannedIds);
    }

    [Test]
    public async Task CreateRealm_WhenCredentialBindingChanges_FailsBeforeProvider()
    {
        var repository = new RecordingRepository();
        var coordinator = new RecordingCoordinator();
        var admin = new RecordingAdminClient(null, null);
        var service = new KeycloakOperationService(
            repository,
            coordinator,
            admin);
        var snapshot = new KeycloakInspectionSnapshot(
            "operators",
            "event-bff",
            "event-api",
            realmExists: false,
            effectiveMappers: [],
            blazorClient: new(
                "event-bff",
                0,
                ProviderId: null,
                Shape: null),
            apiClient: new(
                "event-api",
                0,
                ProviderId: null,
                Shape: null));
        Uri origin = new("https://event.example.test/");
        KeycloakOperation operation =
            (await service.CreateAsync(
                Guid.Parse("77777777-7777-7777-8777-777777777777"),
                new Uri(
                    "https://identity.example.test/realms/operators"),
                snapshot,
                "actor",
                7,
                Now,
                Now.AddMinutes(15),
                KeycloakOperationIntent.CreateRealm,
                origin,
                "revision-1"))!;
        var changedContext = new KeycloakOperationApplyContext(
            operation.Id,
            "actor",
            7,
            operation.Target,
            operation.Digest,
            "event-api",
            origin,
            "revision-2",
            "runtime-secret-canary",
            "admin",
            "password",
            Now.AddMinutes(1));

        await Assert.ThrowsAsync<KeycloakOperationConflictException>(() =>
            service.ApplyAsync(changedContext));

        await Assert.That(admin.ApplyCount).IsEqualTo(0);
        await Assert.That(repository.PersistedState)
            .IsEqualTo(KeycloakOperationState.Previewed);
    }

    [Test]
    public async Task Apply_WhenIntentSaveFails_DoesNotReachProvider()
    {
        TestHarness harness = await TestHarness.CreateAsync(
            twoSteps: false);
        harness.Repository.FailOnSaveAttempt = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.ApplyAsync(harness.Context));

        await Assert.That(harness.Admin.ApplyCount).IsEqualTo(0);
        await Assert.That(harness.Repository.PersistedState)
            .IsEqualTo(KeycloakOperationState.Previewed);
    }

    [Test]
    public async Task Apply_DuplicateReceipt_DoesNotReplayProviderWrite()
    {
        TestHarness harness = await TestHarness.CreateAsync(
            twoSteps: false,
            applyResults:
            [
                Result(KeycloakStepOutcomeKind.Applied)
            ]);
        await harness.Service.ApplyAsync(harness.Context);
        int writesAfterFirstApply = harness.Admin.ApplyCount;

        await Assert.ThrowsAsync<KeycloakOperationConflictException>(() =>
            harness.Service.ApplyAsync(harness.Context));

        await Assert.That(writesAfterFirstApply).IsEqualTo(1);
        await Assert.That(harness.Admin.ApplyCount)
            .IsEqualTo(writesAfterFirstApply);
    }

    [Test]
    public async Task Apply_AcceptedThenTimeout_RemainsOutcomeUnknown()
    {
        TestHarness harness = await TestHarness.CreateAsync(
            twoSteps: false,
            applyResults:
            [
                Result(KeycloakStepOutcomeKind.OutcomeUnknown)
            ]);

        KeycloakOperation result =
            await harness.Service.ApplyAsync(harness.Context);

        await Assert.That(result.State)
            .IsEqualTo(KeycloakOperationState.OutcomeUnknown);
        await Assert.That(result.SettledAtUtc).IsNull();
        await Assert.That(harness.Admin.ApplyCount).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_CallerCancelledAfterSend_PersistsUnknown()
    {
        TestHarness harness =
            await TestHarness.CreateAsync(twoSteps: false);
        using var callerCancellation =
            new CancellationTokenSource();
        harness.Admin.OnApply = _ =>
        {
            callerCancellation.Cancel();
            return Task.FromResult(
                Result(
                    KeycloakStepOutcomeKind.OutcomeUnknown));
        };

        KeycloakOperation result =
            await harness.Service.ApplyAsync(
                harness.Context,
                callerCancellation.Token);

        await Assert.That(result.State)
            .IsEqualTo(
                KeycloakOperationState.OutcomeUnknown);
        await Assert.That(harness.Repository.PersistedState)
            .IsEqualTo(
                KeycloakOperationState.OutcomeUnknown);
        await Assert.That(
                harness.Repository.PersistedOutcomeCount)
            .IsEqualTo(1);
    }

    [Test]
    public async Task Apply_CallerCancelledBeforeSend_SettlesWithoutProvider()
    {
        TestHarness harness =
            await TestHarness.CreateAsync(twoSteps: false);
        using var callerCancellation =
            new CancellationTokenSource();
        harness.Coordinator
            .HonorCancellationBeforeAction = true;
        harness.Repository.OnSaved = operation =>
        {
            if (operation.State
                == KeycloakOperationState.Applying)
            {
                callerCancellation.Cancel();
            }
        };

        KeycloakOperation result =
            await harness.Service.ApplyAsync(
                harness.Context,
                callerCancellation.Token);

        await Assert.That(result.State)
            .IsEqualTo(
                KeycloakOperationState.FailedBeforeWrite);
        await Assert.That(harness.Admin.ApplyCount)
            .IsEqualTo(0);
        await Assert.That(
                result.StepOutcomes.Items.Any(
                    outcome => outcome.Kind
                        != KeycloakStepOutcomeKind
                            .FailedBeforeWrite))
            .IsFalse();
    }

    [Test]
    public async Task Apply_ConflictAfterSuccess_RecordsPartialOutcome()
    {
        TestHarness harness = await TestHarness.CreateAsync(
            twoSteps: true,
            applyResults:
            [
                Result(KeycloakStepOutcomeKind.Applied),
                Result(KeycloakStepOutcomeKind.Conflict)
            ]);

        KeycloakOperation result =
            await harness.Service.ApplyAsync(harness.Context);

        await Assert.That(result.State)
            .IsEqualTo(KeycloakOperationState.PartiallyApplied);
        await Assert.That(result.StepOutcomes.Items.Select(item => item.Kind))
            .IsEquivalentTo(
            [
                KeycloakStepOutcomeKind.Applied,
                KeycloakStepOutcomeKind.Conflict
            ]);
    }

    [Test]
    public async Task Apply_CancellationWhileFirstStepOutstanding_StopsSecondStep()
    {
        TestHarness harness = await TestHarness.CreateAsync(twoSteps: true);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Admin.OnApply = async cancellationToken =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            return Result(KeycloakStepOutcomeKind.Applied);
        };

        Task<KeycloakOperation> apply =
            harness.Service.ApplyAsync(harness.Context);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Guid expectedStamp = harness.Operation.ConcurrencyStamp;
        harness.Operation.RequestCancellation(Now.AddMinutes(2));
        await harness.Repository.SaveAsync(
            harness.Operation,
            expectedStamp);
        release.SetResult();

        KeycloakOperation result =
            await apply.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(harness.Admin.ApplyCount).IsEqualTo(1);
        await Assert.That(result.State)
            .IsEqualTo(KeycloakOperationState.PartiallyApplied);
        await Assert.That(result.StepOutcomes.Items.Any(item =>
                item.Kind == KeycloakStepOutcomeKind.SkippedCancelled))
            .IsTrue();
    }

    [Test]
    public async Task Apply_LocalOutcomeSaveFailure_LeavesDurableIntentApplying()
    {
        TestHarness harness = await TestHarness.CreateAsync(
            twoSteps: false,
            applyResults:
            [
                Result(KeycloakStepOutcomeKind.Applied)
            ]);
        harness.Repository.FailOnSaveAttempt = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.ApplyAsync(harness.Context));

        await Assert.That(harness.Admin.ApplyCount).IsEqualTo(1);
        await Assert.That(harness.Repository.PersistedState)
            .IsEqualTo(KeycloakOperationState.Applying);
        await Assert.That(harness.Repository.PersistedOutcomeCount)
            .IsEqualTo(0);
    }

    [Test]
    public async Task Apply_WhenApiAudienceChanges_FailsBeforeProviderAccess()
    {
        TestHarness harness = await TestHarness.CreateAsync(
            twoSteps: false);
        var changedContext = new KeycloakOperationApplyContext(
            harness.Operation.Id,
            harness.Context.Actor,
            harness.Context.SetupGeneration,
            harness.Context.Target,
            harness.Context.Digest,
            "changed-api",
            harness.Context.PublicOrigin,
            harness.Context.CredentialBindingRevision,
            harness.Context.RuntimeClientSecret,
            harness.Context.AdministratorUsername,
            harness.Context.AdministratorPassword,
            harness.Context.NowUtc);

        await Assert.ThrowsAsync<KeycloakOperationConflictException>(() =>
            harness.Service.ApplyAsync(changedContext));

        await Assert.That(harness.Admin.ApplyCount).IsEqualTo(0);
        await Assert.That(harness.Repository.PersistedState)
            .IsEqualTo(KeycloakOperationState.Previewed);
    }

    [Test]
    public async Task Apply_OverlapSettlesFailedBeforeWriteWithoutProviderCall()
    {
        TestHarness harness = await TestHarness.CreateAsync(
            twoSteps: false);
        harness.Coordinator.ThrowOverlap = true;

        KeycloakOperation result =
            await harness.Service.ApplyAsync(harness.Context);

        await Assert.That(result.State)
            .IsEqualTo(KeycloakOperationState.FailedBeforeWrite);
        await Assert.That(harness.Admin.ApplyCount).IsEqualTo(0);
    }

    [Test]
    public async Task Reconcile_UsesInspectionOnlyAndSettlesUncertainStep()
    {
        TestHarness harness = await TestHarness.CreateAsync(
            twoSteps: false,
            inspectResults:
            [
                Result(KeycloakStepOutcomeKind.Verified)
            ]);
        Guid expectedStamp = harness.Operation.ConcurrencyStamp;
        harness.Operation.AuthorizeApply(
            "actor",
            7,
            harness.Operation.Target,
            harness.Operation.Digest,
            Now.AddMinutes(1));
        harness.Operation.RecordStepOutcome(new KeycloakStepOutcome(
            harness.Operation.ChangeSet.Steps.Single().StepId,
            KeycloakStepOutcomeKind.OutcomeUnknown));
        harness.Operation.MarkOutcomeUnknown();
        await harness.Repository.SaveAsync(
            harness.Operation,
            expectedStamp);

        KeycloakOperation result =
            await harness.Service.ReconcileAsync(harness.Context);

        await Assert.That(result.State)
            .IsEqualTo(KeycloakOperationState.Verified);
        await Assert.That(harness.Admin.ApplyCount).IsEqualTo(0);
        await Assert.That(harness.Admin.InspectCount).IsEqualTo(1);
    }

    [Test]
    public async Task Reconcile_ApplyingUnknownReadback_RemainsRetryable()
    {
        TestHarness harness = await TestHarness.CreateAsync(
            twoSteps: false,
            inspectResults:
            [
                Result(
                    KeycloakStepOutcomeKind.OutcomeUnknown),
                Result(
                    KeycloakStepOutcomeKind.OutcomeUnknown)
            ]);
        Guid expectedStamp =
            harness.Operation.ConcurrencyStamp;
        harness.Operation.AuthorizeApply(
            harness.Context.Actor,
            harness.Context.SetupGeneration,
            harness.Context.Target,
            harness.Context.Digest,
            harness.Context.NowUtc);
        await harness.Repository.SaveAsync(
            harness.Operation,
            expectedStamp);

        KeycloakOperation first =
            await harness.Service.ReconcileAsync(
                harness.Context);
        KeycloakOperation second =
            await harness.Service.ReconcileAsync(
                harness.Context);

        await Assert.That(first.State)
            .IsEqualTo(
                KeycloakOperationState.OutcomeUnknown);
        await Assert.That(second.State)
            .IsEqualTo(
                KeycloakOperationState.OutcomeUnknown);
        await Assert.That(second.SettledAtUtc).IsNull();
        await Assert.That(harness.Admin.ApplyCount)
            .IsEqualTo(0);
        await Assert.That(harness.Admin.InspectCount)
            .IsEqualTo(2);
    }

    [Test]
    public async Task Reconcile_AfterProposalExpiry_RemainsAvailableForUncertainWrite()
    {
        TestHarness harness = await TestHarness.CreateAsync(
            twoSteps: false,
            inspectResults:
            [
                Result(KeycloakStepOutcomeKind.Verified)
            ]);
        Guid expectedStamp = harness.Operation.ConcurrencyStamp;
        harness.Operation.AuthorizeApply(
            harness.Context.Actor,
            harness.Context.SetupGeneration,
            harness.Context.Target,
            harness.Context.Digest,
            harness.Context.NowUtc);
        harness.Operation.RecordStepOutcome(new KeycloakStepOutcome(
            harness.Operation.ChangeSet.Steps.Single().StepId,
            KeycloakStepOutcomeKind.OutcomeUnknown));
        harness.Operation.MarkOutcomeUnknown();
        await harness.Repository.SaveAsync(
            harness.Operation,
            expectedStamp);
        var expiredProposalContext = new KeycloakOperationApplyContext(
            harness.Operation.Id,
            harness.Context.Actor,
            harness.Context.SetupGeneration,
            harness.Context.Target,
            harness.Context.Digest,
            harness.Context.ApiClientId,
            harness.Context.PublicOrigin,
            harness.Context.CredentialBindingRevision,
            harness.Context.RuntimeClientSecret,
            harness.Context.AdministratorUsername,
            harness.Context.AdministratorPassword,
            harness.Operation.ExpiresAtUtc.AddMinutes(1));

        KeycloakOperation result =
            await harness.Service.ReconcileAsync(expiredProposalContext);

        await Assert.That(result.State)
            .IsEqualTo(KeycloakOperationState.Verified);
        await Assert.That(harness.Admin.ApplyCount).IsEqualTo(0);
        await Assert.That(harness.Admin.InspectCount).IsEqualTo(1);
    }

    private static KeycloakInspectionSnapshot Snapshot(
        IReadOnlyList<KeycloakEffectiveMapperSnapshot> mappers) =>
        new(
            "operators",
            "event-bff",
            "event-api",
            realmExists: true,
            mappers);

    private static KeycloakEffectiveMapperSnapshot Mapper(
        string providerId,
        KeycloakMapperSemantic semantic,
        KeycloakMapperOrigin origin,
        string? audience,
        bool accessToken,
        bool idToken) =>
        new(
            providerId,
            semantic,
            origin,
            audience,
            accessToken,
            idToken,
            IsEffective: true);

    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static KeycloakMapperOperationResult Result(
        KeycloakStepOutcomeKind outcome) =>
        new(
            outcome,
            $"result-{outcome}",
            ProviderResourceId: "provider-resource",
            ObservedFingerprint: "observed-fingerprint");

    private sealed class TestHarness
    {
        private TestHarness(
            RecordingRepository repository,
            RecordingCoordinator coordinator,
            RecordingAdminClient admin,
            KeycloakOperationService service,
            KeycloakOperation operation,
            KeycloakOperationApplyContext context)
        {
            Repository = repository;
            Coordinator = coordinator;
            Admin = admin;
            Service = service;
            Operation = operation;
            Context = context;
        }

        public RecordingRepository Repository { get; }
        public RecordingCoordinator Coordinator { get; }
        public RecordingAdminClient Admin { get; }
        public KeycloakOperationService Service { get; }
        public KeycloakOperation Operation { get; }
        public KeycloakOperationApplyContext Context { get; }

        public static async Task<TestHarness> CreateAsync(
            bool twoSteps,
            IEnumerable<KeycloakMapperOperationResult>? applyResults = null,
            IEnumerable<KeycloakMapperOperationResult>? inspectResults = null)
        {
            var repository = new RecordingRepository();
            var coordinator = new RecordingCoordinator();
            var admin = new RecordingAdminClient(
                applyResults,
                inspectResults);
            var service = new KeycloakOperationService(
                repository,
                coordinator,
                admin);
            KeycloakInspectionSnapshot snapshot = twoSteps
                ? Snapshot([])
                : Snapshot(
                [
                    Mapper(
                        "inherited-audience",
                        KeycloakMapperSemantic.Audience,
                        KeycloakMapperOrigin.Inherited,
                        "event-api",
                        accessToken: true,
                        idToken: false)
                ]);
            KeycloakOperation operation =
                (await service.CreateAsync(
                    Guid.Parse("77777777-7777-7777-8777-777777777777"),
                    new Uri(
                        "https://identity.example.test/realms/operators"),
                    snapshot,
                    "actor",
                    7,
                    Now,
                    Now.AddHours(1)))!;
            var context = new KeycloakOperationApplyContext(
                operation.Id,
                "actor",
                7,
                operation.Target,
                operation.Digest,
                "event-api",
                null,
                null,
                null,
                $"admin-{Guid.CreateVersion7():N}",
                $"password-{Guid.CreateVersion7():N}",
                Now.AddMinutes(3));
            return new TestHarness(
                repository,
                coordinator,
                admin,
                service,
                operation,
                context);
        }
    }

    private sealed class RecordingRepository : IKeycloakOperationRepository
    {
        private KeycloakOperation? _operation;
        private Guid _persistedStamp;
        private int _saveAttempts;

        public int? FailOnSaveAttempt { get; set; }

        public Action<KeycloakOperation>? OnSaved { get; set; }

        public KeycloakOperationState? PersistedState { get; private set; }

        public int PersistedOutcomeCount { get; private set; }

        public Task<KeycloakOperation?> GetAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _operation?.Id == id ? _operation : null);
        }

        public Task AddAsync(
            KeycloakOperation operation,
            CancellationToken cancellationToken = default)
        {
            _operation = operation;
            _persistedStamp = operation.ConcurrencyStamp;
            PersistedState = operation.State;
            PersistedOutcomeCount = operation.StepOutcomes.Items.Count;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            KeycloakOperation operation,
            Guid expectedConcurrencyStamp,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _saveAttempts++;
            if (FailOnSaveAttempt == _saveAttempts)
            {
                throw new InvalidOperationException(
                    "Injected local persistence failure.");
            }

            if (expectedConcurrencyStamp != _persistedStamp)
            {
                throw new InvalidOperationException(
                    "Injected optimistic concurrency failure.");
            }

            _operation = operation;
            _persistedStamp = operation.ConcurrencyStamp;
            PersistedState = operation.State;
            PersistedOutcomeCount = operation.StepOutcomes.Items.Count;
            OnSaved?.Invoke(operation);
            return Task.CompletedTask;
        }

        public Task<bool> HasUnresolvedOverlapAsync(
            KeycloakTarget target,
            Guid? excludedOperationId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<KeycloakOperation>>
            FindSettledRetentionEligibleAsync(
                DateTimeOffset beforeUtc,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KeycloakOperation>>([]);

        public Task DeleteAsync(
            KeycloakOperation operation,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingCoordinator : IKeycloakOperationCoordinator
    {
        public bool ThrowOverlap { get; set; }
        public bool HonorCancellationBeforeAction { get; set; }

        public async Task<T> ExecuteAsync<T>(
            KeycloakOperation operation,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOverlap)
            {
                throw new KeycloakOperationOverlapException();
            }

            if (HonorCancellationBeforeAction)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await action(cancellationToken);
        }

        public Task<T> ExecuteReconciliationAsync<T>(
            KeycloakOperation operation,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);

        public Task RequestCancellationAsync(
            Guid operationId,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingAdminClient(
        IEnumerable<KeycloakMapperOperationResult>? applyResults,
        IEnumerable<KeycloakMapperOperationResult>? inspectResults)
        : IKeycloakAdminClient
    {
        private readonly Queue<KeycloakMapperOperationResult> _apply =
            new(applyResults ?? []);
        private readonly Queue<KeycloakMapperOperationResult> _inspect =
            new(inspectResults ?? []);

        public int ApplyCount { get; private set; }
        public int InspectCount { get; private set; }
        public Queue<KeycloakProvisioningOperationResult>
            ProvisioningApplyResults
        {
            get;
        } = new();
        public List<string?>
            InspectedProvisioningProviderIds
        {
            get;
        } = [];
        public Func<CancellationToken, Task<KeycloakMapperOperationResult>>?
            OnApply
        {
            get;
            set;
        }

        public Task<KeycloakAdminInspectionResult> InspectAsync(
            KeycloakAdminInspectionRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Unexpected provider inspection.");

        public async Task<KeycloakMapperOperationResult>
            ApplyApprovedMapperAsync(
                KeycloakMapperOperationRequest request,
                CancellationToken cancellationToken)
        {
            ApplyCount++;
            if (OnApply is not null)
            {
                return await OnApply(cancellationToken);
            }

            return _apply.Count > 0
                ? _apply.Dequeue()
                : Result(KeycloakStepOutcomeKind.Applied);
        }

        public Task<KeycloakMapperOperationResult>
            InspectApprovedMapperAsync(
                KeycloakMapperOperationRequest request,
                CancellationToken cancellationToken)
        {
            InspectCount++;
            return Task.FromResult(
                _inspect.Count > 0
                    ? _inspect.Dequeue()
                    : Result(KeycloakStepOutcomeKind.Verified));
        }

        public Task<KeycloakProvisioningOperationResult>
            ApplyApprovedProvisioningAsync(
                KeycloakProvisioningOperationRequest request,
                CancellationToken cancellationToken)
        {
            ApplyCount++;
            return Task.FromResult(
                ProvisioningApplyResults.Count > 0
                    ? ProvisioningApplyResults.Dequeue()
                    : new KeycloakProvisioningOperationResult(
                        KeycloakStepOutcomeKind.Applied,
                        "applied",
                        request.ProviderResourceId));
        }

        public Task<KeycloakProvisioningOperationResult>
            InspectApprovedProvisioningAsync(
                KeycloakProvisioningOperationRequest request,
                CancellationToken cancellationToken)
        {
            InspectCount++;
            InspectedProvisioningProviderIds.Add(
                request.ProviderResourceId);
            return Task.FromResult(
                new KeycloakProvisioningOperationResult(
                    KeycloakStepOutcomeKind.Verified,
                    "verified",
                    request.ProviderResourceId));
        }
    }
}
