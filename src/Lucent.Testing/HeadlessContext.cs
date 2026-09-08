using Lucent.Core;
using Microsoft.Extensions.Time.Testing;

namespace Lucent.Testing;

/// <summary>Owner-thread-only access to the production state hosted by a headless application.</summary>
/// <remarks>Instances are valid only for the duration of an <c>InvokeAsync</c> callback or recipe factory.</remarks>
public sealed class HeadlessContext
{
    private readonly int _ownerThread;
    private RetainedScene? _scene;
    private readonly ApplicationSession _session;
    private readonly FakeTimeProvider _timeProvider;
    private LayoutViewport _viewport;

    internal HeadlessContext(
        ApplicationSession session,
        FakeTimeProvider timeProvider,
        LayoutViewport viewport
    )
    {
        _ownerThread = Environment.CurrentManagedThreadId;
        _session = session;
        _timeProvider = timeProvider;
        _viewport = viewport;
    }

    /// <summary>Gets the production application session.</summary>
    public ApplicationSession Session
    {
        get
        {
            CheckOwner();
            return _session;
        }
    }

    /// <summary>Gets the retained production composition.</summary>
    public Composition Composition
    {
        get
        {
            CheckOwner();
            return Session.Composition;
        }
    }

    /// <summary>Gets the real production input router.</summary>
    public InputRouter Input
    {
        get
        {
            CheckOwner();
            return Composition.Input;
        }
    }

    /// <summary>Gets the deterministic clock controlled by the harness.</summary>
    public FakeTimeProvider TimeProvider
    {
        get
        {
            CheckOwner();
            return _timeProvider;
        }
    }

    /// <summary>Gets the current viewport.</summary>
    public LayoutViewport Viewport
    {
        get
        {
            CheckOwner();
            return _viewport;
        }
        internal set
        {
            CheckOwner();
            _viewport = value;
        }
    }

    /// <summary>Gets the most recently projected scene.</summary>
    public RetainedScene Scene
    {
        get
        {
            CheckOwner();
            return _scene
                ?? throw new InvalidOperationException(
                    "No scene is available while the root recipe is being created."
                );
        }
    }

    /// <summary>Finds a retained element from an immutable semantic snapshot.</summary>
    public Element RequireElement(SemanticSnapshot semantic)
    {
        CheckOwner();
        ArgumentNullException.ThrowIfNull(semantic);
        if (
            !Scene.Boxes.Any(box =>
                box.Identity.CompositionEpoch == semantic.Identity.CompositionEpoch
                && box.Identity.ElementId == semantic.Identity.ElementId
            )
        )
            throw new InvalidOperationException(
                "The semantic node belongs to another application or is no longer in the scene."
            );
        var match = Descendants(Composition.Root)
            .SingleOrDefault(element => element.Id == semantic.Identity.ElementId);
        return match
            ?? throw new InvalidOperationException(
                "The semantic node no longer identifies a retained element."
            );
    }

    internal void SetScene(RetainedScene scene)
    {
        CheckOwner();
        _scene = scene;
    }

    internal void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "Headless context access belongs to its application owner thread. Use InvokeAsync."
            );
    }

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }
}
