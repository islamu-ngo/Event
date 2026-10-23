using Explore.API.Hateoas;
using Explore.API.OpenApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class ModerationNoticeOpenApiSecurityTests
{
    [Test]
    [Arguments(RouteNames.GetModerationNotices)]
    [Arguments(RouteNames.GetModerationNotice)]
    [Arguments(RouteNames.AcknowledgeModerationNotice)]
    [Arguments(RouteNames.GetModerationNoticeStatus)]
    [Arguments(RouteNames.GetModerationContentResponseAvailability)]
    [Arguments(RouteNames.SubmitModerationContentResponse)]
    [Arguments(RouteNames.GetModerationContentResponseStatement)]
    [Arguments(RouteNames.GetModerationContentResponses)]
    public async Task Dedicated_notice_operations_require_human_bearer_authentication_even_for_allow_anonymous_reads(string route)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var document = new OpenApiDocument();
        var operation = new OpenApiOperation();
        var context = new OpenApiOperationTransformerContext
        {
            DocumentName = "v0.1", Document = document, ApplicationServices = services,
            Description = new ApiDescription
            {
                ActionDescriptor = new ActionDescriptor
                {
                    EndpointMetadata = [new AllowAnonymousAttribute(), new HttpGetAttribute { Name = route }]
                }
            }
        };
        await new ModerationNoticeOpenApiSecurityTransformer().TransformAsync(operation, context, CancellationToken.None);
        await Assert.That(operation.Security!.Count).IsEqualTo(1);
        await Assert.That(operation.Security.Single().Keys.Single().Reference.Id)
            .IsEqualTo(JwtBearerDefaults.AuthenticationScheme);
        await Assert.That(document.Components!.SecuritySchemes!.Count).IsEqualTo(1);
        await Assert.That(document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme].Type)
            .IsEqualTo(SecuritySchemeType.Http);
    }

    [Test]
    public async Task Unrelated_anonymous_operations_keep_their_original_security_contract()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var document = new OpenApiDocument();
        var operation = new OpenApiOperation();
        await new ModerationNoticeOpenApiSecurityTransformer().TransformAsync(operation,
            new OpenApiOperationTransformerContext
            {
                DocumentName = "v0.1", Document = document, ApplicationServices = services,
                Description = new ApiDescription
                {
                    ActionDescriptor = new ActionDescriptor
                    {
                        EndpointMetadata = [new AllowAnonymousAttribute(), new HttpGetAttribute { Name = "UnrelatedRead" }]
                    }
                }
            }, CancellationToken.None);
        await Assert.That(operation.Security is null || operation.Security.Count == 0).IsTrue();
        await Assert.That(document.Components?.SecuritySchemes?.Count ?? 0).IsEqualTo(0);
    }
}
