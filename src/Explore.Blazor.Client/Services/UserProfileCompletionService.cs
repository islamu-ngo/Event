using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Serialization;

namespace Explore.Blazor.Client.Services;

public interface IUserProfileCompletionService
{
    Task<HalResourceOfPrivateProfileCompletionDto> ReadAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateNamesAsync(Guid userId, Guid stamp, UpdateUserNamesDto names,
        ModerationProfileCollectionOption option, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateImageAsync(Guid userId, Guid stamp, UpdateUserProfileImageDto image,
        CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> RecordBirthDateAsync(Guid userId, DateOnly date, long revision,
        ModerationProfileCollectionOption option, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> ClearBirthDateAsync(Guid userId, long revision, CancellationToken cancellationToken = default);
}

/// <summary>Uses separate generated transports for nominated profile fields and ordinary owned images.</summary>
public sealed class UserProfileCompletionService(IUserClient users, IUserProfileCompletionClient completion,
    IUserProfileImageClient images) : IUserProfileCompletionService
{
    public Task<HalResourceOfPrivateProfileCompletionDto> ReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        completion.GetUserProfileCompletionAsync(userId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateNamesAsync(Guid userId, Guid stamp, UpdateUserNamesDto names,
        ModerationProfileCollectionOption option, CancellationToken cancellationToken = default) =>
        users.UpdateCurrentUserAsync(userId, new UpdateUserDto { Names = names }, $"\"{stamp:D}\"",
            Nomination(option), cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateImageAsync(Guid userId, Guid stamp, UpdateUserProfileImageDto image,
        CancellationToken cancellationToken = default) =>
        images.UpdateCurrentUserProfileImageAsync(userId, image, $"\"{stamp:D}\"", cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> RecordBirthDateAsync(Guid userId, DateOnly date, long revision,
        ModerationProfileCollectionOption option, CancellationToken cancellationToken = default) =>
        completion.RecordPrivateBirthDateAsync(userId,
            new RecordPrivateBirthDateDto
            {
                BirthDate = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                ExpectedRevision = revision
            }, Nomination(option),
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> ClearBirthDateAsync(Guid userId, long revision, CancellationToken cancellationToken = default) =>
        completion.ClearPrivateBirthDateAsync(userId, revision, cancellationToken: cancellationToken);

    private static string Nomination(ModerationProfileCollectionOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(option.Assessment);
        return JsonSerializer.Serialize(new UserProfileCollectionNomination(option.Assessment, option.ConsentId),
            AppJsonSerializerContext.Default.UserProfileCollectionNomination);
    }
}

public sealed record UserProfileCollectionNomination(ModerationProfileSubjectConsentAssessment Assessment, Guid? ConsentId)
{
    public override string ToString() => nameof(UserProfileCollectionNomination);
}
