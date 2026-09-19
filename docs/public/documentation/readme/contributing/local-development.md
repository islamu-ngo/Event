---
description: Set up your local developer workstation to contribute code to ISLAMU Event.
---

# Local Development Guide

This guide helps developers set up a local development workstation to contribute to ISLAMU Event.

---

## 1. Prerequisites

To build and run the complete solution from source, install:

- **.NET 10 SDK** (v10.0.302 or compatible)
- **Docker Engine / Docker Desktop** (v24+ with Compose v2)
- **.NET Aspire CLI**:
  ```bash
  dotnet tool install -g Aspire.Cli
  ```
- **Git**

---

## 2. Clone & Run with .NET Aspire

We use [.NET Aspire](../self-hosting/dotnet-aspire-and-cloud.md) for local orchestration, automatically launching PostgreSQL, Keycloak, Mailpit, `Explore.API`, and `Explore.Blazor`:

```bash
# 1. Clone repository
git clone https://github.com/islamu-ngo/Event.git
cd Event

# 2. Copy configuration
cp .env.example .env

# 3. Launch with Aspire AppHost
aspire run --apphost Explore.AppHost/Explore.AppHost.csproj
```

Open the **Aspire Dashboard** URL displayed in your terminal (typically `http://localhost:18888`) to view running resources, inspect logs, and navigate to the application UI.

---

## 3. Building and Testing

Build the solution in Release configuration:

```bash
dotnet build --configuration Release --verbosity quiet
```

Run tests using [TUnit](tunit.md):

```bash
# Run unit tests for Application layer
dotnet test tests/Explore.Application.Tests/Explore.Application.Tests.csproj \
  --configuration Release \
  --treenode-filter "/*/*/*<TestClass>/*"
```

---

## 4. Architectural Rules & Specifications

Before authoring code, review our core architecture and clean-room policies:

* **[Clean Architecture Conventions](clean-architecture.md)** — Inward dependency rules and application operation boundaries.
* **[TUnit Testing Conventions](tunit.md)** — Writing invariant-breaker tests instead of tautological mocks.
* **[Clean-Room IP & Licensing](clean-room-ip-and-licensing.md)** — Independent design and outbound AGPLv3 protection.

For complete internal developer specifications in GitHub:
* 📖 [`docs/internal/DEVELOPER_GUIDE.md`](https://github.com/islamu-ngo/Event/blob/develop/docs/internal/DEVELOPER_GUIDE.md) — 5-minute mental model and invariants.
* 📖 [`docs/internal/ARCHITECTURE_OVERVIEW.md`](https://github.com/islamu-ngo/Event/blob/develop/docs/internal/ARCHITECTURE_OVERVIEW.md) — C4 diagrams and component interactions.

---

## Related Guides & Next Steps

* **[Clean Architecture Conventions](clean-architecture.md)** — Layer boundaries and CQRS handlers.
* **[TUnit Testing Conventions](tunit.md)** — Test slicing and execution.
* **[.NET Aspire & Cloud Deployment](../self-hosting/dotnet-aspire-and-cloud.md)** — Aspire AppHost orchestration details.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Run the split container topology locally.
