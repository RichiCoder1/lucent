namespace Lucent.Core;

/// <summary>Host contract for one owned popup's content, geometry and lifetime.</summary>
/// <remarks>Menu navigation and arbitrary surface content retain separate request implementations. A platform host owns its native window; Core owns no native handle.</remarks>
public abstract class PopupSurfaceRequest : IDisposable
{
    /// <summary>The composition that owns this surface request.</summary>
    public abstract Composition Owner { get; }

    /// <summary>The current preferred anchor in owner-client logical coordinates.</summary>
    public abstract LayoutRect Anchor { get; }

    /// <summary>The inherited portable appearance.</summary>
    public abstract ThemeAppearance Appearance { get; }

    /// <summary>Whether the owner and anchor remain eligible.</summary>
    public abstract bool IsValid { get; }

    /// <summary>Whether the host should close this surface.</summary>
    public abstract bool IsDismissed { get; }

    /// <summary>Whether fresh projected placement geometry is available.</summary>
    public virtual bool HasAnchor => true;

    /// <summary>Whether this surface blocks interaction in its owner while shown.</summary>
    public virtual bool IsModal => false;

    /// <summary>The accessible native window title.</summary>
    public virtual string Title =>
        IsModal ? "Dialog"
        : IsInteractive ? "Popover"
        : "Tooltip";

    /// <summary>Whether this surface accepts focus and interactive input.</summary>
    public virtual bool IsInteractive => true;

    /// <summary>Whether an outside dismissal gesture is consumed rather than delivered to the owner.</summary>
    public virtual bool ConsumeOutsideClick => true;

    /// <summary>The logical gap below the anchor.</summary>
    public virtual float AnchorGap => 0;

    /// <summary>Applies an explicit safe initial focus after the host installs the first scene. False requests ordinary traversal fallback.</summary>
    public virtual bool FocusInitial() => false;

    /// <summary>Whether ordinary traversal may select the initial target if explicit focus was not applied.</summary>
    public virtual bool AllowInitialFocusFallback => true;

    /// <summary>Mounts or returns the retained content composition.</summary>
    public abstract Composition CreateComposition();

    /// <summary>Measures content within the host's available logical work area.</summary>
    public abstract LayoutRect Measure(ITextShaper shaper, LayoutViewport available);

    /// <summary>Captures the current mutation revision shared by this surface and its owner.</summary>
    /// <remarks>Hosts can compare snapshots around popup input to decide whether the owner's retained scene needs projection.</remarks>
    public long CaptureSharedMutationRevision()
    {
        Owner.CheckThread();
        return Owner.Graph.MutationRevision;
    }

    /// <summary>Sets platform-owned safe-area padding on a popup composition on this request's graph.</summary>
    /// <remarks>The owner composition itself is rejected; menu hosts may pass an active child-level composition.</remarks>
    public void ConfigureHostPadding(Composition popup, Insets padding)
    {
        ArgumentNullException.ThrowIfNull(popup);
        Owner.CheckThread();
        if (ReferenceEquals(popup, Owner) || !ReferenceEquals(popup.Graph, Owner.Graph))
            throw new ArgumentException(
                "A popup host can configure only a non-owner composition on the request owner's graph.",
                nameof(popup)
            );
        if (popup.Root.HasPresentation)
            popup.Root.UpdateControl(LayoutProperties.Padding, padding);
        else
            popup.Root.Present(
                new ThemeContext(popup.Root.Scope, new Theme("popup-host")),
                Style.Empty.Padding(padding)
            );
    }

    /// <summary>Requests idempotent host dismissal.</summary>
    public abstract void Dismiss();

    /// <summary>Restores valid prior focus without overriding an explicit later focus choice.</summary>
    public abstract bool RestoreFocus();

    /// <summary>Releases retained content; accepted application work keeps its own lifetime.</summary>
    public abstract void Dispose();
}
