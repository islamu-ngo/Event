using System.Text.Json;
using System.Text.Json.Serialization;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Provides a single <see cref="JsonSerializerOptions"/> instance matching the API's wire contract.
/// The API registers <see cref="JsonStringEnumConverter"/> globally (see <c>Explore.API/Program.cs</c>),
/// so tests that <c>ReadFromJsonAsync&lt;T&gt;</c> DTOs containing enums must use the same converter,
/// otherwise default System.Text.Json cannot convert string values to enum types.
/// </summary>
internal static class TestJsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
