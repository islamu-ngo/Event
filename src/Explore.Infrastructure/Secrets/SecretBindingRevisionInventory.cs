using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Explore.Domain.Secrets;
using Explore.ServiceDefaults.Secrets;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Secrets;

/// <summary>
/// Retains equality evidence, never material or a serialized SDK revision. Tokens
/// are meaningful only while this process owner retains the corresponding entry.
/// </summary>
internal sealed class SecretBindingRevisionInventory
{
    internal const int Capacity = 4096;
    private static readonly ConditionalWeakTable<SecretAuthorityAcquisition, SecretBindingRevisionInventory> Owners = new();
    private readonly object _gate = new();
    private readonly Dictionary<BindingSlot, Entry> _entries = new();
    private readonly LinkedList<BindingSlot> _recency = new();
    private readonly Guid _generationId;
    private readonly SecretAuthoritySelection _selection;
    private readonly bool _requiresRevision;
    private long _observationSequence;
    private long _compactedThrough;

    private SecretBindingRevisionInventory(SecretAuthorityAcquisition owner)
    {
        _generationId = owner.GenerationId;
        _selection = owner.Selection;
        _requiresRevision = owner.Provider == SecretAuthorityKind.Infisical;
    }

    internal static string? Observe(
        SecretAuthorityAcquisition owner, RuntimeSecretBinding binding, InfisicalSecretRevision? revision = null)
    {
        if (owner.Provider == SecretAuthorityKind.Infisical && revision is null)
            return null;
        return BeginObservation(owner, binding).Complete(revision);
    }

    internal static Observation BeginObservation(
        SecretAuthorityAcquisition owner, RuntimeSecretBinding binding)
    {
        var inventory = Owners.GetValue(owner, static authority => new(authority));
        lock (inventory._gate)
            return new(inventory, binding, ++inventory._observationSequence);
    }

    internal static void Forget(SecretAuthorityAcquisition owner) => Owners.Remove(owner);

    internal static bool IsCurrent(
        SecretAuthorityAcquisition owner, RuntimeSecretBinding binding, string? revision)
    {
        if (string.IsNullOrEmpty(revision))
            return false;
        try
        {
            _ = owner.NativeClient;
        }
        catch (ObjectDisposedException)
        {
            Forget(owner);
            return false;
        }
        if (!Owners.TryGetValue(owner, out var inventory))
            return false;
        lock (inventory._gate)
        {
            return inventory._generationId == owner.GenerationId && inventory._selection == owner.Selection
                && inventory._entries.TryGetValue(Slot(binding), out Entry? entry)
                && entry.Binding == binding
                && string.Equals(entry.Token, revision, StringComparison.Ordinal);
        }
    }

    private static BindingSlot Slot(RuntimeSecretBinding binding) => binding.Id != Guid.Empty
        ? new BindingSlot(binding.Id, null, 0, null, null)
        : new BindingSlot(Guid.Empty, binding.SettingKey, binding.SettingScopeId,
            binding.ScopeId, binding.Qualifier);

    private string? Observe(
        RuntimeSecretBinding binding, InfisicalSecretRevision? revision,
        long observationSequence)
    {
        if (_requiresRevision && revision is null)
            return null;
        // Persisted identity stays stable when its metadata is retargeted. Synthetic
        // registry defaults have no row ID, so their logical scope identifies the slot.
        BindingSlot slot = Slot(binding);
        lock (_gate)
        {
            _entries.TryGetValue(slot, out Entry? previous);
            if (previous is not null
                && previous.Binding == binding
                && (previous.Revision is null && revision is null
                    || previous.Revision is { } retained && revision is { } candidate && retained.Equals(candidate)))
            {
                previous.ObservationSequence = Math.Max(previous.ObservationSequence, observationSequence);
                _recency.Remove(previous.Node);
                _recency.AddLast(previous.Node);
                return previous.Token;
            }
            if (observationSequence <= _compactedThrough
                || previous is not null && previous.ObservationSequence > observationSequence)
                return null;
            if (previous is not null)
            {
                _recency.Remove(previous.Node);
                _entries.Remove(slot);
            }
            if (_entries.Count == Capacity)
            {
                BindingSlot evicted = _recency.First!.Value;
                _recency.RemoveFirst();
                _entries.Remove(evicted);
                // Eviction loses per-slot ordering evidence. Reject pending
                // observations predating compaction instead of resurrecting it.
                _compactedThrough = _observationSequence;
            }
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var node = _recency.AddLast(slot);
            _entries.Add(slot, new(binding, revision, token, node, observationSequence));
            return token;
        }
    }

    internal sealed class Observation(
        SecretBindingRevisionInventory inventory, RuntimeSecretBinding binding, long sequence)
    {
        internal string? Complete(InfisicalSecretRevision? revision) =>
            inventory.Observe(binding, revision, sequence);
    }

    private sealed record BindingSlot(Guid Id, string? SettingKey, int Scope, Guid? ScopeId, string? Qualifier);
    private sealed class Entry(
        RuntimeSecretBinding binding, InfisicalSecretRevision? revision,
        string token, LinkedListNode<BindingSlot> node, long observationSequence)
    {
        internal RuntimeSecretBinding Binding { get; } = binding;
        internal InfisicalSecretRevision? Revision { get; } = revision;
        internal string Token { get; } = token;
        internal LinkedListNode<BindingSlot> Node { get; } = node;
        internal long ObservationSequence { get; set; } = observationSequence;
    }
}

/// <summary>Snapshots scalar binding metadata before any asynchronous provider work.</summary>
internal sealed record RuntimeSecretBinding(
    Guid Id, string SettingKey, string Qualifier, int SettingScopeId, Guid? ScopeId,
    int SecretSourceTypeId, string? InfisicalEnvironment, string? InfisicalPath, string? InfisicalKey,
    string? EnvironmentVariableName, bool IsLocked, int SecretValidationStatusId,
    string? LastValidationError, DateTime? LastValidatedAt, Guid? LastValidatedBy,
    DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy)
{
    internal static RuntimeSecretBinding Capture(SecretBinding binding) => new(
        binding.Id, binding.SettingKey, binding.Qualifier, binding.SettingScopeId, binding.ScopeId,
        binding.SecretSourceTypeId, binding.InfisicalEnvironment, binding.InfisicalPath,
        binding.InfisicalKey, binding.EnvironmentVariableName, binding.IsLocked,
        binding.SecretValidationStatusId, binding.LastValidationError, binding.LastValidatedAt,
        binding.LastValidatedBy, binding.CreatedAt, binding.CreatedBy, binding.UpdatedAt, binding.UpdatedBy);

    internal SecretBinding ToBinding() => new()
    {
        Id = Id,
        SettingKey = SettingKey,
        Qualifier = Qualifier,
        SettingScopeId = SettingScopeId,
        ScopeId = ScopeId,
        SecretSourceTypeId = SecretSourceTypeId,
        InfisicalEnvironment = InfisicalEnvironment,
        InfisicalPath = InfisicalPath,
        InfisicalKey = InfisicalKey,
        EnvironmentVariableName = EnvironmentVariableName,
        IsLocked = IsLocked,
        SecretValidationStatusId = SecretValidationStatusId,
        LastValidationError = LastValidationError,
        LastValidatedAt = LastValidatedAt,
        LastValidatedBy = LastValidatedBy,
        CreatedAt = CreatedAt,
        CreatedBy = CreatedBy,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy
    };

    public override string ToString() => "runtime_secret_binding";
}
