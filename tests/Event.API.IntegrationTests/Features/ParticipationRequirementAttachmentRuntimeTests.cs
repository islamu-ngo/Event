using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.RegistrationForms.Handlers.Commands;
using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<ParticipationRequirementAttachmentRuntimeFixture>(Shared = SharedType.PerClass)]
[NotInParallel("RealRuntimeDb")]
public sealed class ParticipationRequirementAttachmentRuntimeTests(
    ParticipationRequirementAttachmentRuntimeFixture fixture)
{
    [Test]
    public async Task RealRuntimeHostKeepsProductionMediatREfAndFallbackServices()
    {
        RuntimeServiceGraph graph = await fixture.GetServiceGraphAsync();

        await Assert.That(graph.DatabaseProvider).IsEqualTo("Npgsql.EntityFrameworkCore.PostgreSQL");
        await Assert.That(graph.AttachHandler).IsEqualTo(typeof(AttachRegistrationRequirementCommandHandler));
        await Assert.That(graph.Repository).IsEqualTo(typeof(ParticipationRequirementAttachmentRepository));
        await Assert.That(graph.UnitOfWork).IsEqualTo(typeof(EfCoreUnitOfWork));
        await Assert.That(graph.AuthorizationProvider).IsEqualTo(typeof(FallbackAuthorizationService));
        await Assert.That(graph.Mediator.Namespace).IsEqualTo("MediatR");
    }

    [Test]
    public async Task RealPipelineKeepsAuthorityConcurrencyDescriptorAndDetachNarrative()
    {
        AttachmentRuntimeScenario scenario = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            nativeChannel: true);

        using HttpResponseMessage unauthenticated = await fixture.AttachAsync(
            scenario, null, scenario.ConfigurationStamp);
        await Assert.That(unauthenticated.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        using HttpResponseMessage unauthorized = await fixture.AttachAsync(
            scenario, scenario.ContributorUserId, scenario.ConfigurationStamp);
        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        using HttpResponseMessage published = await fixture.PublishAsync(scenario);
        await Assert.That(published.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using HttpResponseMessage weakIfMatch = await fixture.AttachWithIfMatchAsync(
            scenario, scenario.OrganizerUserId, $"W/\"{scenario.ConfigurationStamp:D}\"");
        await Assert.That(weakIfMatch.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        using HttpResponseMessage missingIfMatch = await fixture.AttachWithIfMatchAsync(
            scenario, scenario.OrganizerUserId, null);
        await Assert.That(missingIfMatch.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        using HttpResponseMessage attached = await fixture.AttachAsync(
            scenario, scenario.OrganizerUserId, scenario.ConfigurationStamp);
        await Assert.That(attached.StatusCode).IsEqualTo(HttpStatusCode.OK);

        Guid attachedStamp = await fixture.GetConfigurationStampAsync(scenario);
        using HttpResponseMessage duplicate = await fixture.AttachAsync(
            scenario, scenario.OrganizerUserId, attachedStamp);
        await AssertProblemAsync(
            duplicate,
            HttpStatusCode.BadRequest,
            "registration_requirement_already_attached");

        using HttpResponseMessage stale = await fixture.AttachAsync(
            scenario, scenario.OrganizerUserId, scenario.ConfigurationStamp);
        await AssertProblemAsync(
            stale,
            HttpStatusCode.Conflict,
            "registration_requirement_concurrency_conflict");

        using HttpResponseMessage questionnaire = await fixture.GetOptionalQuestionnaireAsync(scenario);
        using JsonDocument questionnaireDocument = await ReadJsonAsync(questionnaire);
        JsonElement questionnaireRoot = questionnaireDocument.RootElement;
        await Assert.That(questionnaire.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(questionnaireRoot.GetProperty("registrationRequirementId").GetGuid())
            .IsEqualTo(scenario.RequirementId);
        await Assert.That(questionnaireRoot.GetProperty("schemaHash").GetString()).IsNotNullOrEmpty();
        await Assert.That(questionnaireRoot.GetProperty("dataSchemaArtifact").GetString()).IsNotNullOrEmpty();
        await Assert.That(questionnaireRoot.GetProperty("uiSchemaArtifact").GetString()).IsNotNullOrEmpty();
        await Assert.That(questionnaireRoot.GetProperty("logicSchemaArtifact").GetString()).IsNotNullOrEmpty();
        await Assert.That(questionnaireRoot.GetProperty("mappingArtifact").GetString()).IsNotNullOrEmpty();
        await Assert.That(questionnaireRoot.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
        await Assert.That(questionnaireRoot.GetProperty("_links").TryGetProperty("event", out _)).IsTrue();
        await AssertEventQuestionnaireLinkAsync(await fixture.GetEventAsync(scenario), expected: true);

        using HttpResponseMessage detached = await fixture.DetachAsync(scenario, attachedStamp);
        await Assert.That(detached.StatusCode).IsEqualTo(HttpStatusCode.OK);
        Guid detachedStamp = await fixture.GetConfigurationStampAsync(scenario);

        using HttpResponseMessage detachedAgain = await fixture.DetachAsync(scenario, detachedStamp);
        await Assert.That(detachedAgain.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await fixture.GetConfigurationStampAsync(scenario)).IsEqualTo(detachedStamp);

        using HttpResponseMessage missing = await fixture.GetOptionalQuestionnaireAsync(scenario);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await fixture.AssertNoRegistrationSideEffectsAsync(scenario, expectedActiveAttachments: 0);
    }

    [Test]
    public async Task InformationOnlyAcceptsNoRegistrationEffectWithoutQuestionnaireDisclosure()
    {
        AttachmentRuntimeScenario scenario = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.InformationOnly,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            nativeChannel: true);

        using HttpResponseMessage attached = await fixture.AttachAsync(
            scenario,
            scenario.OrganizerUserId,
            scenario.ConfigurationStamp,
            standaloneQuestionnaire: false);
        await Assert.That(attached.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using HttpResponseMessage questionnaire = await fixture.GetOptionalQuestionnaireAsync(scenario);
        await Assert.That(questionnaire.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await AssertEventQuestionnaireLinkAsync(await fixture.GetEventAsync(scenario), expected: false);
        await fixture.AssertNoRegistrationSideEffectsAsync(scenario, expectedActiveAttachments: 1);
    }

    [Test]
    public async Task OptionalQuestionnaire_DirectGetDoesNotDiscloseWhenTheEventIsNoLongerPublic()
    {
        AttachmentRuntimeScenario scenario = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            nativeChannel: true);
        using HttpResponseMessage published = await fixture.PublishAsync(scenario);
        await Assert.That(published.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using HttpResponseMessage attached = await fixture.AttachAsync(
            scenario, scenario.OrganizerUserId, scenario.ConfigurationStamp);
        await Assert.That(attached.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await fixture.CancelEventAsync(scenario);

        using HttpResponseMessage questionnaire = await fixture.GetOptionalQuestionnaireAsync(scenario);
        await Assert.That(questionnaire.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task WalkInRejectsBlockingAndRequiresNativeForStandaloneQuestionnaire()
    {
        AttachmentRuntimeScenario blocking = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            nativeChannel: true);
        using HttpResponseMessage blockingPublished = await fixture.PublishAsync(blocking);
        await Assert.That(blockingPublished.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using HttpResponseMessage blockingRejected = await fixture.AttachAsync(
            blocking, blocking.OrganizerUserId, blocking.ConfigurationStamp);
        await AssertProblemAsync(
            blockingRejected,
            HttpStatusCode.BadRequest,
            "registration_requirement_mode_invalid");
        await fixture.AssertNoRegistrationSideEffectsAsync(blocking, expectedActiveAttachments: 0);

        AttachmentRuntimeScenario nonNative = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            nativeChannel: false);
        using HttpResponseMessage nonNativePublished = await fixture.PublishAsync(nonNative);
        await Assert.That(nonNativePublished.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using HttpResponseMessage nonNativeRejected = await fixture.AttachAsync(
            nonNative, nonNative.OrganizerUserId, nonNative.ConfigurationStamp);
        await AssertProblemAsync(
            nonNativeRejected,
            HttpStatusCode.BadRequest,
            "registration_requirement_mode_invalid");
        await fixture.AssertNoRegistrationSideEffectsAsync(nonNative, expectedActiveAttachments: 0);
    }

    [Test]
    public async Task ExternalManagedRejectsNativeAndBlockingRequirements()
    {
        AttachmentRuntimeScenario native = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.ExternalManaged,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            nativeChannel: true);
        using HttpResponseMessage nativeRejected = await fixture.AttachAsync(
            native,
            native.OrganizerUserId,
            native.ConfigurationStamp,
            standaloneQuestionnaire: false);
        await AssertProblemAsync(
            nativeRejected,
            HttpStatusCode.BadRequest,
            "registration_requirement_mode_invalid");
        await fixture.AssertNoRegistrationSideEffectsAsync(native, expectedActiveAttachments: 0);

        AttachmentRuntimeScenario blocking = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.ExternalManaged,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            nativeChannel: false);
        using HttpResponseMessage blockingRejected = await fixture.AttachAsync(
            blocking,
            blocking.OrganizerUserId,
            blocking.ConfigurationStamp,
            standaloneQuestionnaire: false);
        await AssertProblemAsync(
            blockingRejected,
            HttpStatusCode.BadRequest,
            "registration_requirement_mode_invalid");
        await fixture.AssertNoRegistrationSideEffectsAsync(blocking, expectedActiveAttachments: 0);
    }

    [Test]
    public async Task PlatformManagedAcceptsBlockingNativeRequirementOnlyAsNonStandaloneAttachment()
    {
        AttachmentRuntimeScenario scenario = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.PlatformManaged,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            nativeChannel: true);

        using HttpResponseMessage attached = await fixture.AttachAsync(
            scenario,
            scenario.OrganizerUserId,
            scenario.ConfigurationStamp,
            standaloneQuestionnaire: false);
        await Assert.That(attached.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using HttpResponseMessage questionnaire = await fixture.GetOptionalQuestionnaireAsync(scenario);
        await Assert.That(questionnaire.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await AssertEventQuestionnaireLinkAsync(await fixture.GetEventAsync(scenario), expected: false);
        await fixture.AssertNoRegistrationSideEffectsAsync(scenario, expectedActiveAttachments: 1);
    }

    [Test]
    public async Task IllegalPatchReconfigureReturnsProblemAndRollsBackConfigurationAndAttachment()
    {
        AttachmentRuntimeScenario scenario = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            nativeChannel: true);
        using HttpResponseMessage published = await fixture.PublishAsync(scenario);
        await Assert.That(published.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using HttpResponseMessage attached = await fixture.AttachAsync(
            scenario, scenario.OrganizerUserId, scenario.ConfigurationStamp);
        await Assert.That(attached.StatusCode).IsEqualTo(HttpStatusCode.OK);

        string before = await fixture.GetPersistenceFingerprintAsync(scenario);
        Guid attachedStamp = await fixture.GetConfigurationStampAsync(scenario);
        using HttpResponseMessage illegal = await fixture.ConfigureAsync(
            scenario,
            attachedStamp,
            ParticipationHandlingModeEnum.PlatformManaged,
            AdvanceRegistrationObligationEnum.Required,
            IdentityAccessModeEnum.AccountRequired);
        await AssertProblemAsync(
            illegal,
            HttpStatusCode.BadRequest,
            "event_participation_configuration_attachment_conflict");
        await Assert.That(await fixture.GetPersistenceFingerprintAsync(scenario)).IsEqualTo(before);
        await fixture.AssertNoRegistrationSideEffectsAsync(scenario, expectedActiveAttachments: 1);

        using HttpResponseMessage stale = await fixture.ConfigureAsync(
            scenario,
            scenario.ConfigurationStamp,
            ParticipationHandlingModeEnum.WalkIn,
            AdvanceRegistrationObligationEnum.NotApplicable,
            null);
        await AssertProblemAsync(
            stale,
            HttpStatusCode.Conflict,
            "event_participation_configuration_concurrency_conflict");
        await Assert.That(await fixture.GetPersistenceFingerprintAsync(scenario)).IsEqualTo(before);
        await fixture.AssertNoRegistrationSideEffectsAsync(scenario, expectedActiveAttachments: 1);
    }

    [Test]
    public async Task HttpAttachRejectsCrossEventAndCrossTenantIdentifierCombinationsWithoutLeaks()
    {
        AttachmentRuntimeScenario primary = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            nativeChannel: true);
        AttachmentRuntimeScenario otherEvent = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            nativeChannel: true);
        AttachmentRuntimeScenario otherTenant = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            nativeChannel: true,
            secondaryTenant: true);
        using HttpResponseMessage primaryPublished = await fixture.PublishAsync(primary);
        using HttpResponseMessage otherEventPublished = await fixture.PublishAsync(otherEvent);
        await Assert.That(primaryPublished.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(otherEventPublished.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string before = await fixture.GetPersistenceFingerprintAsync(primary);

        using HttpResponseMessage foreignWorkflow = await fixture.AttachAsync(
            primary,
            primary.OrganizerUserId,
            primary.ConfigurationStamp,
            workflowId: otherEvent.WorkflowId,
            requirementId: otherEvent.RequirementId);
        await AssertNonleakingNotFoundAsync(foreignWorkflow, otherEvent.WorkflowId, otherEvent.RequirementId);
        await fixture.AssertRejectedStateAsync(primary, before);

        using HttpResponseMessage foreignRequirement = await fixture.AttachAsync(
            primary,
            primary.OrganizerUserId,
            primary.ConfigurationStamp,
            requirementId: otherEvent.RequirementId);
        await AssertNonleakingNotFoundAsync(foreignRequirement, otherEvent.RequirementId);
        await fixture.AssertRejectedStateAsync(primary, before);

        using HttpResponseMessage foreignForm = await fixture.AttachAsync(
            primary,
            primary.OrganizerUserId,
            primary.ConfigurationStamp,
            formId: otherEvent.FormId,
            versionId: otherEvent.VersionId);
        await AssertNonleakingNotFoundAsync(foreignForm, otherEvent.FormId, otherEvent.VersionId);
        await fixture.AssertRejectedStateAsync(primary, before);

        using HttpResponseMessage mismatchedVersion = await fixture.AttachAsync(
            primary,
            primary.OrganizerUserId,
            primary.ConfigurationStamp,
            formId: primary.FormId,
            versionId: otherEvent.VersionId);
        await AssertNonleakingNotFoundAsync(mismatchedVersion, otherEvent.VersionId);
        await fixture.AssertRejectedStateAsync(primary, before);

        using HttpResponseMessage foreignTenantGraph = await fixture.AttachAsync(
            primary,
            primary.OrganizerUserId,
            primary.ConfigurationStamp,
            workflowId: otherTenant.WorkflowId,
            requirementId: otherTenant.RequirementId,
            formId: otherTenant.FormId,
            versionId: otherTenant.VersionId);
        await AssertNonleakingNotFoundAsync(
            foreignTenantGraph,
            otherTenant.TenantId,
            otherTenant.WorkflowId,
            otherTenant.RequirementId,
            otherTenant.FormId,
            otherTenant.VersionId);
        await fixture.AssertRejectedStateAsync(primary, before);

        using HttpResponseMessage foreignTenantRoute = await fixture.AttachAsync(
            otherTenant,
            primary.OrganizerUserId,
            otherTenant.ConfigurationStamp);
        await Assert.That(foreignTenantRoute.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using JsonDocument foreignTenantProblem = await ReadJsonAsync(foreignTenantRoute);
        string foreignTenantDisclosure = ProblemSemanticDisclosure(foreignTenantProblem.RootElement);
        await Assert.That(foreignTenantDisclosure).DoesNotContain(otherTenant.TenantId.ToString("D"));
        await Assert.That(foreignTenantDisclosure).DoesNotContain(otherTenant.RequirementId.ToString("D"));
        await fixture.AssertNoRegistrationSideEffectsAsync(otherTenant, expectedActiveAttachments: 0);
        await fixture.AssertRejectedStateAsync(primary, before);
    }

    [Test]
    public async Task PostgreSqlFilteredIndexesEnforceRequirementAndStandaloneUniqueness()
    {
        AttachmentRuntimeScenario requirementUnique = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.PlatformManaged,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            nativeChannel: true);
        using HttpResponseMessage attached = await fixture.AttachAsync(
            requirementUnique,
            requirementUnique.OrganizerUserId,
            requirementUnique.ConfigurationStamp,
            standaloneQuestionnaire: false);
        await Assert.That(attached.StatusCode).IsEqualTo(HttpStatusCode.OK);
        Guid requirementStamp = await fixture.GetConfigurationStampAsync(requirementUnique);
        using HttpResponseMessage httpDuplicate = await fixture.AttachAsync(
            requirementUnique,
            requirementUnique.OrganizerUserId,
            requirementStamp,
            standaloneQuestionnaire: false);
        await AssertProblemAsync(
            httpDuplicate,
            HttpStatusCode.BadRequest,
            "registration_requirement_already_attached");
        await fixture.AssertDatabaseUniquenessAsync(
            requirementUnique,
            requirementUnique.RequirementId,
            standaloneQuestionnaire: false);
        await fixture.AssertNoRegistrationSideEffectsAsync(requirementUnique, expectedActiveAttachments: 1);

        AttachmentRuntimeScenario standaloneUnique = await fixture.SeedAsync(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            nativeChannel: true);
        using HttpResponseMessage standalonePublished = await fixture.PublishAsync(standaloneUnique);
        await Assert.That(standalonePublished.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using HttpResponseMessage standaloneAttached = await fixture.AttachAsync(
            standaloneUnique,
            standaloneUnique.OrganizerUserId,
            standaloneUnique.ConfigurationStamp);
        await Assert.That(standaloneAttached.StatusCode).IsEqualTo(HttpStatusCode.OK);
        Guid standaloneStamp = await fixture.GetConfigurationStampAsync(standaloneUnique);
        using HttpResponseMessage secondStandalone = await fixture.AttachAsync(
            standaloneUnique,
            standaloneUnique.OrganizerUserId,
            standaloneStamp,
            requirementId: standaloneUnique.SecondaryRequirementId);
        await AssertProblemAsync(
            secondStandalone,
            HttpStatusCode.BadRequest,
            "registration_requirement_mode_invalid");
        await fixture.AssertDatabaseUniquenessAsync(
            standaloneUnique,
            standaloneUnique.SecondaryRequirementId,
            standaloneQuestionnaire: true);
        await fixture.AssertNoRegistrationSideEffectsAsync(standaloneUnique, expectedActiveAttachments: 1);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        using JsonDocument problem = await ReadJsonAsync(response);
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await Assert.That(response.Content.Headers.ContentType?.MediaType is
            "application/problem+json" or "application/json").IsTrue();
        await Assert.That(problem.RootElement.TryGetProperty("type", out _)).IsTrue();
        await Assert.That(problem.RootElement.TryGetProperty("title", out _)).IsTrue();
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)status);
        await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo(code);
    }

    private static async Task AssertNonleakingNotFoundAsync(
        HttpResponseMessage response,
        params Guid[] foreignIds)
    {
        using JsonDocument problem = await ReadJsonAsync(response);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Content.Headers.ContentType?.MediaType is
            "application/problem+json" or "application/json").IsTrue();
        await Assert.That(problem.RootElement.TryGetProperty("type", out _)).IsTrue();
        await Assert.That(problem.RootElement.TryGetProperty("title", out _)).IsTrue();
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(404);
        await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo("resource_not_found");
        string disclosure = ProblemSemanticDisclosure(problem.RootElement);
        foreach (Guid foreignId in foreignIds)
        {
            await Assert.That(disclosure).DoesNotContain(foreignId.ToString("D"));
        }
    }

    private static string ProblemSemanticDisclosure(JsonElement problem)
    {
        var values = new List<string?>
        {
            problem.TryGetProperty("title", out JsonElement title) ? title.GetString() : null,
            problem.TryGetProperty("detail", out JsonElement detail) ? detail.GetString() : null,
            problem.TryGetProperty("code", out JsonElement code) ? code.GetString() : null,
            problem.TryGetProperty("errors", out JsonElement errors) ? errors.GetRawText() : null
        };
        return string.Join('|', values.Where(value => value is not null));
    }

    private static async Task AssertEventQuestionnaireLinkAsync(
        HttpResponseMessage response,
        bool expected)
    {
        using (response)
        using (JsonDocument document = await ReadJsonAsync(response))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            bool hasLink = document.RootElement.GetProperty("_links")
                .TryGetProperty("optional-questionnaire", out _);
            await Assert.That(hasLink).IsEqualTo(expected);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}

public sealed class ParticipationRequirementAttachmentRuntimeFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("participation_requirement_attachments_http")
        .WithUsername("postgres")
        .WithPassword(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))
        .Build();

    private PostgreSqlApiWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using (var context = CreateDbContext())
        {
            await context.Database.EnsureCreatedAsync();
            await PostgresModelConstraintApplier.ApplyAsync(context);
            await LookupTableSeeder.SeedAsync(context);
        }

        _factory = new PostgreSqlApiWebApplicationFactory(
            _container.GetConnectionString(),
            new Dictionary<string, string?>
            {
                ["Testing:HostProfile"] = TestHostProfile.RealRuntime,
                ["RateLimiting:DisableInTesting"] = "true",
                ["Logging:LogLevel:Default"] = "Critical"
            },
            services =>
            {
                services.RemoveAll<IAuthorizationProvider>();
                services.AddScoped<IAuthorizationProvider, FallbackAuthorizationService>();
            });
        _client = _factory.CreateClient();
    }

    public async Task<AttachmentRuntimeScenario> SeedAsync(
        ParticipationHandlingModeEnum mode,
        RegistrationRequirementCompletionEffectEnum completionEffect,
        bool nativeChannel,
        bool secondaryTenant = false)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        TenantScenarioSeed.TenantScenarioResult organizer = secondaryTenant
            ? await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(context, "Requirement isolation tenant")
            : await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        TenantScenarioSeed.TenantScenarioResult contributor = secondaryTenant
            ? organizer
            : await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);

        var @event = new EventBuilder()
            .WithTitle($"{mode} requirement attachment {Guid.CreateVersion7():N}")
            .WithActorId(contributor.ActorId)
            .WithTenantId(organizer.TenantId)
            .WithStatus(EventStatusEnum.Published)
            .WithVisibility(VisibilityTypeEnum.Public)
            .Build();
        @event.EventProvenanceTypeId = 1;
        @event.OrganizerActorId = organizer.ActorId;

        (AdvanceRegistrationObligationEnum obligation, IdentityAccessModeEnum? identity) = mode switch
        {
            ParticipationHandlingModeEnum.InformationOnly or ParticipationHandlingModeEnum.WalkIn =>
                (AdvanceRegistrationObligationEnum.NotApplicable, (IdentityAccessModeEnum?)null),
            ParticipationHandlingModeEnum.ExternalManaged =>
                (AdvanceRegistrationObligationEnum.Required, (IdentityAccessModeEnum?)null),
            ParticipationHandlingModeEnum.PlatformManaged =>
                (AdvanceRegistrationObligationEnum.Required, IdentityAccessModeEnum.AccountRequired),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        EventParticipationConfiguration configuration = @event.ParticipationConfiguration!;
        configuration.Reconfigure((int)mode, (int)obligation, (int?)identity, null);

        DateTime now = DateTime.UtcNow;
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(
            organizer.TenantId, @event.Id, "registration", now);
        RegistrationRequirement requirement = Requirement(
            workflow, sequence: 1, completionEffect, nativeChannel, now);
        RegistrationRequirement secondaryRequirement = Requirement(
            workflow, sequence: 2, completionEffect, nativeChannel, now);
        workflow.AddRequirement(requirement);
        workflow.AddRequirement(secondaryRequirement);

        RegistrationForm form = RegistrationForm.Create(
            organizer.TenantId,
            @event.Id,
            "platform.registration",
            $"optional-{Guid.CreateVersion7():N}",
            "Optional questionnaire",
            now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        RegistrationFormSection section = RegistrationFormSection.Create(
            Guid.CreateVersion7(), version, 1, "Questions", now);
        RegistrationFormField field = RegistrationFormField.Create(
            Guid.CreateVersion7(), section, 1, "platform.registration", "email", "Email",
            RegistrationFieldTypeEnum.Email, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, now);
        version.AddSection(section);
        version.AddField(section, field);
        form.AddVersion(version);

        context.AddRange(@event, workflow, form);
        await context.SaveChangesAsync();
        return new(
            organizer.TenantId,
            organizer.UserId,
            contributor.UserId,
            @event.Id,
            configuration.ConcurrencyStamp,
            workflow.Id,
            requirement.Id,
            secondaryRequirement.Id,
            form.Id,
            version.Id,
            version.ConcurrencyStamp);
    }

    public async Task<HttpResponseMessage> PublishAsync(AttachmentRuntimeScenario scenario)
    {
        using var request = Authenticated(
            HttpMethod.Post,
            $"/api/events/{scenario.EventId:D}/registration-forms/{scenario.FormId:D}/versions/{scenario.VersionId:D}/publish",
            scenario.OrganizerUserId);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{scenario.VersionStamp:D}\"");
        return await _client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> ConfigureAsync(
        AttachmentRuntimeScenario scenario,
        Guid stamp,
        ParticipationHandlingModeEnum mode,
        AdvanceRegistrationObligationEnum obligation,
        IdentityAccessModeEnum? identity)
    {
        using var request = Authenticated(
            HttpMethod.Patch,
            $"/api/events/{scenario.EventId:D}/participation",
            scenario.OrganizerUserId);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        request.Content = JsonContent.Create(new
        {
            participationHandlingModeId = (int)mode,
            advanceRegistrationObligationId = (int)obligation,
            identityAccessModeId = (int?)identity,
            guestRecoveryPolicy = (string?)null
        });
        return await _client.SendAsync(request);
    }

    public Task<HttpResponseMessage> AttachAsync(
        AttachmentRuntimeScenario scenario,
        Guid? userId,
        Guid stamp,
        bool standaloneQuestionnaire = true,
        Guid? workflowId = null,
        Guid? requirementId = null,
        Guid? formId = null,
        Guid? versionId = null) => AttachWithIfMatchAsync(
            scenario,
            userId,
            $"\"{stamp:D}\"",
            standaloneQuestionnaire,
            workflowId,
            requirementId,
            formId,
            versionId);

    public async Task<HttpResponseMessage> AttachWithIfMatchAsync(
        AttachmentRuntimeScenario scenario,
        Guid? userId,
        string? ifMatch,
        bool standaloneQuestionnaire = true,
        Guid? workflowId = null,
        Guid? requirementId = null,
        Guid? formId = null,
        Guid? versionId = null)
    {
        HttpRequestMessage request = userId.HasValue
            ? Authenticated(
                HttpMethod.Post,
                RequirementRoute(scenario.EventId, requirementId ?? scenario.RequirementId),
                userId.Value)
            : new HttpRequestMessage(
                HttpMethod.Post,
                RequirementRoute(scenario.EventId, requirementId ?? scenario.RequirementId));
        using (request)
        {
            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }

            request.Content = JsonContent.Create(new
            {
                workflowId = workflowId ?? scenario.WorkflowId,
                standaloneQuestionnaire,
                registrationFormId = standaloneQuestionnaire ? formId ?? scenario.FormId : formId,
                registrationFormVersionId = standaloneQuestionnaire ? versionId ?? scenario.VersionId : versionId
            });
            return await _client.SendAsync(request);
        }
    }

    public async Task<HttpResponseMessage> DetachAsync(AttachmentRuntimeScenario scenario, Guid stamp)
    {
        using var request = Authenticated(
            HttpMethod.Delete,
            RequirementRoute(scenario.EventId, scenario.RequirementId),
            scenario.OrganizerUserId);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        return await _client.SendAsync(request);
    }

    public Task<HttpResponseMessage> GetOptionalQuestionnaireAsync(AttachmentRuntimeScenario scenario) =>
        _client.GetAsync($"/api/events/{scenario.EventId:D}/participation/optional-questionnaire");

    public Task<HttpResponseMessage> GetEventAsync(AttachmentRuntimeScenario scenario) =>
        _client.GetAsync($"/api/Event/{scenario.EventId:D}");

    public async Task CancelEventAsync(AttachmentRuntimeScenario scenario)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Explore.Domain.Event @event = await context.Events.IgnoreQueryFilters().SingleAsync(value =>
            value.Id == scenario.EventId && value.TenantId == scenario.TenantId);
        @event.Cancel(DateTime.UtcNow);
        await context.SaveChangesAsync();
    }

    public async Task<Guid> GetConfigurationStampAsync(AttachmentRuntimeScenario scenario)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return await context.EventParticipationConfigurations.IgnoreQueryFilters()
            .Where(value => value.Id == scenario.EventId && value.TenantId == scenario.TenantId)
            .Select(value => value.ConcurrencyStamp)
            .SingleAsync();
    }

    public async Task<string> GetPersistenceFingerprintAsync(AttachmentRuntimeScenario scenario)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        EventParticipationConfiguration configuration = await context.EventParticipationConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(value => value.Id == scenario.EventId && value.TenantId == scenario.TenantId);
        string attachments = string.Join(",", await context.ParticipationRequirementAttachments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(value => value.ParticipationConfigurationId == scenario.EventId && !value.IsDeleted)
            .OrderBy(value => value.Id)
            .Select(value => $"{value.Id:D}:{value.RegistrationRequirementId:D}:{value.IsStandaloneQuestionnaire}")
            .ToListAsync());
        return $"{configuration.ParticipationHandlingModeId}|{configuration.AdvanceRegistrationObligationId}|" +
            $"{configuration.IdentityAccessModeId}|{configuration.GuestRecoveryPolicy}|" +
            $"{configuration.ConcurrencyStamp:D}|{attachments}";
    }

    public async Task AssertRejectedStateAsync(
        AttachmentRuntimeScenario scenario,
        string expectedFingerprint)
    {
        await Assert.That(await GetPersistenceFingerprintAsync(scenario)).IsEqualTo(expectedFingerprint);
        await AssertNoRegistrationSideEffectsAsync(scenario, expectedActiveAttachments: 0);
    }

    public async Task AssertNoRegistrationSideEffectsAsync(
        AttachmentRuntimeScenario scenario,
        int expectedActiveAttachments)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await context.RegistrationOrders.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.RegistrationParticipants.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.EventRegistrations.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ParticipationRequirementAttachments.IgnoreQueryFilters()
            .CountAsync(value =>
                value.ParticipationConfigurationId == scenario.EventId && !value.IsDeleted))
            .IsEqualTo(expectedActiveAttachments);
    }

    public async Task AssertDatabaseUniquenessAsync(
        AttachmentRuntimeScenario scenario,
        Guid requirementId,
        bool standaloneQuestionnaire)
    {
        await using ExploreDbContext context = CreateDbContext();
        string[] expectedProperties = standaloneQuestionnaire
            ? [nameof(ParticipationRequirementAttachment.ParticipationConfigurationId),
                nameof(ParticipationRequirementAttachment.IsStandaloneQuestionnaire)]
            : [nameof(ParticipationRequirementAttachment.ParticipationConfigurationId),
                nameof(ParticipationRequirementAttachment.RegistrationRequirementId)];
        string expectedConstraint = context.Model
            .FindEntityType(typeof(ParticipationRequirementAttachment))!
            .GetIndexes()
            .Single(index => index.IsUnique &&
                index.Properties.Select(property => property.Name).SequenceEqual(expectedProperties))
            .GetDatabaseName();
        await using var transaction = await context.Database.BeginTransactionAsync();
        PostgresException? violation = null;
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "islamu_event".participation_requirement_attachments (
                    id, tenant_id, event_id, participation_configuration_id,
                    registration_workflow_id, registration_requirement_id,
                    registration_form_id, registration_form_version_id,
                    is_standalone_questionnaire, created_at, created_by,
                    updated_at, updated_by, is_deleted, deleted_at, deleted_by,
                    concurrency_stamp)
                SELECT {{Guid.CreateVersion7()}}, tenant_id, event_id, participation_configuration_id,
                    registration_workflow_id, {{requirementId}},
                    registration_form_id, registration_form_version_id,
                    is_standalone_questionnaire, created_at, created_by,
                    updated_at, updated_by, is_deleted, deleted_at, deleted_by,
                    {{Guid.CreateVersion7()}}
                FROM "islamu_event".participation_requirement_attachments
                WHERE participation_configuration_id = {{scenario.EventId}}
                    AND is_deleted = false
                LIMIT 1
                """);
        }
        catch (PostgresException exception)
        {
            violation = exception;
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
        await Assert.That(violation.ConstraintName).IsEqualTo(expectedConstraint);
    }

    public async Task<RuntimeServiceGraph> GetServiceGraphAsync()
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        ExploreDbContext context = services.GetRequiredService<ExploreDbContext>();
        return new(
            context.Database.ProviderName,
            services.GetRequiredService<IMediator>().GetType(),
            services.GetRequiredService<IRequestHandler<
                AttachRegistrationRequirementCommand,
                Explore.Application.Responses.BaseCommandResponse<Guid>>>().GetType(),
            services.GetRequiredService<IParticipationRequirementAttachmentRepository>().GetType(),
            services.GetRequiredService<IUnitOfWork>().GetType(),
            services.GetRequiredService<IAuthorizationProvider>().GetType());
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private ExploreDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ExploreDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);

    private static RegistrationRequirement Requirement(
        RegistrationWorkflow workflow,
        int sequence,
        RegistrationRequirementCompletionEffectEnum completionEffect,
        bool nativeChannel,
        DateTime now)
    {
        (RegistrationRequirementCriticalityEnum criticality, bool canSkip) = completionEffect switch
        {
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration =>
                (RegistrationRequirementCriticalityEnum.Required, false),
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration =>
                (RegistrationRequirementCriticalityEnum.Optional, true),
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect =>
                (RegistrationRequirementCriticalityEnum.Informational, true),
            _ => throw new ArgumentOutOfRangeException(nameof(completionEffect))
        };
        RegistrationRequirement requirement = RegistrationRequirement.Create(
            workflow,
            sequence,
            criticality,
            canSkip,
            completionEffect,
            RegistrationAnswerSyncModeEnum.NONE,
            RegistrationRequirementSubjectTypeEnum.AllOrders,
            null,
            now);
        if (nativeChannel)
        {
            requirement.AddChannel(RegistrationChannel.Create(requirement, 1, true, null, now));
        }
        return requirement;
    }

    private static string RequirementRoute(Guid eventId, Guid requirementId) =>
        $"/api/events/{eventId:D}/participation/requirements/{requirementId:D}";

    private static HttpRequestMessage Authenticated(HttpMethod method, string route, Guid userId)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Requirement attachment runtime"));
        return request;
    }
}

public sealed record AttachmentRuntimeScenario(
    Guid TenantId,
    Guid OrganizerUserId,
    Guid ContributorUserId,
    Guid EventId,
    Guid ConfigurationStamp,
    Guid WorkflowId,
    Guid RequirementId,
    Guid SecondaryRequirementId,
    Guid FormId,
    Guid VersionId,
    Guid VersionStamp);

public sealed record RuntimeServiceGraph(
    string? DatabaseProvider,
    Type Mediator,
    Type AttachHandler,
    Type Repository,
    Type UnitOfWork,
    Type AuthorizationProvider);
