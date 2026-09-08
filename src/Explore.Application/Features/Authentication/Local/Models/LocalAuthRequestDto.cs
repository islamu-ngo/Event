namespace Explore.Application.Features.Authentication.Local.Models;

public sealed record LocalAuthRequestDto(string Identifier, string Password)
{
    public override string ToString() => nameof(LocalAuthRequestDto);
}
