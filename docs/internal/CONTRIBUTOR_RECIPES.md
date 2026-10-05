ABOUTME: Step-by-step blueprints and recipes for implementing common features.
ABOUTME: Practical guide covering entities, CQRS slices, API endpoints, Blazor pages, outbox events, and tests.

# Contributor Feature Recipes & Blueprints

> **Audience:** Contributors | Developers | Architects | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-08-16
> **Source Anchors:** `docs/DEVELOPER_GUIDE.md`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/REQUEST_FLOWS.md`, `docs/QUICK_REFERENCE.md`

This document provides concrete, copy-pasteable blueprints for implementing features in **ISLAMU Event** according to project architectural invariants.

---

## Table of Recipes

1. [Recipe 1: Adding a New Domain Entity & Repository](#recipe-1-adding-a-new-domain-entity--repository)
2. [Recipe 2: Adding a CQRS Command & Query Slice](#recipe-2-adding-a-cqrs-command--query-slice)
3. [Recipe 3: Adding an API Controller Endpoint with HATEOAS Affordances](#recipe-3-adding-an-api-controller-endpoint-with-hateoas-affordances)
4. [Recipe 4: Adding a Blazor WASM Page with Design Tokens & HAL Gating](#recipe-4-adding-a-blazor-wasm-page-with-design-tokens--hal-gating)
5. [Recipe 5: Adding a Domain Event & Transactional Outbox Message](#recipe-5-adding-a-domain-event--transactional-outbox-message)
6. [Recipe 6: Writing Automated Tests Across Testing Tiers](#recipe-6-writing-automated-tests-across-testing-tiers)

---

## Recipe 1: Adding a New Domain Entity & Repository

### Step 1: Create Domain Entity (`Explore.Domain/`)
Create a new file under `src/Explore.Domain/<EntityName>.cs`. Ensure you implement marker interfaces for tenant isolation, auditing, or soft deletion:

```csharp
// src/Explore.Domain/EventSponsor.cs
// ABOUTME: Domain entity representing an event sponsor organization.
// ABOUTME: Implements tenant isolation and full audit tracking.

namespace Explore.Domain;

public class EventSponsor : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid EventId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = "Bronze"; // Bronze, Silver, Gold, Platinum
    public string? WebsiteUrl { get; set; }
    public string? LogoUrl { get; set; }
    public int DisplayOrder { get; set; }

    // Navigation properties (Readonly in domain logic)
    public Event? Event { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // ISoftDeletable
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
```

### Step 2: Add EF Core Configuration (`Explore.Persistence/Configurations/`)
```csharp
// src/Explore.Persistence/Configurations/EventSponsorConfiguration.cs
// ABOUTME: EF Core entity configuration for EventSponsor.
// ABOUTME: Configures indices, table mappings, and required column constraints.

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations;

public class EventSponsorConfiguration : IEntityTypeConfiguration<EventSponsor>
{
    public void Configure(EntityTypeBuilder<EventSponsor> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Tier)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(e => new { e.TenantId, e.EventId });

        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => e.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

### Step 3: Register in `ExploreDbContext` Partials
1. Add `DbSet<EventSponsor>` in `src/Explore.Persistence/ExploreDbContext.DbSets.cs`.
2. Add Named Query Filter in `src/Explore.Persistence/ExploreDbContext.QueryFilters.cs`:
   ```csharp
   modelBuilder.Entity<EventSponsor>()
       .HasQueryFilter(e => (TenantContext == null || !TenantContext.IsResolved || e.TenantId == TenantContext.TenantId)
                         && (!e.IsDeleted));
   ```

### Step 4: Add Repository Interface & Implementation
1. Interface in `src/Explore.Application/Contracts/Persistence/IEventSponsorRepository.cs`:
   ```csharp
   namespace Explore.Application.Contracts.Persistence;

   public interface IEventSponsorRepository : IGenericRepository<EventSponsor>
   {
       Task<IReadOnlyList<EventSponsor>> GetByEventIdAsync(Guid eventId, CancellationToken ct = default);
   }
   ```
2. Implementation in `src/Explore.Persistence/Repositories/EventSponsorRepository.cs`:
   ```csharp
   namespace Explore.Persistence.Repositories;

   public class EventSponsorRepository : GenericRepository<EventSponsor>, IEventSponsorRepository
   {
       public EventSponsorRepository(ExploreDbContext dbContext) : base(dbContext) { }

       public async Task<IReadOnlyList<EventSponsor>> GetByEventIdAsync(Guid eventId, CancellationToken ct = default)
       {
           return await _dbContext.EventSponsors
               .Where(s => s.EventId == eventId)
               .OrderBy(s => s.DisplayOrder)
               .ToListAsync(ct);
       }
   }
   ```

---

## Recipe 2: Adding a CQRS Command & Query Slice

### Step 1: Create DTO & DTO Validator (`Explore.Application/DTOs/`)

First, define the input DTO:
```csharp
// src/Explore.Application/DTOs/EventSponsor/CreateEventSponsorDto.cs
// ABOUTME: Input DTO for creating an event sponsor.
// ABOUTME: Carries client-submitted fields for event sponsor creation.

namespace Explore.Application.DTOs.EventSponsor;

public class CreateEventSponsorDto
{
    public required string Name { get; set; }
    public required string Tier { get; set; } = "Bronze";
    public string? WebsiteUrl { get; set; }
    public string? LogoUrl { get; set; }
    public int DisplayOrder { get; set; }
}
```

Next, create the DTO validator under `Explore.Application/DTOs/<Entity>/Validators/`:
```csharp
// src/Explore.Application/DTOs/EventSponsor/Validators/CreateEventSponsorDtoValidator.cs
// ABOUTME: FluentValidation rules for CreateEventSponsorDto.
// ABOUTME: Instantiated manually in handlers with repository references if needed.

using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSponsor;
using FluentValidation;

namespace Explore.Application.DTOs.EventSponsor.Validators;

public class CreateEventSponsorDtoValidator : AbstractValidator<CreateEventSponsorDto>
{
    public CreateEventSponsorDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(200).WithMessage("{PropertyName} must not exceed 200 characters.");

        RuleFor(x => x.Tier)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(50).WithMessage("{PropertyName} must not exceed 50 characters.");

        RuleFor(x => x.WebsiteUrl)
            .Must(uri => string.IsNullOrEmpty(uri) || Uri.IsWellFormedUriString(uri, UriKind.Absolute))
            .WithMessage("{PropertyName} must be a valid absolute URL.");
    }
}
```

### Step 2: Create Native CQS Command (`Explore.Application/Features/`)

The command wraps the DTO along with any route identifiers or authenticated caller context:
```csharp
// src/Explore.Application/Features/EventSponsors/Requests/Commands/CreateEventSponsorCommand.cs

using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSponsor;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSponsors.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public record CreateEventSponsorCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required Guid EventId { get; init; }
    public required CreateEventSponsorDto SponsorDto { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
    IDictionary<string, object>? ISecureRequest.ResourceAttributes => null;
}
```

### Step 3: Create Command Handler (`Explore.Application/Features/`)

The handler receives the command, manually instantiates the DTO validator, validates `request.SponsorDto`, maps it to the entity via Riok.Mapperly, and persists via the repository:
```csharp
// src/Explore.Application/Features/EventSponsors/Handlers/Commands/CreateEventSponsorCommandHandler.cs

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSponsor.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSponsors.Requests.Commands;
using Explore.Application.Mappings;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.EventSponsors.Handlers.Commands;

public class CreateEventSponsorCommandHandler : ICommandHandler<CreateEventSponsorCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSponsorRepository _sponsorRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ITenantContext _tenantContext;

    public CreateEventSponsorCommandHandler(
        IEventSponsorRepository sponsorRepository,
        IEventRepository eventRepository,
        ITenantContext tenantContext)
    {
        _sponsorRepository = sponsorRepository;
        _eventRepository = eventRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateEventSponsorCommand request, CancellationToken cancellationToken)
    {
        var response = new BaseCommandResponse<Guid>();

        // 1. Manually instantiate and execute DTO validator (Invariant #2)
        var validator = new CreateEventSponsorDtoValidator();
        var validationResult = await validator.ValidateAsync(request.SponsorDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            response.Success = false;
            response.Message = "Event sponsor creation failed due to validation errors.";
            response.Errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
            return response;
        }

        // 2. Validate parent aggregate exists
        var eventEntity = await _eventRepository.GetByIdAsync(request.EventId);
        if (eventEntity == null)
        {
            throw new NotFoundException(nameof(Event), request.EventId);
        }

        // 3. Map DTO to Domain Entity via Riok.Mapperly (Repositories take entities, never DTOs)
        var sponsor = EventSponsorMapper.ToEntity(request.SponsorDto);
        sponsor.EventId = request.EventId;
        sponsor.TenantId = _tenantContext.TenantId;

        var created = await _sponsorRepository.AddAsync(sponsor);

        response.Success = true;
        response.Id = created.Id;
        response.Message = "Sponsor created successfully.";
        return response;
    }
}
```

---

## Recipe 3: Adding an API Controller Endpoint with HATEOAS Affordances

### Step 1: Create Controller in `Explore.API/Controllers/`
```csharp
// src/Explore.API/Controllers/EventSponsorsController.cs

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSponsor;
using Explore.Application.Features.EventSponsors.Requests.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/sponsors")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventSponsorsController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "eventSponsor",
        "Event sponsor validation failed",
        "Event sponsor creation failed.");

    private readonly ICommandHandler<CreateEventSponsorCommand, BaseCommandResponse<Guid>> _createHandler;

    public EventSponsorsController(ICommandHandler<CreateEventSponsorCommand, BaseCommandResponse<Guid>> createHandler)
    {
        _createHandler = createHandler;
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventSponsor)]
    [EndpointSummary("Create Event Sponsor")]
    [EndpointDescription("Creates a new sponsor for an event. Requires event edit permissions.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create(
        Guid eventId,
        [FromBody] CreateEventSponsorDto dto,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateEventSponsorCommand
        {
            EventId = eventId,
            SponsorDto = dto
        };

        var response = await _createHandler.ExecuteAsync(command, cancellationToken);

        if (!response.Success)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventSponsorById,
            new { eventId = eventId, id = response.Id },
            response);
    }
}
```

### Step 2: Refresh OpenAPI Client
After compiling `Explore.API`, build the Blazor client to regenerate `IEventApiClient`:
```bash
dotnet build --project src/Explore.API/Explore.API.csproj --configuration Release --verbosity quiet
dotnet build --project src/Explore.Blazor.Client/Explore.Blazor.Client.csproj --configuration Release --verbosity quiet
```

---

## Recipe 4: Adding a Blazor WASM Page with Design Tokens & HAL Gating

### Step 1: Create Razor Page (`Explore.Blazor.Client/Pages/`)
```razor
@* src/Explore.Blazor.Client/Pages/Events/EventSponsors.razor *@
@page "/events/{EventId:guid}/sponsors"
@using Explore.Blazor.Client.Services
@inject IEventApiClient ApiClient
@inject ISnackbar Snackbar

<MudContainer MaxWidth="MaxWidth.Large" Class="isl-event-sponsors">
    <div class="isl-event-sponsors__header">
        <h1 class="isl-event-sponsors__title">Event Sponsors</h1>

        @* HAL Affordance Gating: Show Add button only if affordance exists *@
        @if (_eventDto?.Links?.ContainsKey("edit") == true)
        {
            <MudButton Variant="Variant.Filled"
                       Color="Color.Primary"
                       StartIcon="@Icons.Material.Filled.Add"
                       OnClick="OpenAddSponsorDialog"
                       Class="isl-event-sponsors__add-btn">
                Add Sponsor
            </MudButton>
        }
    </div>

    <div class="isl-event-sponsors__grid">
        @foreach (var sponsor in _sponsors)
        {
            <MudCard Class="isl-event-sponsors__card" Elevation="2">
                <MudCardHeader>
                    <CardHeaderContent>
                        <MudText Typo="Typo.h6">@sponsor.Name</MudText>
                        <MudChip T="string" Color="Color.Secondary" Size="Size.Small">@sponsor.Tier</MudChip>
                    </CardHeaderContent>
                </MudCardHeader>
                <MudCardActions>
                    @* Gate Delete button using HAL link *@
                    @if (sponsor.Links?.ContainsKey("delete") == true)
                    {
                        <MudIconButton Icon="@Icons.Material.Filled.Delete"
                                       Color="Color.Error"
                                       OnClick="@(() => DeleteSponsor(sponsor.Id))" />
                    }
                </MudCardActions>
            </MudCard>
        }
    </div>
</MudContainer>
```

### Step 2: Create Scoped CSS (`EventSponsors.razor.css`)
```css
/* src/Explore.Blazor.Client/Pages/Events/EventSponsors.razor.css */
.isl-event-sponsors {
    padding-block: var(--isl-spacing-lg);
}

.isl-event-sponsors__header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-block-end: var(--isl-spacing-md);
}

.isl-event-sponsors__title {
    font-size: var(--isl-font-size-2xl);
    font-weight: 700;
    color: var(--isl-color-text-primary);
}

.isl-event-sponsors__grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    gap: var(--isl-spacing-md);
}

.isl-event-sponsors__card {
    border-radius: var(--isl-border-radius-md);
    background-color: var(--isl-color-surface);
}
```

---

## Recipe 5: Adding a Domain Event & Transactional Outbox Message

### Step 1: Define Domain Event
```csharp
// src/Explore.Domain/Events/EventSponsorAddedDomainEvent.cs
namespace Explore.Domain.Events;

public record EventSponsorAddedDomainEvent(
    Guid SponsorId,
    Guid EventId,
    Guid TenantId,
    string SponsorName,
    DateTime OccurredOn
);
```

### Step 2: Enqueue in Outbox during Handler Execution
```csharp
// In CreateEventSponsorCommandHandler.cs:
var domainEvent = new EventSponsorAddedDomainEvent(
    sponsor.Id,
    sponsor.EventId,
    sponsor.TenantId,
    sponsor.Name,
    DateTime.UtcNow);

var outboxMessage = new OutboxMessage
{
    Id = Guid.CreateVersion7(),
    TenantId = sponsor.TenantId,
    MessageType = nameof(EventSponsorAddedDomainEvent),
    PayloadJson = JsonSerializer.Serialize(domainEvent),
    Status = OutboxMessageStatus.Pending,
    CreatedAt = DateTime.UtcNow
};

await _outboxRepository.AddAsync(outboxMessage);
// SaveChangesAsync commits both Sponsor and OutboxMessage atomically!
```

---

## Recipe 6: Writing Automated Tests Across Testing Tiers

### A. Unit Tests (`Event.Application.UnitTests/`)
```csharp
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Explore.Application.DTOs.EventSponsor;
using Explore.Application.DTOs.EventSponsor.Validators;

namespace Event.Application.UnitTests.DTOs.EventSponsors;

public class CreateEventSponsorDtoValidatorTests
{
    [Test]
    public async Task Validate_EmptyName_ShouldHaveValidationError()
    {
        var validator = new CreateEventSponsorDtoValidator();
        var dto = new CreateEventSponsorDto
        {
            Name = "",
            Tier = "Gold",
            WebsiteUrl = "https://example.com",
            DisplayOrder = 1
        };

        var result = await validator.ValidateAsync(dto);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors).Contains(e => e.PropertyName == "Name");
    }
}
```

### B. Persistence Tests with Testcontainers (`Event.Persistence.IntegrationTests/`)
```csharp
using TUnit.Core;
using Explore.Domain;
using Explore.Persistence.Repositories;

namespace Event.Persistence.IntegrationTests.Repositories;

public class EventSponsorRepositoryTests : TestcontainersTestBase
{
    [Test]
    public async Task AddAsync_ValidSponsor_PersistsSuccessfully()
    {
        using var dbContext = CreateDbContext();
        var repo = new EventSponsorRepository(dbContext);

        var sponsor = new EventSponsor
        {
            TenantId = CurrentTenantId,
            EventId = Guid.NewGuid(),
            Name = "Acme Corp",
            Tier = "Gold"
        };

        var created = await repo.AddAsync(sponsor);

        await Assert.That(created.Id).IsNotDefault();
    }
}
```

---

## 7. Verification Commands

Always run these commands before opening a Pull Request:
```bash
# 1. Build Solution in Release Mode
dotnet build --configuration Release --verbosity quiet

# 2. Run Architecture Tests
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet

# 3. Run Unit Tests
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet
```
