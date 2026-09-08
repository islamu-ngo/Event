using System;

namespace Explore.Application.DTOs.Tag;

public sealed record UpdateTagDto
{
    public UpdateTagMasterCodeDto? MasterCode { get; init; }
    public UpdateTagFullNameDto? FullName { get; init; }
}

public sealed record UpdateTagMasterCodeDto
{
    public required string Value { get; init; }
}

public sealed record UpdateTagFullNameDto
{
    public required string Value { get; init; }
}
