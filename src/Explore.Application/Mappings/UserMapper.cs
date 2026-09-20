using Explore.Application.DTOs.User;
using Explore.Application.DTOs.UserAuthenticationToken;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class UserMapper
{
    // The current-user response discloses identity scalars, not PII/navigation graphs,
    // tenant history or audit/deletion state. Authorization and erasure fencing stay in callers.
    [MapperIgnoreSource(nameof(User.Pii))]
    [MapperIgnoreSource(nameof(User.LastActiveTenantId))]
    [MapperIgnoreSource(nameof(User.CreatedAt))]
    [MapperIgnoreSource(nameof(User.CreatedBy))]
    [MapperIgnoreSource(nameof(User.UpdatedAt))]
    [MapperIgnoreSource(nameof(User.UpdatedBy))]
    [MapperIgnoreSource(nameof(User.IsDeleted))]
    [MapperIgnoreSource(nameof(User.DeletedAt))]
    [MapperIgnoreSource(nameof(User.DeletedBy))]
    // Provider bindings are resolved by the identity operation, never inferred by this projection.
    [MapperIgnoreTarget(nameof(UserDto.AuthProvider))]
    [MapperIgnoreTarget(nameof(UserDto.AuthProviderId))]
    // Image presentation is handler-owned; these fields were not populated by the old map.
    [MapperIgnoreTarget(nameof(UserDto.ProfileImageKey))]
    [MapperIgnoreTarget(nameof(UserDto.ProfileImageUri))]
    [MapperIgnoreTarget(nameof(UserDto.ActorBannerPictureId))]
    [MapperIgnoreTarget(nameof(UserDto.ActorBannerPictureUri))]
    [MapperIgnoreTarget(nameof(UserDto.ActorBackgroundImageId))]
    [MapperIgnoreTarget(nameof(UserDto.ActorBackgroundImageUri))]
    [MapProperty(nameof(User.Id), nameof(UserDto.Id))]
    [MapProperty(nameof(User.Email), nameof(UserDto.Email))]
    [MapProperty(nameof(User.FirstName), nameof(UserDto.FirstName))]
    [MapProperty(nameof(User.LastName), nameof(UserDto.LastName))]
    [MapProperty(nameof(User.EmailVerified), nameof(UserDto.EmailVerified))]
    [MapProperty(nameof(User.ConcurrencyStamp), nameof(UserDto.ConcurrencyStamp))]
    [MapProperty(nameof(User.Actor), nameof(UserDto.ActorId), Use = nameof(ActorId))]
    [MapProperty(nameof(User.Actor), nameof(UserDto.ActorDisplayName), Use = nameof(ActorDisplayName))]
    [MapProperty(nameof(User.Actor), nameof(UserDto.ActorHandle), Use = nameof(ActorHandle))]
    [MapProperty(nameof(User.Actor), nameof(UserDto.ActorBackgroundColor), Use = nameof(ActorBackgroundColor))]
    [MapProperty(nameof(User.Actor), nameof(UserDto.ActorBackgroundEffect), Use = nameof(ActorBackgroundEffect))]
    [MapProperty(nameof(User.Actor), nameof(UserDto.ActorBannerColor), Use = nameof(ActorBannerColor))]
    public static partial UserDto ToDetail(User source);

    // Token responses expose connection metadata only, never credentials, subjects or ownership.
    [MapperIgnoreSource(nameof(UserAuthenticationToken.UserId))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.User))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.TenantId))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.Tenant))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.SubjectDid))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.SessionCiphertext))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.EncryptionKeyId))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.OAuthClientKeyId))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.EnvelopeVersion))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.ConcurrencyStamp))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.CreatedAt))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.CreatedBy))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.UpdatedAt))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.UpdatedBy))]
    [MapProperty(nameof(UserAuthenticationToken.Id), nameof(UserAuthenticationTokenDto.Id))]
    [MapProperty(nameof(UserAuthenticationToken.Provider), nameof(UserAuthenticationTokenDto.Provider))]
    [MapProperty(nameof(UserAuthenticationToken.PdsHost), nameof(UserAuthenticationTokenDto.PdsHost))]
    [MapProperty(nameof(UserAuthenticationToken.ExpiresAt), nameof(UserAuthenticationTokenDto.ExpiresAt))]
    public static partial UserAuthenticationTokenDto ToTokenDetail(UserAuthenticationToken source);

    [MapperIgnoreSource(nameof(UserAuthenticationToken.UserId))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.User))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.TenantId))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.Tenant))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.SubjectDid))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.SessionCiphertext))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.EncryptionKeyId))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.OAuthClientKeyId))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.EnvelopeVersion))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.ConcurrencyStamp))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.CreatedAt))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.CreatedBy))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.UpdatedAt))]
    [MapperIgnoreSource(nameof(UserAuthenticationToken.UpdatedBy))]
    [MapProperty(nameof(UserAuthenticationToken.Id), nameof(UserAuthenticationTokenListDto.Id))]
    [MapProperty(nameof(UserAuthenticationToken.Provider), nameof(UserAuthenticationTokenListDto.Provider))]
    [MapProperty(nameof(UserAuthenticationToken.PdsHost), nameof(UserAuthenticationTokenListDto.PdsHost))]
    [MapProperty(nameof(UserAuthenticationToken.ExpiresAt), nameof(UserAuthenticationTokenListDto.ExpiresAt))]
    public static partial UserAuthenticationTokenListDto ToTokenListItem(UserAuthenticationToken source);

    public static List<UserAuthenticationTokenListDto> ToTokenList(IEnumerable<UserAuthenticationToken> source) =>
        source.Select(ToTokenListItem).ToList();

    // Optional navigations stay bounded to scalar selections, even when the entity graph is cyclic.
    private static Guid ActorId(Actor? actor) => actor?.Id ?? Guid.Empty;
    private static string? ActorDisplayName(Actor? actor) => actor?.Pii?.DisplayName;
    private static string? ActorHandle(Actor? actor) => actor?.AtprotoIdentities.Select(identity => identity.Handle).FirstOrDefault();
    private static string? ActorBackgroundColor(Actor? actor) => actor?.BackgroundColor;
    private static string? ActorBackgroundEffect(Actor? actor) => actor?.BackgroundEffect;
    private static string? ActorBannerColor(Actor? actor) => actor?.BannerColor;
}
