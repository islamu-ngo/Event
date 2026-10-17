using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Strategies;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class StrategyDiagnosticConfidentialityTests
{
    [Test]
    public async Task IslamicAspectLifecycleRetainsLinksWithoutDiagnosticIdentityOrAspectDetails()
    {
        var logger = new TestListLogger<IslamicEventStrategy>();
        var strategy = new IslamicEventStrategy(logger);
        Guid eventId = Guid.NewGuid();
        var entity = CreateEvent(eventId, Guid.NewGuid().ToString("N"));
        entity.IslamicAspect = new() { MadhabId = 7 };

        await strategy.PostCreateAsync(entity);
        await strategy.PostUpdateAsync(entity);
        var links = strategy.GetLinks(entity).ToArray();

        await Assert.That(links.Any(link => link.Rel == "islamic-aspect"
            && link.Href == $"/api/events/{eventId}/aspects/islamic")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "madhab" && link.Href == "/api/madhabs/7")).IsTrue();
        await Assert.That(entity.IslamicAspect.MadhabId).IsEqualTo(7);
        await Assert.That(logger.Entries.Count).IsEqualTo(2);
        foreach (var entry in logger.Entries)
        {
            await Assert.That(entry.Level).IsEqualTo(LogLevel.Information);
            await Assert.That(entry.Exception).IsNull();
            await Assert.That(entry.Arguments).IsEmpty();
            await Assert.That(entry.Message + JsonSerializer.Serialize(entry.State)).DoesNotContain(eventId.ToString());
        }
    }

    [Test]
    public async Task TechAspectLifecycleRetainsAuthorizedRepositoryLinkWithoutPrivateDiagnosticCopies()
    {
        string canary = Guid.NewGuid().ToString("N");
        string repository = "https://repository.example.test/" + canary;
        Guid eventId = Guid.NewGuid();
        var logger = new TestListLogger<TechEventStrategy>();
        var strategy = new TechEventStrategy(logger);
        var entity = CreateEvent(eventId, canary);
        entity.TechAspect = new() { GithubRepoUrl = repository };

        await strategy.PostCreateAsync(entity);
        await strategy.PostUpdateAsync(entity);
        var links = strategy.GetLinks(entity).ToArray();

        await Assert.That(links.Any(link => link.Rel == "github-repo" && link.Href == repository)).IsTrue();
        await Assert.That(entity.TechAspect.GithubRepoUrl).IsEqualTo(repository);
        await Assert.That(logger.Entries.Count).IsEqualTo(2);
        foreach (var entry in logger.Entries)
        {
            await Assert.That(entry.Level).IsEqualTo(LogLevel.Information);
            await Assert.That(entry.Exception).IsNull();
            await Assert.That(entry.Arguments).IsEmpty();
            var exported = entry.Message + JsonSerializer.Serialize(entry.State);
            await Assert.That(exported).DoesNotContain(canary);
            await Assert.That(exported).DoesNotContain(eventId.ToString());
        }
    }

    private static Explore.Domain.Event CreateEvent(Guid id, string title)
    {
        Guid actorId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        return new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = id,
            Title = title,
            ActorId = actorId,
            Actor = new()
            {
                Id = actorId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = new() { Id = (int)ActorTypeEnum.User, MasterCode = "User", FullName = "User" },
                Pii = new() { DisplayName = title }
            },
            TenantId = tenantId,
            Tenant = new()
            {
                Id = tenantId,
                FullName = "Strategy tenant",
                Slug = "strategy-tenant",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = new() { Id = (int)TenantStatusEnum.Active, MasterCode = "Active", FullName = "Active", IsActiveState = true }
            },
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = new() { Id = (int)VisibilityTypeEnum.Public, MasterCode = "Public", FullName = "Public" },
            EventStatus = new() { Id = (int)EventStatusEnum.Draft, MasterCode = "Draft", FullName = "Draft" },
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = new() { Id = (int)EventFormatEnum.Local, MasterCode = "Local", FullName = "Local" }
        };
    }
}
