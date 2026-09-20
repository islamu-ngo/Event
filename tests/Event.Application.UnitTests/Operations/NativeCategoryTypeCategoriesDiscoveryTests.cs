using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Features.CategoryTypeCategories.Handlers.Commands;
using Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;
using Explore.Application.Features.CategoryTypeCategories.Requests.Commands;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeCategoryTypeCategoriesDiscoveryTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersAllEightCorrectlyClassifiedPorts()
    {
        (Type Request, Type Handler, Type Port, Type Result)[] operations =
        [
            (typeof(CreateCategoryTypeCategoriesCommand), typeof(CreateCategoryTypeCategoriesCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateCategoryTypeCategoriesCommand), typeof(UpdateCategoryTypeCategoriesCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(DeleteCategoryTypeCategoriesCommand), typeof(DeleteCategoryTypeCategoriesCommandHandler),
                typeof(ICommandHandler<,>), typeof(bool)),
            (typeof(GetCategoriesByCategoryTypeRequest), typeof(GetCategoriesByCategoryTypeRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<CategoryListDto>)),
            (typeof(GetCategoriesGroupedByCategoryTypeRequest), typeof(GetCategoriesGroupedByCategoryTypeRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<CategoryTypeWithCategoriesDto>)),
            (typeof(GetCategoryTypeCategoriesDetailsRequest), typeof(GetCategoryTypeCategoriesDetailsRequestHandler),
                typeof(IQueryHandler<,>), typeof(CategoryTypeCategoriesDto)),
            (typeof(GetCategoryTypeCategoriesListRequest), typeof(GetCategoryTypeCategoriesListRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<CategoryTypeCategoriesListDto>)),
            (typeof(GetCategoryTypesForCategoryRequest), typeof(GetCategoryTypesForCategoryRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<CategoryTypeListDto>))
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(8);
        foreach (var operation in operations)
        {
            var port = ports.Single(type => type.GetGenericArguments()[0] == operation.Request);
            await Assert.That(port.GetGenericTypeDefinition()).IsEqualTo(operation.Port);
            await Assert.That(port.GetGenericArguments()[1]).IsEqualTo(operation.Result);
        }
    }

    [Test]
    public async Task MissingRelationship_DeclaresNullableHandlerResult()
    {
        var method = typeof(GetCategoryTypeCategoriesDetailsRequestHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.ReturnType == typeof(Task<CategoryTypeCategoriesDto>));
        var result = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single();

        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
