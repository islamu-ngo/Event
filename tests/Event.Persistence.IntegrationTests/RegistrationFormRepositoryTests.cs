// ABOUTME: Verifies registration authoring repository graph loading, isolation, tracking, and concurrency translation.
// ABOUTME: Covers exact event ownership, default tenant filters, and persistence DI composition without filter bypasses.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class RegistrationFormRepositoryTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Registration_UsesConcreteRepositoryInPersistenceComposition()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        services.ConfigurePersistenceServices(configuration, skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);

        ServiceDescriptor descriptor = services.Single(value =>
            value.ServiceType == typeof(IRegistrationFormAuthoringRepository));
        await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(RegistrationFormAuthoringRepository));
        await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
    }

    [Test]
    public async Task Reads_AreDetachedAndMutationLoadsTrackCompleteEventOwnedGraphs()
    {
        string database = $"registration-authoring-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        RegistrationWorkflow workflow = CreateWorkflow(tenantId, eventId);
        RegistrationForm form = CreateForm(tenantId, eventId);

        await using (ExploreDbContext seed = CreateContext(database, root, tenantId))
        {
            seed.AddRange(workflow, form);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateContext(database, root, tenantId);
        var repository = new RegistrationFormAuthoringRepository(context);

        RegistrationWorkflow? readWorkflow = await repository.GetWorkflowAsync(eventId, workflow.Purpose, default);
        IReadOnlyList<RegistrationForm> readForms = await repository.GetFormsAsync(eventId, default);
        RegistrationForm? readForm = await repository.GetFormAsync(eventId, form.Id, default);
        RegistrationFormVersion expectedVersion = form.Versions.Single();
        RegistrationFormVersion? readVersion = await repository.GetVersionAsync(eventId, form.Id, expectedVersion.Id, default);

        await Assert.That(readWorkflow).IsNotNull();
        await Assert.That(readWorkflow!.Requirements.Single().Channels).HasSingleItem();
        await Assert.That(readForms).HasSingleItem();
        await Assert.That(readForms.Single().Versions).HasSingleItem();
        await Assert.That(readForm).IsNotNull();
        await Assert.That(readForm!.Versions.Single().Sections.Single().Fields.Single().Options).HasSingleItem();
        await Assert.That(readVersion).IsNotNull();
        await Assert.That(readVersion!.Sections.Single().Fields.Single().Options).HasSingleItem();
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();

        await Assert.That(await repository.GetWorkflowAsync(Guid.CreateVersion7(), workflow.Purpose, default)).IsNull();
        await Assert.That(await repository.GetFormsAsync(Guid.CreateVersion7(), default)).IsEmpty();
        await Assert.That(await repository.GetFormAsync(Guid.CreateVersion7(), form.Id, default)).IsNull();
        await Assert.That(await repository.GetVersionAsync(eventId, Guid.CreateVersion7(), expectedVersion.Id, default)).IsNull();

        RegistrationWorkflow? trackedWorkflow = await repository.GetWorkflowForUpdateAsync(eventId, workflow.Id, default);
        RegistrationForm? trackedForm = await repository.GetFormForUpdateAsync(eventId, form.Id, default);
        RegistrationFormVersion? trackedVersion = await repository.GetVersionForUpdateAsync(
            eventId, form.Id, expectedVersion.Id, default);

        await Assert.That(trackedWorkflow).IsNotNull();
        await Assert.That(trackedForm).IsNotNull();
        await Assert.That(trackedVersion).IsNotNull();
        await Assert.That(context.Entry(trackedWorkflow!).State).IsEqualTo(EntityState.Unchanged);
        await Assert.That(context.Entry(trackedForm!).State).IsEqualTo(EntityState.Unchanged);
        await Assert.That(context.Entry(trackedVersion!).State).IsEqualTo(EntityState.Unchanged);

        await using ExploreDbContext otherTenant = CreateContext(database, root, Guid.CreateVersion7());
        var otherTenantRepository = new RegistrationFormAuthoringRepository(otherTenant);
        await Assert.That(await otherTenantRepository.GetFormsAsync(eventId, default)).IsEmpty();
        await Assert.That(await otherTenantRepository.GetWorkflowForUpdateAsync(eventId, workflow.Id, default)).IsNull();
        await Assert.That(await otherTenantRepository.GetFormForUpdateAsync(eventId, form.Id, default)).IsNull();
    }

    [Test]
    public async Task UpdateWorkflow_TranslatesStaleConcurrencyToApplicationConflict()
    {
        string database = $"registration-concurrency-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        RegistrationWorkflow workflow = CreateWorkflow(tenantId, eventId);
        await using (ExploreDbContext seed = CreateContext(database, root, tenantId))
        {
            seed.Add(workflow);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext firstContext = CreateContext(database, root, tenantId);
        await using ExploreDbContext staleContext = CreateContext(database, root, tenantId);
        var firstRepository = new RegistrationFormAuthoringRepository(firstContext);
        var staleRepository = new RegistrationFormAuthoringRepository(staleContext);
        RegistrationWorkflow first = (await firstRepository.GetWorkflowForUpdateAsync(eventId, workflow.Id, default))!;
        RegistrationWorkflow stale = (await staleRepository.GetWorkflowForUpdateAsync(eventId, workflow.Id, default))!;

        first.UpdatePurpose("first");
        await firstRepository.UpdateWorkflowAsync(first, default);
        stale.UpdatePurpose("stale");

        ConcurrencyConflictException? exception = await Assert.That(async () =>
                await staleRepository.UpdateWorkflowAsync(stale, default))
            .Throws<ConcurrencyConflictException>();
        await Assert.That(exception!.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        await Assert.That(exception.InnerException).IsTypeOf<DbUpdateConcurrencyException>();
    }

    private static RegistrationWorkflow CreateWorkflow(Guid tenantId, Guid eventId)
    {
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, eventId, "checkout", Now);
        RegistrationRequirement requirement = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.COMPLETION_ONLY,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now);
        requirement.AddChannel(RegistrationChannel.Create(requirement, 1, true, null, Now));
        workflow.AddRequirement(requirement);
        return workflow;
    }

    private static RegistrationForm CreateForm(Guid tenantId, Guid eventId)
    {
        RegistrationForm form = RegistrationForm.Create(
            tenantId, eventId, "platform.registration", "attendee", "Attendee", Now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", Now);
        RegistrationFormField field = RegistrationFormField.Create(
            Guid.CreateVersion7(), section, 1, "platform.registration", "email", "Email",
            RegistrationFieldTypeEnum.Email, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, Now);
        version.AddSection(section);
        version.AddField(section, field);
        version.AddOption(field, RegistrationFormFieldOption.Create(
            Guid.CreateVersion7(), field, 1, "primary", "Primary", Now));
        form.AddVersion(version);
        return form;
    }

    private static ExploreDbContext CreateContext(
        string database,
        InMemoryDatabaseRoot root,
        Guid tenantId)
    {
        var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(database, root).Options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };
        return context;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
