<!-- ABOUTME: Documents the FOSS/commercial dual-versioning strategy for AutoMapper and MediatR. -->
<!-- ABOUTME: Covers build flags, Docker args, Infisical secrets, lock-file strategy, and CVE disclosure. -->

# Dual-Versioning Strategy

## Current Migration Status

AutoMapper has been removed from Application registration and the restored
dependency graph. Its version matrix, security-depth mitigation, build override
and license guidance below are historical and must not be used for current builds.
The remaining MediatR edition policy still applies until its native-operation
closure is complete. This guide is retired by the final single-edition migration;
[Mapperly provenance](legal/dependencies/mapperly.md) owns the current mapping
dependency contract.

## 1. Overview

ISLAMU Event uses a **dual-versioning strategy** for Lucky Penny libraries — specifically **AutoMapper** and **MediatR**. These libraries transitioned from permissive open-source licenses to a commercial model under the "Lucky Penny" umbrella.

- **Default builds** resolve **FOSS versions** (the last permissive-license releases). No license key, environment variable, or build flag is required.
- **ISLAMU production** uses **commercial versions** with license keys injected at runtime via Infisical secret management.

This strategy ensures that **self-hosters and contributors can build and run the project out of the box**, while ISLAMU's own deployments benefit from the latest commercial releases with security patches and new features.

---

## 2. Version Matrix

| Edition | AutoMapper | MediatR | License |
|---|---|---|---|
| **FOSS** (default) | 14.0.0 | 12.5.0 | MIT / Apache-2.0 |
| **Commercial** | 16.1.1 (overridable) | 14.1.0 (overridable) | Lucky Penny commercial |

---

## 3. Security Advisory

> [!CAUTION]
> **AutoMapper 14.0.0 carries [CVE-2026-32933](https://cve.mitre.org/cgi-bin/cvename.cgi?name=CVE-2026-32933) — a Denial of Service (DoS) vulnerability.**
>
> This version is **security-frozen** and will not receive patches under the MIT license.
> The vulnerability is patched in commercial versions **15.1.1** and **16.1.1**.

The default FOSS build applies a global depth ceiling of 64 to every registered map in
`ApplicationServicesRegistration`. That bounds recursive traversal at the application
composition root, directly mitigating the advisory's uncontrolled-recursion path. The
repository suppresses only this exact NuGet advisory after applying that runtime control;
all other package advisories remain visible and actionable.

Self-hosters still have three strategic options:

1. **Use the built-in mitigation** — Keep the global depth ceiling and validate that legitimate DTO graphs do not require more than 64 mapping levels.
2. **Supply your own commercial license** — Purchase a Lucky Penny license and build with `UseCommercialLuckyPennyLibraries=true` to get the patched version.
3. **Migrate away from AutoMapper** — Replace AutoMapper with a source-generated alternative such as [Mapperly](https://mapperly.riok.app/) (see [§12. Long-Term Migration](#12-long-term-migration)).

---

## 4. Self-Hoster Build (Default)

No special configuration is needed. The default build resolves FOSS versions automatically:

```bash
# CLI
dotnet build

# Docker
docker build .
```

- No license key required.
- No environment variable required.
- No build flag required.
- Lock files committed to the repository enforce **deterministic, reproducible builds**.

> [!NOTE]
> The FOSS build is the default path. If you clone the repo and run `dotnet build`, you get the FOSS edition. This is by design — the project must always be buildable without any commercial dependency.

---

## 5. Production Build

Three methods to activate commercial versions:

### 5a. CLI

```bash
dotnet build -p:UseCommercialLuckyPennyLibraries=true
```

### 5b. Docker

```bash
docker build --build-arg USE_COMMERCIAL_LUCKYPENNY_LIBS=true .
```

### 5c. CI / MSBuild Property

Set the MSBuild property `UseCommercialLuckyPennyLibraries=true` in your CI pipeline configuration or a `Directory.Build.props` override:

```xml
<PropertyGroup>
  <UseCommercialLuckyPennyLibraries>true</UseCommercialLuckyPennyLibraries>
</PropertyGroup>
```

---

## 6. Version Overrides

Commercial versions default to **AutoMapper 16.1.1** and **MediatR 14.1.0**, but can be overridden at build time:

```bash
dotnet build \
  -p:UseCommercialLuckyPennyLibraries=true \
  -p:AutoMapperCommercialVersion=17.0.0 \
  -p:MediatRCommercialVersion=15.0.0
```

| Property | Default | Purpose |
|---|---|---|
| `AutoMapperCommercialVersion` | `16.1.1` | Override the commercial AutoMapper version |
| `MediatRCommercialVersion` | `14.1.0` | Override the commercial MediatR version |

> [!TIP]
> Version overrides are useful when testing pre-release commercial builds or when a newer patched version is available before the repository defaults are updated.

---

## 7. License Key Configuration

> [!WARNING]
> License keys are **runtime-only** configuration. They must **NEVER** be passed as Docker build arguments — build args leak through `docker history` and are visible in image metadata.

### Configuration Paths

A **single license key** (`LUCKYPENNY_LICENSE_KEY`) is used for both AutoMapper and MediatR:

| Infisical Secret | Config Key | Environment Variable |
|---|---|---|
| `LUCKYPENNY_LICENSE_KEY` | `Licensing:LuckyPenny:LicenseKey` | `Licensing__LuckyPenny__LicenseKey` |
| `USE_COMMERCIAL_LUCKYPENNY` | `Licensing:LuckyPenny:Enabled` | `Licensing__LuckyPenny__Enabled` |

### Secret Storage

License keys are stored in the **Infisical `/api` folder** and injected at runtime via the project's existing secret management infrastructure (`AddSecretManagement`). The `ConfigurationExtensions.ApplyMapping` method maps flat Infisical secret names to structured .NET configuration keys.

```csharp
// Resolved automatically via IConfiguration after Infisical mapping
var licenseKey = configuration["Licensing:LuckyPenny:LicenseKey"];
```

---

## 8. Infisical Secret Mapping

All secrets live in the Infisical **`/api`** folder:

| Infisical Secret | Mapping | Used At |
|---|---|---|
| `USE_COMMERCIAL_LUCKYPENNY` | `Licensing:LuckyPenny:Enabled` (runtime config) | **Runtime** |
| `LUCKYPENNY_LICENSE_KEY` | `Licensing:LuckyPenny:LicenseKey` (runtime config) | **Runtime** |
| `AUTOMAPPER_COMMERCIAL_VERSION` | MSBuild property `-p:AutoMapperCommercialVersion=...` | **Build time** |
| `MEDIATR_COMMERCIAL_VERSION` | MSBuild property `-p:MediatRCommercialVersion=...` | **Build time** |

> [!IMPORTANT]
> Build-time secrets (`USE_COMMERCIAL_LUCKYPENNY_LIBS` Docker arg, version overrides) are only needed during CI image builds. Runtime secrets (`LUCKYPENNY_LICENSE_KEY`) must be available in the container's environment or mounted configuration at startup. The `ConfigurationExtensions.ApplyMapping` method in `Explore.API` handles the flat-to-structured mapping.

---

## 9. Lock File Strategy

The repository commits **NuGet lock files** (`packages.lock.json`) that track FOSS package versions for deterministic builds.

### FOSS Builds

FOSS builds use `--locked-mode` to ensure the resolved packages match the committed lock files exactly:

```bash
dotnet restore --locked-mode
```

If a dependency drifts, the restore fails — preventing silent version changes.

### Commercial Builds

Commercial builds **skip locked-mode** and re-resolve dependencies, since the commercial packages differ from what the lock files record:

```bash
dotnet restore -p:UseCommercialLuckyPennyLibraries=true
```

### CI Guard

To verify that FOSS lock files remain in sync after changes, CI runs:

```bash
dotnet restore -p:UseCommercialLuckyPennyLibraries=false
git diff --exit-code '**/packages.lock.json'
```

If the lock files have drifted, the CI step fails and the developer must regenerate and commit them.

> [!NOTE]
> Lock files are only authoritative for the FOSS edition. Commercial builds are expected to resolve different versions and do not update or validate against the committed lock files.

---

## 10. How It Works (Technical)

The dual-versioning mechanism is implemented through four coordination points:

### 10a. `Directory.Packages.props` — Conditional Version Groups

MSBuild conditionals select the correct package versions based on the `UseCommercialLuckyPennyLibraries` property:

```xml
<!-- Simplified illustration -->
<ItemGroup Condition="'$(UseCommercialLuckyPennyLibraries)' != 'true'">
  <PackageVersion Include="AutoMapper" Version="14.0.0" />
  <PackageVersion Include="MediatR"    Version="12.5.0" />
</ItemGroup>

<ItemGroup Condition="'$(UseCommercialLuckyPennyLibraries)' == 'true'">
  <PackageVersion Include="AutoMapper" Version="$(AutoMapperCommercialVersion)" />
  <PackageVersion Include="MediatR"    Version="$(MediatRCommercialVersion)" />
</ItemGroup>
```

### 10b. `Directory.Build.props` — DefineConstants + CI Locked-Mode Split

Defines the `USE_COMMERCIAL_LUCKYPENNY_LIBS` preprocessor symbol when commercial mode is active, and controls whether `RestoreLockedMode` is enabled:

```xml
<PropertyGroup Condition="'$(UseCommercialLuckyPennyLibraries)' == 'true'">
  <DefineConstants>$(DefineConstants);USE_COMMERCIAL_LUCKYPENNY_LIBS</DefineConstants>
  <RestoreLockedMode>false</RestoreLockedMode>
</PropertyGroup>

<PropertyGroup Condition="'$(UseCommercialLuckyPennyLibraries)' != 'true'">
  <RestoreLockedMode>true</RestoreLockedMode>
</PropertyGroup>
```

### 10c. `ApplicationServicesRegistration.cs` — License Key Injection

Uses `#if` preprocessor directives to conditionally inject the single Lucky Penny license key at runtime:

```csharp
#if USE_COMMERCIAL_LUCKYPENNY_LIBS
    var licenseKey = configuration["Licensing:LuckyPenny:LicenseKey"];
    if (!string.IsNullOrEmpty(licenseKey))
    {
        cfg.LicenseKey = licenseKey; // Same key for both AutoMapper and MediatR
    }
#endif
```

### 10d. `ConfigurationExtensions.cs` — Infisical Secret Mapping

Maps flat Infisical secret names to structured .NET config keys:

```csharp
// USE_COMMERCIAL_LUCKYPENNY → Licensing:LuckyPenny:Enabled
// LUCKYPENNY_LICENSE_KEY → Licensing:LuckyPenny:LicenseKey
TrySet(mappedConfig, config, "Licensing:LuckyPenny:Enabled",
    NormalizeBoolean(ReadFirst(config, "USE_COMMERCIAL_LUCKYPENNY", ...)));
TrySet(mappedConfig, config, "Licensing:LuckyPenny:LicenseKey",
    ReadFirst(config, "LUCKYPENNY_LICENSE_KEY", ...));
```

### 10e. Dockerfiles — Build ARG Passthrough

Dockerfiles accept the flag as a build argument and forward it to MSBuild:

```dockerfile
ARG USE_COMMERCIAL_LUCKYPENNY_LIBS=false
RUN dotnet publish ... -p:UseCommercialLuckyPennyLibraries=${USE_COMMERCIAL_LUCKYPENNY_LIBS}
```

---

## 11. Files Involved

| File | Role |
|---|---|
| `Directory.Packages.props` | Conditional version groups for FOSS vs. commercial packages |
| `Directory.Build.props` | `DefineConstants` + CI locked-mode split |
| `Explore.Application/ApplicationServicesRegistration.cs` | License key injection via `#if` preprocessor directives |
| `Explore.API/Extensions/ConfigurationExtensions.cs` | Infisical secret name → .NET config key mapping |
| `Explore.API/Dockerfile` | Build ARG for commercial flag |
| `Explore.Blazor/Dockerfile` | Build ARG for commercial flag |

---

## 12. Long-Term Migration

### AutoMapper → Mapperly

[Mapperly](https://mapperly.riok.app/) is a **source-generated**, compile-time mapper licensed under **MIT**. It produces zero-reflection, zero-allocation mapping code and eliminates the runtime cost and licensing concerns of AutoMapper.

**Benefits:**
- MIT licensed — no commercial dependency
- Source-generated — no runtime reflection or DoS surface
- Better performance — compile-time code generation
- Compile-time safety — mapping errors caught at build time

### MediatR → Alternatives

MediatR is harder to replace due to its deep integration with the CQRS pipeline (behaviors, validators, notifications). Potential alternatives:

- **[Wolverine](https://wolverinefx.net/)** — Full messaging framework with mediator capabilities (MIT)
- **Raw dispatch** — Manual `IServiceProvider`-based handler resolution (no dependency)

> [!NOTE]
> Migration is a long-term consideration, not an immediate action. The dual-versioning strategy is designed to be stable and maintainable until a migration decision is made. Any migration should be tracked as a separate initiative with its own PRD and task breakdown.
