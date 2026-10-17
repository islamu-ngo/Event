using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;
using Explore.Application.Features.EventTemplates.Handlers.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class AdministrativeActionOriginalAdmissionTests
{
    [Test]
    [Arguments("template_create", "unchanged")]
    [Arguments("template_create", "binding")]
    [Arguments("template_create", "stamp")]
    [Arguments("template_create", "grant")]
    [Arguments("template_create", "request")]
    [Arguments("template_create", "route")]
    [Arguments("template_create", "credential_after_reads")]
    [Arguments("template_update", "unchanged")]
    [Arguments("template_update", "binding")]
    [Arguments("template_update", "stamp")]
    [Arguments("template_update", "grant")]
    [Arguments("template_update", "request")]
    [Arguments("template_update", "route")]
    [Arguments("template_update", "credential_after_reads")]
    [Arguments("template_delete", "unchanged")]
    [Arguments("template_delete", "binding")]
    [Arguments("template_delete", "stamp")]
    [Arguments("template_delete", "grant")]
    [Arguments("template_delete", "request")]
    [Arguments("template_delete", "route")]
    [Arguments("template_delete", "credential_after_reads")]
    [Arguments("definition_create", "unchanged")]
    [Arguments("definition_create", "binding")]
    [Arguments("definition_create", "stamp")]
    [Arguments("definition_create", "grant")]
    [Arguments("definition_create", "request")]
    [Arguments("definition_create", "route")]
    [Arguments("definition_create", "credential_after_reads")]
    [Arguments("definition_update", "unchanged")]
    [Arguments("definition_update", "binding")]
    [Arguments("definition_update", "stamp")]
    [Arguments("definition_update", "grant")]
    [Arguments("definition_update", "request")]
    [Arguments("definition_update", "route")]
    [Arguments("definition_update", "credential_after_reads")]
    public async Task TemplateAndDefinitionBodiesRequireOriginalAuthorityThroughCommit(
        string action, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid definitionId = Guid.CreateVersion7();
        Guid templateId = Guid.CreateVersion7();
        Guid grantId = Guid.CreateVersion7();
        string key = "admission-" + Guid.CreateVersion7().ToString("N");
        await SeedAsync(fixture, definitionId, grantId, false, false,
            fixture.Clock.GetUtcNow().AddMinutes(5), _ => { });
        Guid templateStamp;
        Guid definitionStamp;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.EventTemplates.Add(new EventTemplate
            {
                Id = templateId,
                TenantId = PlatformDefaults.DefaultTenantId,
                TemplateKey = key,
                DisplayName = "Original template",
                Version = 1,
                IsActive = true
            });
            await seed.SaveChangesAsync();
            templateStamp = await seed.EventTemplates.Where(row => row.Id == templateId)
                .Select(row => row.ConcurrencyStamp).SingleAsync();
            definitionStamp = await seed.CustomPropertyDefinitions.Where(row => row.Id == definitionId)
                .Select(row => row.ConcurrencyStamp).SingleAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = services.GetRequiredService<ExploreDbContext>();
        bool bodyObserved = false;
        async Task<bool> HasEffectAsync(ExploreDbContext database, CancellationToken token)
        {
            return action switch
            {
                "template_create" => await database.EventTemplates.AsNoTracking()
                    .AnyAsync(row => row.TemplateKey == key + "-new", token),
                "template_update" => await database.EventTemplates.AsNoTracking()
                    .AnyAsync(row => row.Id == templateId && row.DisplayName == "Changed template", token),
                "template_delete" => await database.EventTemplates.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                    .AsNoTracking().AnyAsync(row => row.Id == templateId && row.IsDeleted, token),
                "definition_create" => await database.CustomPropertyDefinitions.AsNoTracking()
                    .AnyAsync(row => row.Key == key, token),
                "definition_update" => await database.CustomPropertyDefinitions.AsNoTracking()
                    .AnyAsync(row => row.Id == definitionId && row.DisplayName == "Changed definition", token),
                _ => throw new InvalidOperationException("Unknown administrative test action.")
            };
        }
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            bodyObserved = await HasEffectAsync(context, token);
            await Assert.That(bodyObserved).IsTrue();
            switch (change)
            {
                case "binding":
                    await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey,
                            Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "stamp":
                    await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                            Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "grant":
                    await context.TenantUserRoleGrants.Where(row => row.Id == grantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt,
                            fixture.Clock.GetUtcNow().UtcDateTime), token);
                    break;
                case "request":
                    request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
                    break;
                case "route":
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                    break;
                case "credential_after_reads":
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                    {
                        await Assert.That(await HasEffectAsync(context, finalToken)).IsTrue();
                        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                            .ObserveAsync(request.Context.User, finalToken))!;
                        var deadline = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                        fixture.Clock.Advance(deadline - fixture.Clock.GetUtcNow());
                    }, _ => { });
                    break;
            }
        });
        async Task InvokeAsync()
        {
            switch (action)
            {
                case "template_create":
                    var createdTemplate = await ActivatorUtilities.CreateInstance<CreateEventTemplateCommandHandler>(services, owner)
                        .ExecuteAsync(new() { TemplateDto = new() { TemplateKey = key + "-new", DisplayName = "New template" } }, default);
                    await Assert.That(createdTemplate.IsSuccess).IsTrue();
                    break;
                case "template_update":
                    var updatedTemplate = await ActivatorUtilities.CreateInstance<UpdateEventTemplateCommandHandler>(services, owner)
                        .ExecuteAsync(new()
                        {
                            TemplateId = templateId,
                            TenantId = PlatformDefaults.DefaultTenantId,
                            ExpectedConcurrencyStamp = templateStamp,
                            TemplateDto = new UpdateEventTemplateDto { Metadata = new() { DisplayName = "Changed template" } }
                        }, default);
                    await Assert.That(updatedTemplate.IsSuccess).IsTrue();
                    break;
                case "template_delete":
                    await Assert.That(await ActivatorUtilities.CreateInstance<DeleteEventTemplateCommandHandler>(services, owner)
                        .ExecuteAsync(new() { Id = templateId }, default)).IsTrue();
                    break;
                case "definition_create":
                    var createdDefinition = await ActivatorUtilities.CreateInstance<CreateCustomPropertyDefinitionCommandHandler>(services, owner)
                        .ExecuteAsync(new()
                        {
                            DefinitionDto = new CreateCustomPropertyDefinitionDto
                            {
                                EntityTypeName = EntityTypeName.Organization,
                                Namespace = "tenant",
                                Key = key,
                                DisplayName = "New definition",
                                PropertyType = PropertyType.Text,
                                ExposureLevel = ExposureLevel.Public
                            }
                        }, default);
                    await Assert.That(createdDefinition.IsSuccess).IsTrue();
                    break;
                case "definition_update":
                    var updatedDefinition = await ActivatorUtilities.CreateInstance<UpdateCustomPropertyDefinitionCommandHandler>(services, owner)
                        .ExecuteAsync(new()
                        {
                            DefinitionId = definitionId,
                            TenantId = PlatformDefaults.DefaultTenantId,
                            ExpectedConcurrencyStamp = definitionStamp,
                            DefinitionDto = new UpdateCustomPropertyDefinitionDto { Metadata = new() { DisplayName = "Changed definition" } }
                        }, default);
                    await Assert.That(updatedDefinition.IsSuccess).IsTrue();
                    break;
                default:
                    throw new InvalidOperationException("Unknown administrative test action.");
            }
        }
        if (change == "unchanged") await InvokeAsync();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(InvokeAsync);
        await Assert.That(bodyObserved).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await HasEffectAsync(observer, default)).IsEqualTo(change == "unchanged");
        await Assert.That(await observer.TenantUserRoleGrants.Where(row => row.Id == grantId)
            .Select(row => row.RevokedAt).SingleAsync()).IsNull();
        await Assert.That(await observer.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
            .Select(row => row.ProviderKey).SingleAsync()).IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
    }
}
