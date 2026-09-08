using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

public sealed record GrantAiConsentCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required Guid TenantId { get; init; }
    public required Guid SubjectUserId { get; init; }
    public required string EntityName { get; init; }
    public required string FieldName { get; init; }
    public int ProviderTrustTierId { get; init; } = (int)AiProviderTrustTierEnum.Unknown;
    public string? Purpose { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required Guid GrantedByUserId { get; init; }
}
