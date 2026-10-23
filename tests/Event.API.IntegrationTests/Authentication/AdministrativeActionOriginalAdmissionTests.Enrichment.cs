using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Features.EventTemplates.Handlers.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class AdministrativeActionOriginalAdmissionTests
{
    [Test]
    [Arguments("create")]
    [Arguments("update")]
    [Arguments("delete")]
    public async Task OriginalTemplateQuestionUsesPersistedTenantFactsForForwardedSupport(string action)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = Guid.Empty;
        await SeedAsync(fixture, Guid.CreateVersion7(), Guid.CreateVersion7(), true, true,
            fixture.Clock.GetUtcNow().AddMinutes(5), id => sessionId = id);
        Guid templateId = Guid.CreateVersion7();
        Guid stamp;
        string key = "enriched-" + Guid.CreateVersion7().ToString("N");
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.EventTemplates.Add(new EventTemplate
            {
                Id = templateId,
                TenantId = PlatformDefaults.DefaultTenantId,
                TemplateKey = key,
                DisplayName = "Original enriched template",
                Version = 1,
                IsActive = true
            });
            await seed.SaveChangesAsync();
            stamp = await seed.EventTemplates.Where(row => row.Id == templateId)
                .Select(row => row.ConcurrencyStamp).SingleAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>()
            .GetCurrentAsync()).IsActive).IsTrue();
        switch (action)
        {
            case "create":
                var created = await ActivatorUtilities.CreateInstance<CreateEventTemplateCommandHandler>(request.Services)
                    .ExecuteAsync(new() { TemplateDto = new() { TemplateKey = key + "-new", DisplayName = "Created enriched template" } }, default);
                await Assert.That(created.IsSuccess).IsTrue();
                break;
            case "update":
                var updated = await ActivatorUtilities.CreateInstance<UpdateEventTemplateCommandHandler>(request.Services)
                    .ExecuteAsync(new()
                    {
                        TemplateId = templateId,
                        ExpectedConcurrencyStamp = stamp,
                        TenantId = Guid.CreateVersion7(),
                        TemplateDto = new UpdateEventTemplateDto { Metadata = new() { DisplayName = "Updated enriched template" } }
                    }, default);
                await Assert.That(updated.IsSuccess).IsTrue();
                break;
            case "delete":
                await Assert.That(await ActivatorUtilities.CreateInstance<DeleteEventTemplateCommandHandler>(request.Services)
                    .ExecuteAsync(new() { Id = templateId }, default)).IsTrue();
                break;
            default:
                throw new InvalidOperationException("Unknown template action.");
        }
        await Assert.That(request.Services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        var template = await observer.EventTemplates.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking().SingleAsync(row => row.Id == templateId);
        await Assert.That(template.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(template.IsDeleted).IsEqualTo(action == "delete");
        await Assert.That(template.DisplayName)
            .IsEqualTo(action == "update" ? "Updated enriched template" : "Original enriched template");
        await Assert.That(await observer.EventTemplates.AnyAsync(row => row.TemplateKey == key + "-new"))
            .IsEqualTo(action == "create");
    }
}
