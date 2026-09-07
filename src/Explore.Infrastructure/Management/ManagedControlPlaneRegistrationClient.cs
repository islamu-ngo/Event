using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Management;

namespace Explore.Infrastructure.Management;

internal sealed class ManagedControlPlaneRegistrationClient(HttpClient httpClient)
    : IManagedControlPlaneRegistrationClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public async Task<CompleteManagedInstanceRegistrationResponseDto> CompleteRegistrationAsync(
        Uri controlPlaneUrl,
        CompleteManagedInstanceRegistrationRequestDto request,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(
            controlPlaneUrl,
            $"/api/managed-event-instances/{request.ManagedInstanceId:D}/registration");
        using var response = await httpClient.PostAsJsonAsync(
            endpoint,
            request,
            SerializerOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<CompleteManagedInstanceRegistrationResponseDto>(
                   SerializerOptions,
                   cancellationToken)
               ?? throw new InvalidOperationException("The Control Plane returned an empty registration response.");
    }
}
