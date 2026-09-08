namespace Explore.Application.DTOs.Instance;

public sealed record ModuleSettingsDto
{
    public bool EnableIslamicModule { get; set; } = true;
    public bool EnableTechModule { get; set; } = true;
}
