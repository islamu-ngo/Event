namespace Explore.API.Attributes;

/// <summary>Requires current native collection admission before MVC may consume profile fields.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ProfileCollectionAdmissionAttribute : Attribute
{
    public const string HeaderName = "X-Profile-Collection";
}
