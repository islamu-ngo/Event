namespace Explore.Blazor.Client.Services;

public sealed class MainContentAppearanceState
{
    private string _owner = string.Empty;
    private string _style = string.Empty;

    public event Action? Changed;

    public string Style => _style;
    public bool HasAppearance => !string.IsNullOrWhiteSpace(_style);

    public void Set(string owner, string style)
    {
        var normalizedOwner = owner.Trim();
        var normalizedStyle = style.Trim();

        if (_owner == normalizedOwner && _style == normalizedStyle)
        {
            return;
        }

        _owner = normalizedOwner;
        _style = normalizedStyle;
        Changed?.Invoke();
    }

    public void Clear(string owner)
    {
        if (!string.Equals(_owner, owner.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_style) && string.IsNullOrWhiteSpace(_owner))
        {
            return;
        }

        _owner = string.Empty;
        _style = string.Empty;
        Changed?.Invoke();
    }
}
