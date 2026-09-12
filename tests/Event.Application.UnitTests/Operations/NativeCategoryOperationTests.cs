using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Category;
using Explore.Application.Features.Categories.Handlers.Commands;
using Explore.Application.Features.Categories.Handlers.Queries;
using Explore.Application.Features.Categories.Requests.Commands;
using Explore.Application.Features.Categories.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeCategoryOperationTests
{
    [Test]
    public async Task CategoryCapability_RegistersThreeCommandsAndTwoQueries()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(CreateCategoryCommand), typeof(CreateCategoryCommandHandler),
            typeof(UpdateCategoryCommand), typeof(UpdateCategoryCommandHandler),
            typeof(DeleteCategoryCommand), typeof(DeleteCategoryCommandHandler),
            typeof(GetCategoryDetailsRequest), typeof(GetCategoryDetailsRequestHandler),
            typeof(GetCategoryListRequest), typeof(GetCategoryListRequestHandler)
        ]);

        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(5);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        var commands = ports.Select(port => port.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
            .ToArray();
        var queries = ports.Select(port => port.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(commands.Select(type => type.GetGenericArguments()[0]))
            .IsEquivalentTo(new[]
            {
                typeof(CreateCategoryCommand), typeof(UpdateCategoryCommand), typeof(DeleteCategoryCommand)
            });
        await Assert.That(queries.Select(type => type.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetCategoryDetailsRequest), typeof(GetCategoryListRequest) });

        await Assert.That(commands.Single(type => type.GetGenericArguments()[0] == typeof(CreateCategoryCommand))
            .GetGenericArguments()[1]).IsEqualTo(typeof(BaseCommandResponse<Guid>));
        await Assert.That(commands.Single(type => type.GetGenericArguments()[0] == typeof(UpdateCategoryCommand))
            .GetGenericArguments()[1]).IsEqualTo(typeof(BaseCommandResponse<Guid>));
        await Assert.That(commands.Single(type => type.GetGenericArguments()[0] == typeof(DeleteCategoryCommand))
            .GetGenericArguments()[1]).IsEqualTo(typeof(bool));
        await Assert.That(queries.Single(type => type.GetGenericArguments()[0] == typeof(GetCategoryDetailsRequest))
            .GetGenericArguments()[1]).IsEqualTo(typeof(CategoryDto));
        await Assert.That(queries.Single(type => type.GetGenericArguments()[0] == typeof(GetCategoryListRequest))
            .GetGenericArguments()[1]).IsEqualTo(typeof(PaginatedResult<CategoryListDto>));
    }

    [Test]
    public async Task MissingDetail_DeclaresNullableHandlerResult()
    {
        var method = typeof(GetCategoryDetailsRequestHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.ReturnType == typeof(Task<CategoryDto>));
        var result = new NullabilityInfoContext().Create(method.ReturnParameter)
            .GenericTypeArguments.Single();

        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
