using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.TenantUsers.Handlers.Commands;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Mono.Cecil;

namespace Event.Architecture.Tests;

public sealed class EventLocationPrivacyArchitectureTests
{
    private static readonly Type[] GlobalAuthorities =
    [
        typeof(DeleteUserCommand), typeof(IUserRepository), typeof(ILocationRepository),
        typeof(IUserLocationPrivacyErasureRepository), typeof(IUserPrivacyErasureRepository),
        typeof(IPrivacyErasureAuthority), typeof(IPrivacyErasureService), typeof(IPrivacyErasureReplayService)
    ];

    [Test]
    public async Task TenantMembershipFeature_MustNotReferenceGlobalDeletionOrHomeErasureAuthority()
    {
        using var assembly = CompiledDependencyGraph.ReadProject("Explore.Application");
        var violations = assembly.MainModule.Types
            .Where(type => type.Namespace == "Explore.Application.Features.TenantUsers"
                || type.Namespace.StartsWith("Explore.Application.Features.TenantUsers.", StringComparison.Ordinal))
            .Where(HasGlobalAuthorityDependency)
            .Select(type => type.FullName).ToArray();
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task MembershipHandler_MustUseOnlyTenantScopedRepositories()
    {
        var dependencies = typeof(RemoveTenantMembershipCommandHandler).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        await Assert.That(dependencies).IsEquivalentTo([
            typeof(ITenantUserRepository), typeof(ITenantUserRoleGrantRepository), typeof(IUnitOfWork),
            typeof(ITenantContext), typeof(ICurrentUserService), typeof(TimeProvider),
            typeof(Func<IHumanSecuritySourceGate>)]);
    }

    [Test]
    public async Task MembershipMutation_MustNotBypassTenantOrQueryFilters()
    {
        using var assembly = CompiledDependencyGraph.ReadProject("Explore.Persistence");
        var repository = assembly.MainModule.Types.Single(type => type.FullName == typeof(TenantUserRepository).FullName);
        var mutation = repository.Methods.Single(method => method.Name == nameof(TenantUserRepository.TryRemoveMembershipAsync));
        var violations = CompiledDependencyGraph.ReachableBodies(mutation)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand).OfType<MethodReference>()
            .Where(IsFilterBypass).Select(method => method.FullName).ToArray();
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task MembershipBoundary_RejectsBodyOnlyGlobalAuthorityAndNestedQueryFilterBypass()
    {
        using var assembly = CompiledDependencyGraph.ReadAssembly(typeof(EventLocationPrivacyArchitectureTests).Assembly.Location);
        var probes = CompiledDependencyGraph.Types(assembly.MainModule)
            .Where(type => type.DeclaringType?.FullName == typeof(EventLocationPrivacyArchitectureTests).FullName)
            .ToDictionary(type => type.Name, StringComparer.Ordinal);
        await Assert.That(HasGlobalAuthorityDependency(probes[nameof(GlobalAuthorityProbe)])).IsTrue();
        await Assert.That(HasGlobalAuthorityDependency(probes[nameof(AttributeAuthorityProbe)])).IsTrue();
        await Assert.That(HasGlobalAuthorityDependency(probes[nameof(AttributeArrayAuthorityProbe)])).IsTrue();
        await Assert.That(HasGlobalAuthorityDependency(probes[nameof(AttributeNamedAuthorityProbe)])).IsTrue();
        await Assert.That(HasGlobalAuthorityDependency(probes[nameof(UnrelatedProbe)])).IsFalse();
        var entry = probes[nameof(FilterBypassProbe)].Methods.Single(method => method.Name == nameof(FilterBypassProbe.Mutate));
        await Assert.That(CompiledDependencyGraph.ReachableBodies(entry)
            .Where(method => method.HasBody).SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand).OfType<MethodReference>().Any(IsFilterBypass)).IsTrue();
        var nested = probes[nameof(GenericFilterBypassProbe)].Methods.Single(method => method.Name == nameof(GenericFilterBypassProbe.Mutate));
        await Assert.That(CompiledDependencyGraph.ReachableBodies(nested)
            .Where(method => method.HasBody).SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand).OfType<MethodReference>().Any(IsFilterBypass)).IsTrue();
        await Assert.That(CompiledDependencyGraph.Members(probes[nameof(NativeAllFiltersProbe)])
            .OfType<MethodReference>().Any(IsFilterBypass)).IsTrue();
    }

    private static bool HasGlobalAuthorityDependency(TypeDefinition type) =>
        CompiledDependencyGraph.Dependencies(type)
            .Any(dependency => GlobalAuthorities.Any(authority => authority.FullName == dependency.FullName));

    private static bool IsFilterBypass(MethodReference method) =>
        method.DeclaringType.FullName == typeof(QueryFilterExtensions).FullName
            && method.Name is nameof(QueryFilterExtensions.IgnoreTenantFilter) or nameof(QueryFilterExtensions.IgnoreAllFilters)
        || method.DeclaringType.FullName == typeof(EntityFrameworkQueryableExtensions).FullName
            && method.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters);

    private static class GlobalAuthorityProbe
    {
        public static Func<Type> Dependency() => () => typeof(DeleteUserCommand);
    }

    private static class FilterBypassProbe
    {
        public static async Task<IQueryable<T>> Mutate<T>(IQueryable<T> query) where T : class
        {
            await Task.Yield();
            return query.IgnoreQueryFilters();
        }
    }

    private static class UnrelatedProbe
    {
        public static Type Dependency() => typeof(ITenantUserRepository);
    }

    private static class GenericFilterBypassProbe
    {
        public static async Task<IQueryable<object>> Mutate(IQueryable<object> query)
        {
            await Task.Yield();
            return Helper<object>.Apply(query);
        }

        private static class Helper<T> where T : class
        {
            public static IQueryable<T> Apply(IQueryable<T> query) => query.IgnoreQueryFilters();
        }
    }

    private static class NativeAllFiltersProbe
    {
        public static IQueryable<object> Mutate(IQueryable<object> query) =>
            query.IgnoreAllFilters("Compiled membership-boundary calibration.");
    }

    [TypeDependency(typeof(DeleteUserCommand))]
    private sealed class AttributeAuthorityProbe
    {
    }

    [TypeDependency(new Type[] { typeof(DeleteUserCommand) })]
    private sealed class AttributeArrayAuthorityProbe
    {
    }

    [TypeDependency(typeof(string), Named = typeof(DeleteUserCommand), Others = new Type[] { typeof(DeleteUserCommand) })]
    private sealed class AttributeNamedAuthorityProbe
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    private sealed class TypeDependencyAttribute(Type type) : Attribute
    {
        public TypeDependencyAttribute(Type[] types) : this(typeof(string)) => Others = types;

        public Type Value { get; } = type;
        public Type[] Others { get; set; } = [];
        public Type? Named { get; set; }
    }
}
