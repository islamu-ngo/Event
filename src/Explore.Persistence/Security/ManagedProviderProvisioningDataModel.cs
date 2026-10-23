using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ManagedProviderProvisioning;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

/// <summary>Source-owned vocabulary. Nothing in this inventory is supplied by an HTTP caller.</summary>
internal static partial class ManagedProviderProvisioningDataModel
{
    internal static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };
    internal sealed record Seeds(Guid Tenant, Guid Branding, Guid Directory, Guid User, Guid Actor, Guid Login,
        Guid Member, Guid Profile, Guid Grant, Guid Organizer, Guid OrganizerActor, Guid Participation,
        Guid Membership, Guid Revision, Guid CustomerBinding, Guid UserBinding, Guid MemberBinding,
        Guid ProfileBinding, Guid ActorBinding, Guid LoginBinding, Guid OrganizerBinding, Guid OrganizerActorBinding,
        Guid UserStamp, Guid ActorStamp, Guid OrganizerStamp, Guid ParticipationStamp, Guid BrandingStamp, Guid DirectoryStamp,
        Guid OrganizerActorStamp)
    {
        internal static Seeds Create() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
    }

    internal sealed record Request(Guid Human, HumanManagedProviderProvisioningInput Input, int Provider,
        string Account, string IdentitySystem, string Subject, Seeds Seeds, DateTime At,
        string BrandingKey, int BrandingSchema, string BrandingVersion, JsonElement BrandingPayload,
        string DirectoryKey, int DirectorySchema, string DirectoryVersion, JsonElement DirectoryPayload)
    {
        public override string ToString() => nameof(Request);
    }

    internal static string SerializeRequest(Request request)
    {
        var value = JsonSerializer.SerializeToNode(request, Json)!.AsObject();
        value[nameof(Request.At)] = request.At.ToString("O", CultureInfo.InvariantCulture);
        return value.ToJsonString();
    }

    internal static async Task<Request> CaptureAsync(EnsureManagedProviderClientProvisionedCommand command,
        Guid human, TimeProvider clock, CancellationToken token)
    {
        var input = await HumanManagedProviderProvisioningInputFactory.CaptureAsync(command, token);
        string subject = input.LocalIdentity?.LocalSubjectId.ToString("D") ?? input.ExternalAdmin!.Subject;
        string identity;
        ProviderAccountKey account;
        if (input.LocalIdentity is not null)
        {
            identity = "local";
            account = new(AuthenticationProviderKind.Local, subject);
        }
        else if (input.ExternalAdmin!.IdentityProvider.Equals("atproto", StringComparison.OrdinalIgnoreCase))
        {
            identity = "atproto";
            account = PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(Explore.Domain.ValueObjects.AtprotoDid.Parse(subject));
        }
        else
        {
            string authority = input.ExternalAdmin!.IdentityProvider;
            if (!Uri.TryCreate(authority, UriKind.Absolute, out var issuer) || issuer.Scheme is not ("http" or "https"))
                throw new ArgumentException("An absolute OIDC issuer is required.", nameof(command));
            identity = issuer.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase) ? "google" : "keycloak";
            account = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(authority, subject);
        }
        var seeds = Seeds.Create();
        DateTime now = clock.GetUtcNow().UtcDateTime;
        now = new(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var branding = TenantBrandingSettingsDocumentDefaults.Create(seeds.Tenant, input.TenantFullName);
        var directory = input.DirectoryOperatorIdentity is null
            ? TenantDirectoryOperatorIdentityDocumentDefaults.Create(seeds.Tenant)
            : TenantDirectoryOperatorIdentityDocumentDefaults.Create(seeds.Tenant, input.DirectoryOperatorIdentity);
        var prepared = TenantCreationService.Prepare(new(seeds.Tenant, input.TenantFullName, input.TenantSlug,
            input.ActivateTenant ? (int)TenantStatusEnum.Active : (int)TenantStatusEnum.Provisioning, human, now,
            new(seeds.Branding, branding.SchemaVersion, branding.DefaultsVersion, branding.PayloadJson),
            new(seeds.Directory, directory.SchemaVersion, directory.DefaultsVersion, directory.PayloadJson)));
        return new(human, input, (int)account.ProviderKind, account.Value, identity, subject, seeds, now,
            branding.DocumentKey, branding.SchemaVersion, branding.DefaultsVersion, JsonSerializer.Deserialize<JsonElement>(branding.PayloadJson),
            directory.DocumentKey, directory.SchemaVersion, directory.DefaultsVersion,
            JsonSerializer.Deserialize<JsonElement>(prepared.DirectoryOperatorIdentityDocument.PayloadJson));
    }

    internal static JsonNode? Value(IProperty property, object value)
    {
        if (value is DBNull) return null;
        Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (property.GetValueConverter() is { } converter) value = converter.ConvertFromProvider(value)!;
        if (type == typeof(Guid)) return JsonValue.Create(Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!).ToString("D"));
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            DateTime date = value is DateTime instant ? DateTime.SpecifyKind(instant, DateTimeKind.Utc)
                : value is DateTimeOffset offset ? offset.UtcDateTime
                : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return JsonValue.Create(date.ToString("O", CultureInfo.InvariantCulture));
        }
        if (type == typeof(bool)) return JsonValue.Create(Convert.ToBoolean(value, CultureInfo.InvariantCulture));
        if (type.IsEnum || type == typeof(int)) return JsonValue.Create(Convert.ToInt32(value, CultureInfo.InvariantCulture));
        if (type == typeof(long)) return JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        string text = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        return property.Name is "PayloadJson" or "PreferencesJson" or "ConsentJson" or "MetadataJson"
            ? JsonNode.Parse(text) : JsonValue.Create(text);
    }

}
