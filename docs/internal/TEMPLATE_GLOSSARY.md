# Template Glossary

> **Project-Agnostic Documentation Placeholder Reference**
>
> This glossary defines all placeholders used throughout the documentation, skills, and agents.
> Replace placeholders with your project-specific values when implementing.

**Last Updated**: January 2026

---

## Table of Contents

1. [Placeholder Syntax](#1-placeholder-syntax)
2. [Project Placeholders](#2-project-placeholders)
3. [Entity Placeholders](#3-entity-placeholders)
4. [Type Placeholders](#4-type-placeholders)
5. [Layer Placeholders](#5-layer-placeholders)
6. [CQRS Placeholders](#6-cqrs-placeholders)
7. [Substitution Examples](#7-substitution-examples)
8. [Best Practices](#8-best-practices)

---

## 1. Placeholder Syntax

All placeholders use **curly braces** `{PlaceholderName}` for consistency with common templating systems.

### Conventions

| Format | Meaning | Example |
|--------|---------|---------|
| `{Entity}` | PascalCase singular | `Event`, `Order`, `Customer` |
| `{Entities}` | PascalCase plural | `Events`, `Orders`, `Customers` |
| `{entity}` | camelCase singular | `event`, `order`, `customer` |
| `{entities}` | camelCase plural | `events`, `orders`, `customers` |
| `{ENTITY}` | UPPERCASE | `EVENT`, `ORDER`, `CUSTOMER` |
| `_{entity}` | Private field | `_event`, `_order`, `_customer` |

---

## 2. Project Placeholders

### Solution & Project Names

| Placeholder | Description | Example Value |
|-------------|-------------|---------------|
| `{Project}` | Solution/project root name | `Explore`, `OrderSystem`, `MyApp` |
| `{Project.Domain}` | Domain layer project | `Explore.Domain` |
| `{Project.Application}` | Application layer project | `Explore.Application` |
| `{Project.Persistence}` | Persistence layer project | `Explore.Persistence` |
| `{Project.Infrastructure}` | Infrastructure layer project | `Explore.Infrastructure` |
| `{Project.API}` | API project | `Explore.API` |
| `{Project.Blazor}` | Blazor Server project | `Explore.Blazor` |
| `{Project.Blazor.Client}` | Blazor WASM project | `Explore.Blazor.Client` |
| `{Project.AppHost}` | Aspire AppHost project | `Explore.AppHost` |

### Database & Context

| Placeholder | Description | Example Value |
|-------------|-------------|---------------|
| `{DbContext}` | EF Core DbContext class | `ExploreDbContext`, `AppDbContext` |
| `{ConnectionString}` | Database connection string name | `DefaultConnection` |
| `{DatabaseProvider}` | Database provider | `PostgreSQL`, `SQL Server` |

---

## 3. Entity Placeholders

### Primary Entity

| Placeholder | Description | Example Value |
|-------------|-------------|---------------|
| `{Entity}` | Main entity being worked on (PascalCase) | `Event`, `Order`, `Product` |
| `{Entities}` | Plural form | `Events`, `Orders`, `Products` |
| `{entity}` | camelCase for variables | `event`, `order`, `product` |
| `{entities}` | camelCase plural | `events`, `orders`, `products` |
| `_{entity}` | Private field | `_event`, `_order`, `_product` |

### Related Entities

| Placeholder | Description | Example Value |
|-------------|-------------|---------------|
| `{RelatedEntity}` | Foreign key target entity | `Organization`, `Category` |
| `{RelatedEntities}` | Plural form | `Organizations`, `Categories` |
| `{ParentEntity}` | Parent in hierarchy | `Category` (self-referencing) |
| `{ChildEntity}` | Child in one-to-many | `OrderItem`, `EventSession` |
| `{ChildEntities}` | Plural form | `OrderItems`, `EventSessions` |
| `{LinkEntity}` | Many-to-many link table | `EventCategories`, `RolePermission` |

### Lookup Tables

| Placeholder | Description | Example Value |
|-------------|-------------|---------------|
| `{LookupEntity}` | Lookup/reference table entity | `EventType`, `OrderStatus` |
| `{LookupEntities}` | Plural form | `EventTypes`, `OrderStatuses` |

---

## 4. Type Placeholders

### ID Types

| Placeholder | Description | Typical Value |
|-------------|-------------|---------------|
| `{IdType}` | Primary key type for main entities | `Guid` |
| `{LookupIdType}` | Primary key type for lookup tables | `int` |
| `{TenantIdType}` | Tenant identifier type | `Guid` |

### Common Types

| Placeholder | Description | Example Value |
|-------------|-------------|---------------|
| `{ResponseType}` | Command response type | `BaseCommandResponse<Guid>` |
| `{ListDtoType}` | List DTO type | `List<{Entity}ListDto>` |
| `{PaginatedType}` | Paginated response type | `PaginatedResult<{Entity}ListDto>` |

---

## 5. Layer Placeholders

### Namespace Patterns

| Placeholder | Expands To |
|-------------|------------|
| `{Project.Domain.Namespace}` | `{Project}.Domain` |
| `{Project.Application.Namespace}` | `{Project}.Application` |
| `{Project.Application.Features.Namespace}` | `{Project}.Application.Features.{Entities}` |
| `{Project.Persistence.Namespace}` | `{Project}.Persistence` |
| `{Project.API.Namespace}` | `{Project}.API` |

### File Path Patterns

| Placeholder | Example Path |
|-------------|--------------|
| `{Entity.Command.Path}` | `{Project}.Application/Features/{Entities}/Requests/Commands/` |
| `{Entity.Query.Path}` | `{Project}.Application/Features/{Entities}/Requests/Queries/` |
| `{Entity.Handler.Path}` | `{Project}.Application/Features/{Entities}/Handlers/` |
| `{Entity.Dto.Path}` | `{Project}.Application/DTOs/{Entity}/` |
| `{Entity.Validator.Path}` | `{Project}.Application/DTOs/{Entity}/Validators/` |
| `{Entity.Repository.Path}` | `{Project}.Persistence/Repositories/` |
| `{Entity.Controller.Path}` | `{Project}.API/Controllers/` |

---

## 6. CQRS Placeholders

### Commands

| Placeholder | Expands To | Example |
|-------------|------------|---------|
| `Create{Entity}Command` | Create command class | `CreateEventCommand` |
| `Update{Entity}Command` | Update command class | `UpdateEventCommand` |
| `Delete{Entity}Command` | Delete command class | `DeleteEventCommand` |
| `Create{Entity}CommandHandler` | Create handler | `CreateEventCommandHandler` |

### Queries

| Placeholder | Expands To | Example |
|-------------|------------|---------|
| `Get{Entity}ListRequest` | List query | `GetEventListRequest` |
| `Get{Entity}DetailsRequest` | Details query | `GetEventDetailsRequest` |
| `Get{Entities}By{RelatedEntity}Request` | Filter query | `GetEventsByOrganizationRequest` |

### DTOs

| Placeholder | Expands To | Example |
|-------------|------------|---------|
| `{Entity}Dto` | Full details DTO | `EventDto` |
| `{Entity}ListDto` | List view DTO | `EventListDto` |
| `Create{Entity}Dto` | Create payload | `CreateEventDto` |
| `Update{Entity}Dto` | Update payload | `UpdateEventDto` |

### Validators

| Placeholder | Expands To | Example |
|-------------|------------|---------|
| `Create{Entity}DtoValidator` | Create validator | `CreateEventDtoValidator` |
| `Update{Entity}DtoValidator` | Update validator | `UpdateEventDtoValidator` |

### Repositories

| Placeholder | Expands To | Example |
|-------------|------------|---------|
| `I{Entity}Repository` | Repository interface | `IEventRepository` |
| `{Entity}Repository` | Repository implementation | `EventRepository` |

---

## 7. Substitution Examples

### Example 1: Event Management System

**Substitutions:**
```
{Project}        = Explore
{Entity}         = Event
{Entities}       = Events
{RelatedEntity}  = Organization
{LookupEntity}   = EventType
{IdType}         = Guid
{LookupIdType}   = int
```

**Template Code:**
```csharp
namespace {Project}.Application.Features.{Entities}.Handlers.Commands;

public class Create{Entity}CommandHandler : IRequestHandler<Create{Entity}Command, BaseCommandResponse<{IdType}>>
{
    private readonly I{Entity}Repository _{entity}Repository;
    private readonly I{RelatedEntity}Repository _{relatedEntity}Repository;

    // ...
}
```

**Becomes:**
```csharp
namespace Explore.Application.Features.Events.Handlers.Commands;

public class CreateEventCommandHandler : IRequestHandler<CreateEventCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventRepository _eventRepository;
    private readonly IOrganizationRepository _organizationRepository;

    // ...
}
```

### Example 2: E-Commerce System

**Substitutions:**
```
{Project}        = ECommerce
{Entity}         = Order
{Entities}       = Orders
{RelatedEntity}  = Customer
{ChildEntity}    = OrderItem
{LookupEntity}   = OrderStatus
{IdType}         = Guid
{LookupIdType}   = int
```

**Template Code:**
```csharp
public class {Entity}Repository : GenericRepository<{Entity}, {IdType}>, I{Entity}Repository
{
    public async Task<List<{Entity}>> Get{Entities}By{RelatedEntity}({IdType} {relatedEntity}Id)
    {
        return await _dbContext.{Entities}
            .Include(e => e.{ChildEntities})
            .Include(e => e.{LookupEntity})
            .Where(e => e.{RelatedEntity}Id == {relatedEntity}Id)
            .ToListAsync();
    }
}
```

**Becomes:**
```csharp
public class OrderRepository : GenericRepository<Order, Guid>, IOrderRepository
{
    public async Task<List<Order>> GetOrdersByCustomer(Guid customerId)
    {
        return await _dbContext.Orders
            .Include(e => e.OrderItems)
            .Include(e => e.OrderStatus)
            .Where(e => e.CustomerId == customerId)
            .ToListAsync();
    }
}
```

---

## 8. Best Practices

### When Creating New Documentation

1. **Always use placeholders** for entity-specific names
2. **Provide substitution tables** at the top of each document
3. **Include concrete examples** in "Implementation Example" sections
4. **Mark project-specific sections** clearly

### Substitution Table Template

Add this to any document that uses placeholders:

```markdown
## Placeholder Substitutions

| Placeholder | Replace With |
|-------------|--------------|
| `{Project}` | Your solution name |
| `{Entity}` | Your main entity |
| `{Entities}` | Plural form |
| `{RelatedEntity}` | Related entity for FK |
| `{IdType}` | `Guid` (recommended) or `int` |
```

### Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Entity | PascalCase, singular | `Event`, `Order` |
| DbSet | PascalCase, plural | `Events`, `Orders` |
| Repository | I{Entity}Repository | `IEventRepository` |
| Controller | {Entity}Controller, or {Entity}{Capability}Controller once a family partitions | `EventController`, `EventModerationController` |
| DTO | {Entity}Dto, {Entity}ListDto | `EventDto`, `EventListDto` |
| Command | {Verb}{Entity}Command | `CreateEventCommand` |
| Query | Get{Entity}*Request | `GetEventListRequest` |
| Handler | {Command/Query}Handler | `CreateEventCommandHandler` |
| Validator | {Dto}Validator | `CreateEventDtoValidator` |

---

## Design Principles Reference

These principles guide all documentation and should be followed when implementing:

### SOLID Principles

| Principle | Application |
|-----------|-------------|
| **S**ingle Responsibility | Each handler handles one command/query |
| **O**pen/Closed | Use interfaces for extension |
| **L**iskov Substitution | Repositories implement interfaces correctly |
| **I**nterface Segregation | Small, focused repository interfaces |
| **D**ependency Inversion | Depend on abstractions (interfaces) |

### Clean Architecture

| Rule | Enforcement |
|------|-------------|
| Dependencies flow inward | Domain has no dependencies |
| Entities are framework-agnostic | No EF Core in Domain |
| Use cases in Application | Business logic in handlers |
| Controllers are thin | Only HTTP ↔ CQS Handlers |

### Clean Code

| Principle | Example |
|-----------|---------|
| **DRY** | Use `GenericRepository<T, TId>` |
| **KISS** | Simple handlers, one responsibility |
| **YAGNI** | Don't add unused abstractions |
| Meaningful names | `GetEventsWithDetails()` not `GetData()` |

---

## Related Documentation

- **[GOVERNANCE.md](GOVERNANCE.md)** - Coding conventions (project-agnostic)
- **[QUICK_REFERENCE.md](QUICK_REFERENCE.md)** - Critical rules summary
- **[ARCHITECTURE.md](ARCHITECTURE.md)** - System architecture patterns
- **[NAMING_CONVENTIONS.md](NAMING_CONVENTIONS.md)** - Capability-partitioned controller families and the rules for splitting one

---

## Implementation Example: ISLAMU Event

This repository (`Explore`) uses the following substitutions:

| Placeholder | ISLAMU Event Value |
|-------------|-------------------|
| `{Project}` | `Explore` |
| `{Project.Domain}` | `Explore.Domain` |
| `{Project.Application}` | `Explore.Application` |
| `{Project.Persistence}` | `Explore.Persistence` |
| `{Project.API}` | `Explore.API` |
| `{Project.Blazor}` | `Explore.Blazor` |
| `{DbContext}` | `ExploreDbContext` |
| `{IdType}` | `Guid` |
| `{LookupIdType}` | `int` |

**Sample Entities:**
- `{Entity}` = `Event`, `Organization`, `User`, `EventSession`
- `{LookupEntity}` = `EventType`, `EventStatus`, `Madhab`, `AudienceAge`
- `{LinkEntity}` = `EventCategories`, `EventTags`, `OrganizationMembers`
