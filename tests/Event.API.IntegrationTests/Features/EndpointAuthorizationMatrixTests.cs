using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class EndpointAuthorizationMatrixTests
{
    private static readonly HashSet<string> ScopedOptionalQueryParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "actorId",
        "eventId",
        "eventSessionId",
        "eventTemplateId",
        "locationId",
        "tenantId"
    };

    private readonly ApiTestFixture _fixture;

    public EndpointAuthorizationMatrixTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task EveryDiscoveredRouteVerbHasOneAuthorizationClassification()
    {
        IReadOnlyList<ApiDescription> endpoints = GetApiDescriptions();
        var failures = new List<string>();

        foreach (IGrouping<string, ApiDescription> duplicate in endpoints
            .GroupBy(
                endpoint =>
                    $"{endpoint.HttpMethod} {endpoint.RelativePath}",
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() != 1))
        {
            failures.Add(
                $"{duplicate.Key}: discovered {duplicate.Count()} times");
        }

        foreach (ApiDescription endpoint in endpoints)
        {
            var metadata = endpoint.ActionDescriptor.EndpointMetadata;
            bool hasAllowAnonymous = metadata.OfType<IAllowAnonymous>().Any();
            bool hasAuthorizationRequirement =
                metadata.OfType<IAuthorizeData>().Any()
                || metadata.OfType<SetupSecretRequiredAttribute>().Any();

            if (!hasAllowAnonymous && !hasAuthorizationRequirement)
            {
                failures.Add(
                    $"{endpoint.HttpMethod} {endpoint.RelativePath}: "
                    + "authorization classification is missing");
            }
        }

        await Assert.That(endpoints).IsNotEmpty();
        await Assert.That(failures).IsEmpty()
            .Because("every discovered route/verb must be unique and classified exactly once");
    }

    [Test]
    public async Task Public_Get_Endpoints_ReturnOk()
    {
        var endpoints = GetApiDescriptions()
            .Where(description => IsHttpMethod(description, HttpMethod.Get))
            .Where(description => !IsProtected(description))
            .Where(description => !IsPublicSmokeException(description));

        var failures = new List<string>();

        foreach (var description in endpoints)
        {
            var path = BuildPath(description);
            Console.WriteLine($"Testing endpoint: {path}");
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

            var response = await _fixture.Client.SendAsync(request);

            var hasPathParams = description.ParameterDescriptions.Any(p => p.Source == BindingSource.Path);
            var isSuccess = response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent
                || (response.StatusCode == HttpStatusCode.NotFound
                    && (hasPathParams || DeclaresStatus(description, (int)HttpStatusCode.NotFound)))
                || (response.StatusCode == HttpStatusCode.Forbidden
                    && DeclaresStatus(description, (int)HttpStatusCode.Forbidden))
                || (response.StatusCode == HttpStatusCode.ServiceUnavailable
                    && DeclaresStatus(description, (int)HttpStatusCode.ServiceUnavailable));

            if (!isSuccess)
            {
                failures.Add($"{path} => {(int)response.StatusCode} {response.StatusCode}");
            }
        }

        await Assert.That(failures).IsEmpty()
            .Because($"Public GET endpoints should return success or an explicitly declared safe read outcome. Failures: {string.Join("; ", failures)}");
    }

    [Test]
    public async Task Protected_Endpoints_ReturnUnauthorized_Or_Forbidden()
    {
        var endpoints = GetApiDescriptions()
            .Where(description => IsProtected(description))
            .Where(description => !IsProtectedSmokeException(description));

        var failures = new List<string>();

        foreach (var description in endpoints)
        {
            var path = BuildPath(description);
            using var request = new HttpRequestMessage(new HttpMethod(description.HttpMethod ?? HttpMethod.Get.Method), path)
            {
                Content = BuildBody(description)
            };
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("*/*"));

            var response = await _fixture.Client.SendAsync(request);
            var isUnauthorized = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                || (IsSetupSecretProtected(description) && response.StatusCode == HttpStatusCode.Gone)
                || (IsAdmissionScannerEndpoint(description) && response.StatusCode == HttpStatusCode.NotFound);

            if (!isUnauthorized)
            {
                failures.Add($"{description.HttpMethod} {path} => {(int)response.StatusCode} {response.StatusCode}");
            }
        }

        await Assert.That(failures).IsEmpty()
            .Because($"Protected endpoints should challenge or forbid anonymous callers. Failures: {string.Join("; ", failures)}");
    }

    [Test]
    public async Task Public_Write_Endpoints_DoNotReturnUnauthorized_Or_ServerError()
    {
        var endpoints = GetApiDescriptions()
            .Where(description => !IsProtected(description))
            .Where(description => !IsHttpMethod(description, HttpMethod.Get));

        var failures = new List<string>();

        foreach (var description in endpoints)
        {
            var path = BuildPath(description);
            using var request = new HttpRequestMessage(new HttpMethod(description.HttpMethod ?? HttpMethod.Post.Method), path)
            {
                Content = BuildBody(description)
            };

            var response = await _fixture.Client.SendAsync(request);
            var isUnauthorized = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            var isServerError = (int)response.StatusCode >= 500;

            if (isUnauthorized || isServerError)
            {
                failures.Add($"{description.HttpMethod} {path} => {(int)response.StatusCode} {response.StatusCode}");
            }
        }

        await Assert.That(failures).IsEmpty()
            .Because($"Public write endpoints should not challenge anonymous callers or return 5xx. Failures: {string.Join("; ", failures)}");
    }

    private IReadOnlyList<ApiDescription> GetApiDescriptions()
    {
        var provider = _fixture.Factory.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
        return provider.ApiDescriptionGroups.Items.SelectMany(group => group.Items).ToList();
    }

    private static bool IsProtected(ApiDescription description)
    {
        var metadata = description.ActionDescriptor.EndpointMetadata;
        var hasAllowAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var hasAuthorize = metadata.OfType<IAuthorizeData>().Any();
        var hasSetupSecretRequirement = metadata.OfType<SetupSecretRequiredAttribute>().Any();

        return (hasAuthorize && !hasAllowAnonymous) || hasSetupSecretRequirement;
    }

    private static bool IsSetupSecretProtected(ApiDescription description)
    {
        return description.ActionDescriptor.EndpointMetadata.OfType<SetupSecretRequiredAttribute>().Any();
    }

    private static bool IsAdmissionScannerEndpoint(ApiDescription description)
    {
        return description.RelativePath?.StartsWith(
            "api/admission/scanner/check-ins",
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsHttpMethod(ApiDescription description, HttpMethod method)
    {
        return string.Equals(description.HttpMethod, method.Method, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DeclaresStatus(ApiDescription description, int statusCode)
    {
        return description.SupportedResponseTypes.Any(response => response.StatusCode == statusCode);
    }

    private static bool IsProtectedSmokeException(ApiDescription description)
    {
        var path = description.RelativePath ?? string.Empty;

        // The setup package download is a binary-only endpoint. MVC content negotiation can
        // reject generic anonymous smoke probes before the setup-secret filter materializes
        // the expected challenge; dedicated onboarding tests cover this route.
        return path.Contains(
            "instanceonboarding/authz-provider-configuration/package",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicSmokeException(ApiDescription description)
    {
        var path = description.RelativePath ?? string.Empty;
        return path.Contains("tenant/navigation", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPath(ApiDescription description)
    {
        var relativePath = description.RelativePath ?? string.Empty;
        var path = "/" + relativePath.TrimStart('/');

        foreach (var parameter in description.ParameterDescriptions.Where(p => p.Source == BindingSource.Path))
        {
            var value = GetSampleValue(parameter, description);
            path = ReplaceRouteParameter(path, parameter.Name, value);
        }

        var queryParameters = description.ParameterDescriptions
            .Where(p => p.Source == BindingSource.Query)
            .Where(p => p.IsRequired || IsSafeOptionalScopeParameter(p))
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .ToList();

        if (queryParameters.Count > 0)
        {
            var query = new QueryBuilder();
            foreach (var parameter in queryParameters)
            {
                var value = GetSampleValue(parameter, description);
                query.Add(parameter.Name!, value);
            }

            path += query.ToQueryString().ToString();
        }

        return path;
    }

    private static string ReplaceRouteParameter(string path, string? name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return path;
        }

        var escapedName = Regex.Escape(name);
        var pattern = $@"\{{\*?{escapedName}(?:\:[^}}]+)?\}}";
        return Regex.Replace(path, pattern, value);
    }

    private static string GetSampleValue(ApiParameterDescription parameter, ApiDescription description)
    {
        var type = Nullable.GetUnderlyingType(parameter.Type ?? typeof(string)) ?? parameter.Type ?? typeof(string);

        if (description.RelativePath != null &&
            (description.RelativePath.Contains($"*{{{parameter.Name}}}") || description.RelativePath.Contains($"**{{{parameter.Name}}}")))
        {
            return "test/file.txt";
        }

        if (TryGetEnumerableElementType(type, out var elementType))
        {
            type = elementType;
        }

        if (type == typeof(Guid))
        {
            return Guid.NewGuid().ToString();
        }

        if (type == typeof(DateOnly))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short))
        {
            return "1";
        }

        if (type == typeof(bool))
        {
            return "true";
        }

        if (type.IsEnum)
        {
            return "1";
        }

        if (string.Equals(parameter.Name, "did", StringComparison.OrdinalIgnoreCase))
        {
            return "did:plc:test";
        }

        if (description.RelativePath?.StartsWith("api/translation/{languageCode}", StringComparison.OrdinalIgnoreCase) == true &&
            string.Equals(parameter.Name, "languageCode", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        return "test";
    }

    private static bool IsSafeOptionalScopeParameter(ApiParameterDescription parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter.Name))
        {
            return false;
        }

        return ScopedOptionalQueryParameters.Contains(parameter.Name);
    }

    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        var enumerableInterface = type
            .GetInterfaces()
            .Append(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (enumerableInterface is not null)
        {
            elementType = enumerableInterface.GetGenericArguments()[0];
            return true;
        }

        elementType = typeof(object);
        return false;
    }

    private static HttpContent? BuildBody(ApiDescription description)
    {
        var bodyParameter = description.ParameterDescriptions.FirstOrDefault(p => p.Source == BindingSource.Body);
        if (bodyParameter == null)
        {
            return null;
        }

        var type = bodyParameter.Type ?? typeof(object);
        var payload = GetSamplePayload(type);

        return new StringContent(payload, Encoding.UTF8, "application/json");
    }

    private static string GetSamplePayload(Type type)
    {
        if (type.Name == "RegistrationParticipantRequest")
        {
            return "{\"participantTypeId\":1,\"details\":{}}";
        }

        if (type.Name == "RegistrationTicketAssignmentsRequest")
        {
            return "{\"assignments\":[]}";
        }

        if (type.Name == "RegistrationTicketDeferralsRequest")
        {
            return $"{{\"assignments\":[],\"assignmentDeadline\":\"{DateTime.UtcNow.AddDays(1):O}\"}}";
        }

        if (type == typeof(string))
        {
            return "\"test\"";
        }

        if (type == typeof(Guid))
        {
            return $"\"{Guid.CreateVersion7()}\"";
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short))
        {
            return "1";
        }

        if (type == typeof(bool))
        {
            return "true";
        }

        return "{}";
    }
}
