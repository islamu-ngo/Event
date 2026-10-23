using System.ComponentModel.DataAnnotations;
using Explore.Domain.Authentication;

namespace Explore.Application.Features.Authentication.Local.Models;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class LocalPasswordAttribute(bool newCredential = true) : ValidationAttribute
{
    public override bool IsValid(object? value) => value is string password
        && (newCredential ? LocalPasswordPolicy.TryNormalizeNewCredential(password, out _)
            : LocalPasswordPolicy.TryNormalizeForVerification(password, out _));

    public override string FormatErrorMessage(string name) => "A bounded Local credential is required.";
}
