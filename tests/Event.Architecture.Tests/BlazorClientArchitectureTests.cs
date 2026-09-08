// ABOUTME: Architecture fitness functions for Blazor (Explore.Blazor host + Explore.Blazor.Client WASM).
// Component injection uses compiled metadata; placement rules inspect handwritten source.

using System.Reflection;
using System.Text.RegularExpressions;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;
using Explore.Blazor.Client.Services.ControlPlane;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Architecture.Tests;

/// <summary>
/// Architectural guardrails for the Blazor layer (host + client).
/// Each rule has a deliberately minimal exception list — NEW violations are forbidden.
/// Exception lists should shrink over time; they document TODOs, not permanent carve-outs.
/// </summary>
public class BlazorClientArchitectureTests
{
    private static readonly string? BlazorClientRoot = ResolveProjectRoot("Explore.Blazor.Client");
    private static readonly string? BlazorHostRoot = ResolveProjectRoot("Explore.Blazor");

    // --------------------------------------------------------------------------------------------
    // Exception lists — all paths relative to the repository root (forward slashes).
    // Every entry represents a deliberate deferral. Shrink these aggressively.
    // --------------------------------------------------------------------------------------------

    private static readonly HashSet<string> Known_GeneratedApiClient_ComponentExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pages/Admin/Instance/Components/SupportAccessConsoleSection.razor",
    };

    private static readonly HashSet<string> Known_ConsoleWriteLine_Files = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Known_MiddlewareLambda_LongBodies = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Known_NewDialogOptions_Files = new(StringComparer.OrdinalIgnoreCase)
    {
        "Explore.Blazor.Client/Shared/LoginPromptDialog.razor",
        "Explore.Blazor.Client/Pages/User/Components/SettingsConnectedApps.razor",
        "Explore.Blazor.Client/Pages/Admin/Tenant/Components/TenantLookupTablesSection.razor",
        "Explore.Blazor.Client/Pages/Admin/Dialogs/CreateApiKeyDialog.razor",
        "Explore.Blazor.Client/Pages/Admin/Dialogs/CreateApiKeyDialog.razor.cs",
    };

    private static readonly HashSet<string> Known_IJSRuntimeInServices_Files = new(StringComparer.OrdinalIgnoreCase)
    {
        "Explore.Blazor.Client/Services/UserSettingsService.cs",
        "Explore.Blazor.Client/Services/InstanceOnboardingService.cs",
        "Explore.Blazor.Client/Services/Accessibility/AccessibilityFocusService.cs",
        "Explore.Blazor.Client/Services/Accessibility/AccessibilityAnnouncerService.cs",
    };

    private static readonly HashSet<string> Known_MutableStateSingleton_Files = new(StringComparer.OrdinalIgnoreCase)
    {
        // Wave B Phase 6B swaps HashSet for ImmutableHashSet snapshot.
        "Explore.Blazor/Services/DynamicAuthSchemeManager.cs",
        // Bounded session-scoped token cache — ConcurrentDictionary required for atomic operations.
        "Explore.Blazor/Services/CircuitTokenStore.cs",
        // SetupSecretSessionService (co-located) uses ConcurrentDictionary for ephemeral secret storage.
        "Explore.Blazor/Services/CircuitAccessTokenService.cs",
    };

    private static readonly HashSet<string> Known_IConfigurationInjection_Files = new(StringComparer.OrdinalIgnoreCase)
    {
        // Wave B Phase 6B migrates to IOptions<T>.
        "Explore.Blazor/Services/DynamicAuthSchemeManager.cs",
    };

    private static readonly HashSet<string> Known_ModelTypesInInterfaceFile_Files = new(StringComparer.OrdinalIgnoreCase)
    {
        // Wave A Phase 2 extracts DTOs co-located with these interfaces into Models/ subfolders.
        "Explore.Blazor.Client/Contracts/Services/IContactShareConsentService.cs",
        "Explore.Blazor.Client/Contracts/Services/ILocalizationAdminService.cs",
        "Explore.Blazor.Client/Contracts/Services/Footer/IFooterAdminService.cs",
    };

    private static readonly HashSet<string> Known_BffClient_Exceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // These are currently being migrated to Refit.
        "Explore.Blazor.Client/Program.cs",
        "Explore.Blazor.Client/Services/Http/BffClient.cs",
        "Explore.Blazor.Client/Services/InstanceOnboardingService.cs",
        "Explore.Blazor.Client/Services/TenantOnboardingService.cs",
        "Explore.Blazor.Client/Services/TenantBrandingSettingsAdminService.cs",
        "Explore.Blazor.Client/Services/LanguagePreferenceService.cs",
        "Explore.Blazor.Client/Services/PublicExperienceService.cs",
        "Explore.Blazor.Client/Services/ImageStorageService.cs",
        "Explore.Blazor.Client/Services/ImageUploadClient.cs"
    };

    private static readonly HashSet<string> Known_RawHttpJson_Exceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Low-level HTTP helpers are the approved boundary for raw JSON deserialization/problem handling.
        "Explore.Blazor.Client/Extensions/HttpResponseExtensions.cs",
        "Explore.Blazor.Client/Services/Http/BffClient.cs",
        // Phase 3 explicitly defers onboarding until its raw HTTP flow is split or migrated.
        "Explore.Blazor.Client/Services/InstanceOnboardingService.cs",
    };

    // --------------------------------------------------------------------------------------------
    // Framework-concrete types that are intentionally injected without an interface.
    // --------------------------------------------------------------------------------------------

    private static readonly HashSet<Type> FrameworkAllowedConcreteInjects =
    [
        typeof(NavigationManager),
        typeof(PersistentComponentState),
        typeof(Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider),
        typeof(HttpClient),
        typeof(TimeProvider),
        typeof(IHttpClientFactory),
    ];

    // --------------------------------------------------------------------------------------------
    // Method-name prefixes recognised as event handlers (allowed for `async void`).
    // --------------------------------------------------------------------------------------------

    private static readonly string[] EventHandlerNamePrefixes =
    {
        "On",     // Blazor lifecycle + MudBlazor callbacks (OnClick, OnInitialized, OnValidSubmit, …)
        "Handle", // DispatcherTimer / legacy event handlers
    };

    private static readonly HashSet<string> Known_AsyncVoid_Exceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Timer + list callbacks that cannot be Task-returning due to signature constraints.
        "Explore.Blazor.Client/Pages/Events/EventList.razor.cs:FlushPendingChanges",
        "Explore.Blazor.Client/Pages/Events/EventEdit.razor.cs:RemoveSession",
        "Explore.Blazor.Client/Pages/Events/CreateEvent.razor.cs:RemoveSession",
    };

    // ============================================================================================
    // RULE 1.1 — Components must not inject generated API clients directly.
    // ============================================================================================

    [Test]
    public async Task Rule_1_01_Components_MustNotInject_GeneratedApiClients_Directly()
    {
        if (BlazorClientRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Blazor.Client source not found — skipping");
            return;
        }

        var clientInterfaceNames = Explore.Blazor.Client.Extensions.GeneratedEventApiClients.ClientTypes
            .Select(c => c.InterfaceType.Name)
            .ToHashSet(StringComparer.Ordinal);

        var violations = new List<string>();
        var razorDirs = new[] { "Pages", "Shared" };

        foreach (var dir in razorDirs)
        {
            var searchPath = Path.Combine(BlazorClientRoot, dir);
            if (!Directory.Exists(searchPath)) continue;

            foreach (var file in Directory.EnumerateFiles(searchPath, "*.razor", SearchOption.AllDirectories))
            {
                if (IsGenerated(file)) continue;
                var content = await File.ReadAllTextAsync(file);
                foreach (Match match in Regex.Matches(content, @"@inject\s+(?<type>I\w+)\b", RegexOptions.CultureInvariant))
                {
                    var injectedType = match.Groups["type"].Value;
                    if (clientInterfaceNames.Contains(injectedType))
                    {
                        var relative = NormalisePath(Path.GetRelativePath(BlazorClientRoot, file));
                        if (!IsKnownComponentException(relative, Known_GeneratedApiClient_ComponentExceptions))
                        {
                            violations.Add($"{relative} (@inject {injectedType})");
                        }
                    }
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Components must not inject generated API clients directly — use a typed service in Services/. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.2 — No Console.WriteLine in Blazor production code (use ILogger).
    // ============================================================================================

    [Test]
    public async Task Rule_1_02_Blazor_MustNotUse_ConsoleWriteLine()
    {
        var violations = new List<string>();

        await ScanProjectAsync(BlazorHostRoot, "Explore.Blazor", includeRazor: true, (relative, content) =>
        {
            if (Regex.IsMatch(content, @"\bConsole\s*\.\s*WriteLine\s*\(", RegexOptions.CultureInvariant)
                && !Known_ConsoleWriteLine_Files.Contains(relative))
            {
                violations.Add(relative);
            }
        });

        await ScanProjectAsync(BlazorClientRoot, "Explore.Blazor.Client", includeRazor: true, (relative, content) =>
        {
            if (Regex.IsMatch(content, @"\bConsole\s*\.\s*WriteLine\s*\(", RegexOptions.CultureInvariant)
                && !Known_ConsoleWriteLine_Files.Contains(relative))
            {
                violations.Add(relative);
            }
        });

        await Assert.That(violations).IsEmpty()
            .Because($"Blazor code must log via ILogger, not Console.WriteLine. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.3 — No inline middleware lambdas >5 body lines in Explore.Blazor.
    // Extract to private static methods (Startup/MiddlewareExtensions pattern).
    // ============================================================================================

    [Test]
    public async Task Rule_1_03_Blazor_Host_MustNotHave_LongInlineMiddlewareLambdas()
    {
        if (BlazorHostRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Explore.Blazor source not found — skipping");
            return;
        }

        var violations = new List<string>();

        foreach (var file in EnumerateCsFiles(BlazorHostRoot))
        {
            var content = await File.ReadAllTextAsync(file);
            var lines = content.Split('\n');

            // Match `app.Use(async (...) =>` or `endpoints.Use(async (...) =>` opening lines.
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"\b(app|endpoints|builder)\b[^\n]*\.Use\w*\s*\(\s*async\s*\(", RegexOptions.CultureInvariant))
                    continue;

                // Walk forward, count body lines until matching closing brace at the correct depth.
                var depth = 0;
                var seenOpen = false;
                var bodyLines = 0;

                for (var j = i; j < lines.Length; j++)
                {
                    var line = lines[j];
                    foreach (var c in line)
                    {
                        if (c == '{') { depth++; seenOpen = true; }
                        else if (c == '}') { depth--; }
                    }
                    if (seenOpen) bodyLines++;
                    if (seenOpen && depth == 0) break;
                }

                if (bodyLines > 5)
                {
                    var relative = NormalisePath(Path.GetRelativePath(GetRepoRoot(BlazorHostRoot), file));
                    var key = $"{relative}:{i + 1}";
                    if (!Known_MiddlewareLambda_LongBodies.Contains(key))
                    {
                        violations.Add($"{key} (body {bodyLines} lines)");
                    }
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Inline middleware lambdas >5 body lines must be extracted to private static methods. Violations: {string.Join("; ", violations)}");
    }

    // ============================================================================================
    // RULE 1.4 — Interface injection is the default, with framework/state/interop patterns
    // and the exact scoped, stateless Local administration facade contract documented in BLAZOR.md.
    // Compiled identities prevent same-name types or additional consumers inheriting its permission.
    // ============================================================================================

    [Test]
    public async Task Rule_1_04_Components_MustInject_InterfacesOnly()
    {
        var violations = GetComponentInjections(typeof(LocalAccountsSection).Assembly.GetTypes())
            .Where(injection => !IsAllowedComponentInjection(injection.Consumer, injection.Property.PropertyType))
            .Select(injection => $"{injection.Consumer.FullName}.{injection.Property.Name}: {injection.Property.PropertyType.FullName}")
            .ToArray();

        await Assert.That(violations).IsEmpty()
            .Because($"Components must inject interfaces or a documented bounded concrete contract. Violations: {string.Join("; ", violations)}");
    }

    [Test]
    public async Task Rule_1_04_LocalAdministrationFacade_HasExactlyTheApprovedConsumers()
    {
        var consumers = GetComponentInjections(typeof(LocalAccountsSection).Assembly.GetTypes())
            .Where(injection => injection.Property.PropertyType == typeof(LocalIdentityAdministrationService))
            .Select(injection => injection.Consumer)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(consumers.SequenceEqual(new[]
        {
            typeof(InstanceAdminSettingsLayout), typeof(LocalAccountsSection)
        })).IsTrue();
    }

    [Test]
    public async Task Rule_1_04_LocalAdministrationFacade_MustBeScopedAndDependencyOnly()
    {
        var services = new ServiceCollection();
        services.AddSharedApplicationServices();

        await Assert.That(HasScopedLocalAdministrationRegistration(services)).IsTrue();
        await Assert.That(HasDependencyOnlyFacadeShape(typeof(LocalIdentityAdministrationService))).IsTrue()
            .Because("Only two private readonly dependency fields may survive a call; no credential, response, task, delegate or cache state is permitted.");
    }

    [Test]
    public async Task Rule_1_04_ConcreteInjectionGuard_RejectsUnapprovedAndImpersonatedConsumersAndServices()
    {
        await Assert.That(IsAllowedComponentInjection(typeof(LocalAccountsSection), typeof(LocalIdentityAdministrationService))).IsTrue();
        await Assert.That(IsAllowedComponentInjection(typeof(InstanceAdminSettingsLayout), typeof(LocalIdentityAdministrationService))).IsTrue();
        await Assert.That(IsAllowedComponentInjection(typeof(LocalAccountsSection), typeof(DependencyOnlyFacadeProbe))).IsFalse();
        await Assert.That(IsAllowedComponentInjection(typeof(LocalAccountsSection), typeof(Impostor.LocalIdentityAdministrationService))).IsFalse();
        await Assert.That(IsAllowedComponentInjection(typeof(Impostor.LocalAccountsSection), typeof(LocalIdentityAdministrationService))).IsFalse();

        var injected = GetComponentInjections(new[] { typeof(UnapprovedConsumer), typeof(InheritedConsumer) })
            .Where(injection => !IsAllowedComponentInjection(injection.Consumer, injection.Property.PropertyType))
            .ToArray();
        await Assert.That(injected.Length).IsEqualTo(2);
        await Assert.That(injected.All(injection => injection.Property.PropertyType == typeof(LocalIdentityAdministrationService))).IsTrue();
    }

    [Test]
    public async Task Rule_1_04_FacadeLifetimeGuard_RejectsWrongLifetimeFactoriesKeysAndDuplicates()
    {
        await Assert.That(HasScopedLocalAdministrationRegistration(new ServiceCollection())).IsFalse();
        foreach (var lifetime in new[] { ServiceLifetime.Singleton, ServiceLifetime.Transient })
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSharedApplicationServices();
            var registration = services.Single(descriptor => descriptor.ServiceType == typeof(LocalIdentityAdministrationService));
            services.Remove(registration);
            services.Add(ServiceDescriptor.Describe(typeof(LocalIdentityAdministrationService), typeof(LocalIdentityAdministrationService), lifetime));
            await Assert.That(HasScopedLocalAdministrationRegistration(services)).IsFalse();
        }

        var duplicate = new ServiceCollection();
        duplicate.AddSharedApplicationServices();
        duplicate.AddScoped<LocalIdentityAdministrationService>();
        await Assert.That(HasScopedLocalAdministrationRegistration(duplicate)).IsFalse();

        var factory = new ServiceCollection();
        factory.AddScoped<LocalIdentityAdministrationService>(_ => throw new InvalidOperationException("Probe factory must not execute."));
        await Assert.That(HasScopedLocalAdministrationRegistration(factory)).IsFalse();

        var keyed = new ServiceCollection();
        keyed.AddKeyedScoped<LocalIdentityAdministrationService>("probe");
        await Assert.That(HasScopedLocalAdministrationRegistration(keyed)).IsFalse();
    }

    [Test]
    public async Task Rule_1_04_FacadeStateGuard_RejectsCredentialResponseTaskAndStaticRetention()
    {
        await Assert.That(HasDependencyOnlyFacadeShape(typeof(DependencyOnlyFacadeProbe))).IsTrue();
        foreach (var retainedType in new[]
        {
            typeof(string), typeof(object), typeof(HalResourceOfLocalCredentialIssueDto),
            typeof(Task<HalResourceOfLocalCredentialIssueDto>), typeof(Action), typeof(Dictionary<Guid, string>)
        })
        {
            await Assert.That(HasDependencyOnlyFacadeShape(typeof(RetainingFacadeProbe<>).MakeGenericType(retainedType))).IsFalse();
            await Assert.That(HasDependencyOnlyFacadeShape(typeof(StaticRetainingFacadeProbe<>).MakeGenericType(retainedType))).IsFalse();
        }
        await Assert.That(HasDependencyOnlyFacadeShape(typeof(MutableDependencyFacadeProbe))).IsFalse();
        await Assert.That(HasDependencyOnlyFacadeShape(typeof(InheritedFacadeProbe))).IsFalse();
    }

    private static IEnumerable<(Type Consumer, PropertyInfo Property)> GetComponentInjections(IEnumerable<Type> types)
    {
        foreach (var consumer in types.Where(type => type.IsClass && typeof(IComponent).IsAssignableFrom(type)))
        {
            // Private base injection is inherited by the component even though GetProperties alone omits it.
            for (Type? declaring = consumer; declaring is not null; declaring = declaring.BaseType)
            {
                foreach (var property in declaring.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (property.IsDefined(typeof(InjectAttribute), inherit: false))
                        yield return (consumer, property);
                }
            }
        }
    }

    private static bool IsAllowedComponentInjection(Type consumer, Type service)
    {
        if (service == typeof(LocalIdentityAdministrationService))
            return (consumer == typeof(LocalAccountsSection) || consumer == typeof(InstanceAdminSettingsLayout))
                && HasDependencyOnlyFacadeShape(service);

        if (service.IsInterface || FrameworkAllowedConcreteInjects.Contains(service)) return true;
        string name = service.Name.Split('`')[0];
        return name.EndsWith("State", StringComparison.Ordinal)
            || name.EndsWith("StateService", StringComparison.Ordinal)
            || name.EndsWith("StateContainer", StringComparison.Ordinal)
            || name.EndsWith("Interop", StringComparison.Ordinal);
    }

    private static bool HasScopedLocalAdministrationRegistration(IServiceCollection services)
    {
        var registrations = services.Where(descriptor => descriptor.ServiceType == typeof(LocalIdentityAdministrationService)).ToArray();
        return registrations.Length == 1
            && !registrations[0].IsKeyedService
            && registrations[0].Lifetime == ServiceLifetime.Scoped
            && registrations[0].ImplementationType == typeof(LocalIdentityAdministrationService);
    }

    private static bool HasDependencyOnlyFacadeShape(Type facade)
    {
        var fields = facade.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        return facade.IsSealed && facade.BaseType == typeof(object)
            && fields.Length == 2
            && fields.All(field => field.IsPrivate && field.IsInitOnly && !field.IsStatic)
            && fields.Count(field => field.FieldType == typeof(ILocalIdentityAdministrationClient)) == 1
            && fields.Count(field => field.FieldType == typeof(IControlPlaneOverviewService)) == 1;
    }

    private class UnapprovedConsumer : ComponentBase
    {
        [Inject] private LocalIdentityAdministrationService Administration { get; set; } = default!;
    }

    private sealed class InheritedConsumer : UnapprovedConsumer;

    private static class Impostor
    {
        internal sealed class LocalIdentityAdministrationService;
        internal sealed class LocalAccountsSection : ComponentBase;
    }

    private sealed class DependencyOnlyFacadeProbe
    {
        public ILocalIdentityAdministrationClient Client { get; } = null!;
        public IControlPlaneOverviewService Overview { get; } = null!;
    }

    private sealed class RetainingFacadeProbe<T>
    {
        public ILocalIdentityAdministrationClient Client { get; } = null!;
        public IControlPlaneOverviewService Overview { get; } = null!;
        public T? Retained { get; }
    }

    private sealed class StaticRetainingFacadeProbe<T>
    {
        public ILocalIdentityAdministrationClient Client { get; } = null!;
        public IControlPlaneOverviewService Overview { get; } = null!;
        public static T? Retained { get; set; }
    }

    private sealed class MutableDependencyFacadeProbe
    {
        public ILocalIdentityAdministrationClient Client { get; set; } = null!;
        public IControlPlaneOverviewService Overview { get; } = null!;
    }

    private sealed class InheritedFacadeProbe : UnapprovedConsumer
    {
        public ILocalIdentityAdministrationClient Client { get; } = null!;
        public IControlPlaneOverviewService Overview { get; } = null!;
    }

    // ============================================================================================
    // RULE 1.5 — No `new DialogOptions()` literals. Use DialogOptionsFactory.
    // ============================================================================================

    [Test]
    public async Task Rule_1_05_MustNot_ConstructDialogOptions_Directly()
    {
        if (BlazorClientRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Blazor.Client source not found — skipping");
            return;
        }

        var violations = new List<string>();
        var repoRoot = GetRepoRoot(BlazorClientRoot);

        foreach (var file in EnumerateRazorAndCsFiles(BlazorClientRoot))
        {
            var relative = NormalisePath(Path.GetRelativePath(repoRoot, file));

            // Skip the factory itself.
            if (relative.EndsWith("DialogOptionsFactory.cs", StringComparison.OrdinalIgnoreCase)) continue;

            var content = await File.ReadAllTextAsync(file);
            if (Regex.IsMatch(content, @"\bnew\s+DialogOptions\s*(\(\s*\)|\{)", RegexOptions.CultureInvariant))
            {
                if (!Known_NewDialogOptions_Files.Contains(relative))
                {
                    violations.Add(relative);
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Use DialogOptionsFactory instead of `new DialogOptions()`. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.6 — Shared components (Components/Common, Components/Collection) must not depend
    // on NavigationManager. Navigation is a page/layout concern; bubble via EventCallback.
    // ============================================================================================

    [Test]
    public async Task Rule_1_06_SharedComponents_MustNotInject_NavigationManager()
    {
        if (BlazorClientRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Blazor.Client source not found — skipping");
            return;
        }

        var violations = new List<string>();
        var sharedRoots = new[]
        {
            Path.Combine(BlazorClientRoot, "Components", "Common"),
            Path.Combine(BlazorClientRoot, "Components", "Collection"),
        };

        foreach (var root in sharedRoots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var file in EnumerateRazorAndCsFiles(root))
            {
                var content = await File.ReadAllTextAsync(file);
                if (Regex.IsMatch(content, @"(@inject\s+NavigationManager\b|\[Inject\][^;]*?\bNavigationManager\b)", RegexOptions.CultureInvariant))
                {
                    violations.Add(NormalisePath(Path.GetRelativePath(BlazorClientRoot, file)));
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Shared components must not depend on NavigationManager — raise EventCallback instead. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.7 — IJSRuntime is only permitted in Services/Interop/ or Services/Http/
    // (plus *Interop-suffixed files). Other services must delegate through an interop wrapper.
    // ============================================================================================

    [Test]
    public async Task Rule_1_07_Services_MustNotUse_IJSRuntime_OutsideInterop()
    {
        if (BlazorClientRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Blazor.Client source not found — skipping");
            return;
        }

        var servicesDir = Path.Combine(BlazorClientRoot, "Services");
        if (!Directory.Exists(servicesDir))
        {
            await Assert.That(true).IsTrue().Because("Services/ not present");
            return;
        }

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(servicesDir, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file)) continue;
            var relative = NormalisePath(Path.GetRelativePath(BlazorClientRoot, file));
            var fullRelative = "Explore.Blazor.Client/" + relative;

            // Allowed: inside Interop/ or Http/ subfolders.
            if (relative.Contains("/Interop/", StringComparison.OrdinalIgnoreCase)
                || relative.Contains("/Http/", StringComparison.OrdinalIgnoreCase))
                continue;
            // Allowed: filename ending with Interop.cs (e.g. CookieConsentInterop.cs).
            if (relative.EndsWith("Interop.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = await File.ReadAllTextAsync(file);
            if (Regex.IsMatch(content, @"\bIJSRuntime\b", RegexOptions.CultureInvariant))
            {
                if (!Known_IJSRuntimeInServices_Files.Contains(fullRelative))
                {
                    violations.Add(fullRelative);
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"IJSRuntime is restricted to Services/Interop/, Services/Http/, or *Interop-suffixed files. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.8 — Data service classes (not UI helpers) must not inject ISnackbar.
    // ============================================================================================

    [Test]
    public async Task Rule_1_08_DataServices_MustNotInject_ISnackbar()
    {
        if (BlazorClientRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Blazor.Client source not found — skipping");
            return;
        }

        var servicesDir = Path.Combine(BlazorClientRoot, "Services");
        if (!Directory.Exists(servicesDir))
        {
            await Assert.That(true).IsTrue().Because("Services/ not present");
            return;
        }

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(servicesDir, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file)) continue;
            var relative = NormalisePath(Path.GetRelativePath(BlazorClientRoot, file));
            // Allow UI-facing helpers under Services/UserInterface/ or Services/Notifications/.
            if (relative.Contains("/Notifications/", StringComparison.OrdinalIgnoreCase)
                || relative.Contains("/UserInterface/", StringComparison.OrdinalIgnoreCase)
                || relative.Contains("/UI/", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = await File.ReadAllTextAsync(file);
            if (Regex.IsMatch(content, @"\bISnackbar\b", RegexOptions.CultureInvariant))
            {
                violations.Add("Explore.Blazor.Client/" + relative);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Data services must not inject ISnackbar — raise errors and let callers surface them. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.9 — Services under Explore.Blazor/Services/ must not hold per-request/per-user
    // mutable state (non-static, non-ImmutableXxx collection fields) when they could be
    // registered as singletons. Heuristic: flag `private readonly (HashSet|List|Queue|Stack|Dictionary|ConcurrentDictionary)<...>` instance fields.
    // ============================================================================================

    [Test]
    public async Task Rule_1_09_HostServices_MustNotHold_MutableCollectionFields_OnSingletons()
    {
        if (BlazorHostRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Explore.Blazor source not found — skipping");
            return;
        }

        var servicesDir = Path.Combine(BlazorHostRoot, "Services");
        if (!Directory.Exists(servicesDir))
        {
            await Assert.That(true).IsTrue().Because("Services/ not present");
            return;
        }

        var violations = new List<string>();
        var mutableCollectionField = new Regex(
            @"^\s*private\s+readonly\s+(?:HashSet|List|Queue|Stack|Dictionary|ConcurrentDictionary|ConcurrentBag|ConcurrentQueue|SortedSet)\s*<",
            RegexOptions.CultureInvariant | RegexOptions.Multiline);

        foreach (var file in Directory.EnumerateFiles(servicesDir, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file)) continue;
            var relative = "Explore.Blazor/" + NormalisePath(Path.GetRelativePath(BlazorHostRoot, file));
            var content = await File.ReadAllTextAsync(file);

            if (mutableCollectionField.IsMatch(content))
            {
                if (!Known_MutableStateSingleton_Files.Contains(relative))
                {
                    violations.Add(relative);
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Host singletons must not hold non-static mutable collection fields. Use ImmutableHashSet/ImmutableDictionary snapshots. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.10 — `async void` is only permitted for event-handler-shaped methods.
    // ============================================================================================

    [Test]
    public async Task Rule_1_10_AsyncVoid_OnlyAllowed_ForEventHandlers()
    {
        var violations = new List<string>();
        var asyncVoidRegex = new Regex(
            @"\b(?:private|public|protected|internal)\s+(?:static\s+)?async\s+void\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.CultureInvariant);

        await ScanProjectAsync(BlazorClientRoot, "Explore.Blazor.Client", includeRazor: true, (relative, content) =>
        {
            foreach (Match m in asyncVoidRegex.Matches(content))
            {
                var name = m.Groups["name"].Value;
                if (IsEventHandlerName(name)) continue;
                var key = $"{relative}:{name}";
                if (!Known_AsyncVoid_Exceptions.Contains(key))
                {
                    violations.Add(key);
                }
            }
        });

        await ScanProjectAsync(BlazorHostRoot, "Explore.Blazor", includeRazor: true, (relative, content) =>
        {
            foreach (Match m in asyncVoidRegex.Matches(content))
            {
                var name = m.Groups["name"].Value;
                if (IsEventHandlerName(name)) continue;
                var key = $"{relative}:{name}";
                if (!Known_AsyncVoid_Exceptions.Contains(key))
                {
                    violations.Add(key);
                }
            }
        });

        await Assert.That(violations).IsEmpty()
            .Because($"`async void` is only permitted for event handlers (On* / Handle*). Violations: {string.Join("; ", violations)}");
    }

    // ============================================================================================
    // RULE 1.11 — No sync-over-async in Blazor projects.
    // Detects `.Result` / `.Wait()` after an obvious Task boundary (`Async(…)` call or `Task.Run`).
    // ============================================================================================

    [Test]
    public async Task Rule_1_11_Blazor_MustNotUse_SyncOverAsync()
    {
        var violations = new List<string>();
        // Matches: xxxAsync(...).Result    or    xxxAsync(...).Wait()
        //          Task.Run(...).Result     or    Task.Run(...).Wait()
        //          Task.FromResult(...).Result is allowed — it's sync materialisation.
        var syncOverAsync = new Regex(
            @"(?:Async\s*\([^;]*?\)|Task\s*\.\s*Run\s*\([^;]*?\))\s*\.\s*(?:Result|Wait\s*\(\s*\))\b",
            RegexOptions.CultureInvariant);

        await ScanProjectAsync(BlazorClientRoot, "Explore.Blazor.Client", includeRazor: true, (relative, content) =>
        {
            if (syncOverAsync.IsMatch(content)) violations.Add(relative);
        });

        await ScanProjectAsync(BlazorHostRoot, "Explore.Blazor", includeRazor: true, (relative, content) =>
        {
            if (syncOverAsync.IsMatch(content)) violations.Add(relative);
        });

        await Assert.That(violations).IsEmpty()
            .Because($"Never block on tasks in Blazor — use `await`. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.12 — Components and service classes must consume configuration via IOptions<T>,
    // not IConfiguration directly. Bootstrap extensions/Program.cs remain free to use IConfiguration.
    // ============================================================================================

    [Test]
    public async Task Rule_1_12_Services_MustNotInject_IConfiguration_Directly()
    {
        var violations = new List<string>();

        await ScanProjectAsync(BlazorHostRoot, "Explore.Blazor", includeRazor: false, (relative, content) =>
        {
            // Only flag files under Services/ or Components/.
            if (!relative.Contains("/Services/", StringComparison.OrdinalIgnoreCase)
                && !relative.Contains("/Components/", StringComparison.OrdinalIgnoreCase))
                return;

            if (Regex.IsMatch(content, @"(\[Inject\][^;]*?\bIConfiguration\b|@inject\s+IConfiguration\b|\bIConfiguration\s+\w+\s*[,)])", RegexOptions.CultureInvariant))
            {
                if (!Known_IConfigurationInjection_Files.Contains(relative))
                {
                    violations.Add(relative);
                }
            }
        });

        await ScanProjectAsync(BlazorClientRoot, "Explore.Blazor.Client", includeRazor: true, (relative, content) =>
        {
            if (!relative.Contains("/Services/", StringComparison.OrdinalIgnoreCase)
                && !relative.Contains("/Components/", StringComparison.OrdinalIgnoreCase)
                && !relative.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                && !relative.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
                return;

            if (Regex.IsMatch(content, @"(\[Inject\][^;]*?\bIConfiguration\b|@inject\s+IConfiguration\b|\bIConfiguration\s+\w+\s*[,)])", RegexOptions.CultureInvariant))
            {
                if (!Known_IConfigurationInjection_Files.Contains(relative))
                {
                    violations.Add(relative);
                }
            }
        });

        await Assert.That(violations).IsEmpty()
            .Because($"Services/components must use IOptions<T> (or IOptionsMonitor<T>) instead of IConfiguration. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.13 — Client-side code must not resolve services via GetRequiredService<T>().
    // Server extensions/middleware composition and Program.cs are allowed (DI factory pattern).
    // ============================================================================================

    [Test]
    public async Task Rule_1_13_Client_MustNotUse_ServiceLocator()
    {
        if (BlazorClientRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Blazor.Client source not found — skipping");
            return;
        }

        var violations = new List<string>();

        foreach (var file in EnumerateRazorAndCsFiles(BlazorClientRoot))
        {
            var relative = NormalisePath(Path.GetRelativePath(BlazorClientRoot, file));
            // Allowed: Program.cs (WASM bootstrap) + any Extensions/ folder.
            if (relative.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.StartsWith("Extensions/", StringComparison.OrdinalIgnoreCase)) continue;

            var content = await File.ReadAllTextAsync(file);
            if (Regex.IsMatch(content, @"\bGetRequiredService\s*<", RegexOptions.CultureInvariant))
            {
                violations.Add("Explore.Blazor.Client/" + relative);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Blazor.Client must use constructor/[Inject] DI, not GetRequiredService<T>(). Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.14 — Pure interface files under Contracts/ (I{Name}.cs) must not declare models.
    // The repo convention is that service impls and their DTOs live together in {Name}Service.cs;
    // interface-only files belong under Contracts/ and must stay interface-only. If Contracts/
    // does not yet exist (current state), the rule passes trivially and will activate once the
    // Wave A Phase 2 extraction creates that folder.
    // ============================================================================================

    [Test]
    public async Task Rule_1_14_InterfaceFiles_MustNotDeclare_ModelTypes()
    {
        if (BlazorClientRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Blazor.Client source not found — skipping");
            return;
        }

        var contractsDir = Path.Combine(BlazorClientRoot, "Contracts");
        if (!Directory.Exists(contractsDir))
        {
            await Assert.That(true).IsTrue().Because("Contracts/ not yet present — rule activates after Wave A Phase 2 extraction");
            return;
        }

        var violations = new List<string>();
        var nonInterfaceTypeRegex = new Regex(@"^\s*public\s+(?:sealed\s+|abstract\s+|partial\s+|static\s+)*(?:class|record|struct|enum)\s+\w+", RegexOptions.CultureInvariant | RegexOptions.Multiline);

        foreach (var file in Directory.EnumerateFiles(contractsDir, "I*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file)) continue;
            var fileName = Path.GetFileName(file);
            // Require capital-I + uppercase letter so `ImageStorageService.cs` (Im…) is excluded.
            if (fileName.Length < 2 || !char.IsUpper(fileName[1])) continue;

            var relative = NormalisePath(Path.GetRelativePath(BlazorClientRoot, file));
            var key = "Explore.Blazor.Client/" + relative;
            if (Known_ModelTypesInInterfaceFile_Files.Contains(key)) continue;

            var content = await File.ReadAllTextAsync(file);
            if (nonInterfaceTypeRegex.IsMatch(content))
            {
                violations.Add(key);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Pure interface files under Contracts/I*.cs must not declare model types. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.15 — Do not use magic strings to resolve BffClient manually via IHttpClientFactory.
    // ============================================================================================

    [Test]
    public async Task Rule_1_15_Services_MustNotUse_IHttpClientFactory_For_BffClient()
    {
        if (BlazorClientRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Blazor.Client source not found — skipping");
            return;
        }

        var violations = new List<string>();

        foreach (var file in EnumerateRazorAndCsFiles(BlazorClientRoot))
        {
            var relative = NormalisePath(Path.GetRelativePath(BlazorClientRoot, file));
            var fullRelative = "Explore.Blazor.Client/" + relative;

            var content = await File.ReadAllTextAsync(file);
            if (content.Contains("CreateClient(\"BffClient\")"))
            {
                // We're actively migrating these. Do not add new ones.
                if (!Known_BffClient_Exceptions.Contains(fullRelative))
                {
                    violations.Add(fullRelative);
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Use typed Refit clients instead of IHttpClientFactory.CreateClient(\"BffClient\"). Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.16 — Refit clients must be registered securely using AddBffRefitClient<T>().
    // ============================================================================================

    [Test]
    public async Task Rule_1_16_RefitClients_MustUse_AddBffRefitClient_Registration()
    {
        if (BlazorClientRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Blazor.Client source not found — skipping");
            return;
        }

        var violations = new List<string>();
        var programCsPath = Path.Combine(BlazorClientRoot, "Program.cs");
        if (File.Exists(programCsPath))
        {
            var content = await File.ReadAllTextAsync(programCsPath);
            // Look for raw AddRefitClient usage (ignoring AddBffRefitClient)
            if (Regex.IsMatch(content, @"(?<!AddBff)AddRefitClient\s*<", RegexOptions.CultureInvariant))
            {
                violations.Add("Explore.Blazor.Client/Program.cs");
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Refit clients targeting the BFF must be registered using AddBffRefitClient<T>() to guarantee antiforgery and credentials. Violations: {string.Join(", ", violations)}");
    }

    // ============================================================================================
    // RULE 1.17 — Raw HTTP JSON/success-status helpers stay behind approved low-level boundaries.
    // ============================================================================================

    [Test]
    public async Task Rule_1_17_RawHttpJsonHelpers_MustStayIn_ApprovedBoundaries()
    {
        if (BlazorClientRoot is null)
        {
            await Assert.That(true).IsTrue().Because("Blazor.Client source not found — skipping");
            return;
        }

        var violations = new List<string>();
        var forbiddenPatterns = new[]
        {
            "ReadFromJsonAsync",
            "GetFromJsonAsync",
            "EnsureSuccessStatusCode"
        };

        foreach (var file in EnumerateRazorAndCsFiles(BlazorClientRoot))
        {
            var relative = NormalisePath(Path.GetRelativePath(BlazorClientRoot, file));
            var fullRelative = "Explore.Blazor.Client/" + relative;
            if (Known_RawHttpJson_Exceptions.Contains(fullRelative))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(file);
            if (forbiddenPatterns.Any(pattern => content.Contains(pattern, StringComparison.Ordinal)))
            {
                violations.Add(fullRelative);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Use IApiClientExecutor, generated clients, or approved low-level HTTP helpers instead of raw HTTP JSON/status helpers. Violations: {string.Join(", ", violations)}");
    }

    // --------------------------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------------------------

    private static async Task ScanProjectAsync(string? projectRoot, string projectName, bool includeRazor, Action<string, string> visitor)
    {
        if (projectRoot is null) return;
        var repoRoot = GetRepoRoot(projectRoot);

        foreach (var file in EnumerateCsFiles(projectRoot))
        {
            var relative = NormalisePath(Path.GetRelativePath(repoRoot, file));
            var content = await File.ReadAllTextAsync(file);
            visitor(relative, content);
        }

        if (includeRazor)
        {
            foreach (var file in Directory.EnumerateFiles(projectRoot, "*.razor", SearchOption.AllDirectories))
            {
                if (IsGenerated(file)) continue;
                var relative = NormalisePath(Path.GetRelativePath(repoRoot, file));
                var content = await File.ReadAllTextAsync(file);
                visitor(relative, content);
            }
        }
    }

    private static IEnumerable<string> EnumerateCsFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f));

    private static IEnumerable<string> EnumerateRazorAndCsFiles(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                        && !IsGenerated(f));

    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);

    private static string NormalisePath(string path) => path.Replace('\\', '/');

    private static bool IsEventHandlerName(string name) =>
        EventHandlerNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private static bool IsKnownComponentException(string relativePath, HashSet<string> exceptions) =>
        exceptions.Any(ex => relativePath.EndsWith(ex.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));

    private static string GetRepoRoot(string projectRoot) =>
        Path.GetDirectoryName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;

    private static string? ResolveProjectRoot(string projectName)
    {
        var candidate = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            candidate = Path.GetDirectoryName(candidate);
            if (candidate is null) break;

            var target = Path.Combine(candidate, projectName);
            if (Directory.Exists(target)) return target;
            target = Path.Combine(candidate, "src", projectName);
            if (Directory.Exists(target)) return target;
            target = Path.Combine(candidate, "tests", projectName);
            if (Directory.Exists(target)) return target;
        }
        return null;
    }
}
