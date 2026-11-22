using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Learning;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Services.Learning;

public sealed record LearningTarget(string Key, string ElementId, ElementReference Element);

public sealed record LearningPresentationContext(
    LearningTourFamily Family, string Generation, Guid? EventId = null);

/// <summary>Contains only targets published after their real owners have rendered.</summary>
public sealed class LearningTargetRegistry : ILearningTargetRegistry
{
    private readonly Dictionary<object, Registration> _owners = new();
    public event Action? Changed;

    public LearningPresentationContext? Current => _owners.Values
        .OrderByDescending(item => (int)item.Context.Family)
        .Select(item => item.Context).FirstOrDefault();

    public bool IsBlocked => _owners.Values.Any(item => item.Blocked);

    public void Publish(object owner, LearningPresentationContext context,
        bool blocked, params LearningTarget[] targets)
    {
        if (targets.Any(target => string.IsNullOrWhiteSpace(target.Element.Id)
            || string.IsNullOrWhiteSpace(target.Key) || string.IsNullOrWhiteSpace(target.ElementId)
            || target.ElementId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
            throw new ArgumentException("A target must identify a rendered, application-owned element.", nameof(targets));
        if (_owners.TryGetValue(owner, out var old) && old.Context == context
            && old.Blocked == blocked && old.Targets.SequenceEqual(targets))
            return;
        _owners[owner] = new(context, blocked, targets.ToArray());
        Changed?.Invoke();
    }

    public void Remove(object owner)
    {
        if (_owners.Remove(owner))
            Changed?.Invoke();
    }

    public LearningTarget? Find(LearningPresentationContext context, string key)
    {
        var matches = _owners.Values.Where(item => item.Context == context)
            .SelectMany(item => item.Targets).Where(target => target.Key == key).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public async Task<LearningTarget> WaitAsync(LearningPresentationContext context,
        string key, CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource<LearningTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe()
        {
            if (Current != context || IsBlocked)
                ready.TrySetCanceled(cancellationToken);
            else if (Find(context, key) is { } target)
                ready.TrySetResult(target);
        }
        Changed += Observe;
        try
        {
            Observe();
            return await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            Changed -= Observe;
        }
    }

    private sealed record Registration(
        LearningPresentationContext Context, bool Blocked, LearningTarget[] Targets);
}
