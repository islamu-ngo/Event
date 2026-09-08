namespace Explore.Blazor.Client.Services.Docking;

public sealed record DockPanelDescriptor
{
    public DockPanelDescriptor(
        DockPanelId Id,
        DockScope Scope,
        DockSide Side,
        DockMode DefaultMode,
        string Title,
        string AriaLabel,
        int DefaultWidth,
        int MinWidth,
        int MaxWidth,
        int Order,
        bool IsResizable,
        bool CanClose,
        bool PersistState,
        DockPanelStackStrategy? StackStrategy = null,
        DockPanelMobilePresentation? MobilePresentation = null,
        int ResponsivePriority = 0,
        bool CanAutoCloseWhenConstrained = false)
    {
        this.Id = Id;
        this.Scope = Scope;
        this.Side = Side;
        this.DefaultMode = DefaultMode;
        this.Title = Title;
        this.AriaLabel = AriaLabel;
        this.DefaultWidth = DefaultWidth;
        this.MinWidth = MinWidth;
        this.MaxWidth = MaxWidth;
        this.Order = Order;
        this.IsResizable = IsResizable;
        this.CanClose = CanClose;
        this.PersistState = PersistState;
        this.StackStrategy = StackStrategy ?? DockPanelStackStrategy.Tabbed;
        this.MobilePresentation = MobilePresentation ?? DockPanelMobilePresentation.TemporaryOverlay;
        this.ResponsivePriority = ResponsivePriority;
        this.CanAutoCloseWhenConstrained = CanAutoCloseWhenConstrained;
    }

    public DockPanelId Id { get; init; }
    public DockScope Scope { get; init; }
    public DockSide Side { get; init; }
    public DockMode DefaultMode { get; init; }
    public string Title { get; init; }
    public string AriaLabel { get; init; }
    public int DefaultWidth { get; init; }
    public int MinWidth { get; init; }
    public int MaxWidth { get; init; }
    public int Order { get; init; }
    public bool IsResizable { get; init; }
    public bool CanClose { get; init; }
    public bool PersistState { get; init; }
    public DockPanelStackStrategy StackStrategy { get; init; }
    public DockPanelMobilePresentation MobilePresentation { get; init; }
    public int ResponsivePriority { get; init; }
    public bool CanAutoCloseWhenConstrained { get; init; }

    public DockPanelDescriptor Validate()
    {
        ArgumentNullException.ThrowIfNull(Id);
        ArgumentNullException.ThrowIfNull(StackStrategy);
        ArgumentNullException.ThrowIfNull(MobilePresentation);
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(AriaLabel);

        if (MinWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinWidth), "Minimum width must be greater than zero.");
        }

        if (MaxWidth < MinWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxWidth), "Maximum width must be greater than or equal to minimum width.");
        }

        if (DefaultWidth < MinWidth || DefaultWidth > MaxWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultWidth), "Default width must be between minimum and maximum width.");
        }

        if (ResponsivePriority < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ResponsivePriority), "Responsive priority must be zero or greater.");
        }

        return this;
    }
}
