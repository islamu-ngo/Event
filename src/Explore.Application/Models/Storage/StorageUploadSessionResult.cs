using System.Text.Json.Serialization;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;
using Explore.Application.Services;

namespace Explore.Application.Models.Storage;

/// <summary>
/// Ordinary uploads retain their native command response. Resource-owned session fields
/// remain private until their single-use read projection establishes disclosure authority.
/// </summary>
public sealed record StorageUploadSessionResult : IAsyncDisposable
{
    [JsonIgnore]
    public BaseCommandResponse<StorageUploadSessionDto>? Response { get; }
    [JsonIgnore]
    public EventResourceReadProjection<BaseCommandResponse<StorageUploadSessionDto>>? Projection { get; }
    public bool IsSuccess => Response?.IsSuccess ?? Projection is not null;

    private StorageUploadSessionResult(BaseCommandResponse<StorageUploadSessionDto>? response,
        EventResourceReadProjection<BaseCommandResponse<StorageUploadSessionDto>>? projection)
        => (Response, Projection) = (response, projection);

    public static StorageUploadSessionResult Ordinary(BaseCommandResponse<StorageUploadSessionDto> response) =>
        new(response ?? throw new ArgumentNullException(nameof(response)), null);

    internal static StorageUploadSessionResult Resource(
        EventResourceReadProjection<BaseCommandResponse<StorageUploadSessionDto>> projection) =>
        new(null, projection);

    public ValueTask DisposeAsync() => Projection?.DisposeAsync() ?? ValueTask.CompletedTask;
    public override string ToString() => nameof(StorageUploadSessionResult);
}
