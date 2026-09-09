namespace Lucent.Core;

public sealed partial class InputRouter
{
    private PopupSurfaceRequest? _surface;
    private readonly Queue<PopupSurfaceRequest> _waitingModalSurfaces = [];

    internal LayoutRect? Bounds(ElementIdentity identity) =>
        _scene is not null && ValidateScene(_scene) && Eligible(identity)
            ? SurfaceAnchor(identity)
            : null;

    /// <summary>The current owner-scoped surface request, if any.</summary>
    public PopupSurfaceRequest? ActiveSurface
    {
        get
        {
            Check();
            return _surface is { IsDismissed: false } current ? current : null;
        }
    }

    /// <summary>Raised when a surface becomes active; hosts defer construction until input and reactive work settle.</summary>
    public event Action<PopupSurfaceRequest>? SurfaceRequested;

    internal LayoutRect? SurfaceAnchor(ElementIdentity identity) =>
        identity.CompositionEpoch == _composition.Epoch
        && _input.TryGetValue(identity.ElementId, out var input)
            ? input.Bounds
            : null;

    internal void RequestSurface(PopupSurfaceRequest request)
    {
        Check();
        if (!ReferenceEquals(request.Owner, _composition))
            throw new ArgumentException(
                "A surface belongs to its opening composition.",
                nameof(request)
            );
        if (ReferenceEquals(_surface, request))
            return;
        if (_surface is { IsModal: true })
        {
            if (request.IsModal)
                _waitingModalSurfaces.Enqueue(request);
            else
            {
                request.Dismiss();
                request.Dispose();
            }
            return;
        }
        _surface?.Dismiss();
        _surface = request;
        try
        {
            SurfaceRequested?.Invoke(request);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    /// <summary>Releases a completed host session and promotes the next still-valid modal request for this owner.</summary>
    public void CompleteSurface(PopupSurfaceRequest request)
    {
        Check();
        if (!ReferenceEquals(_surface, request))
            return;
        _surface = null;
        while (_waitingModalSurfaces.TryDequeue(out var next))
        {
            if (next.IsDismissed)
            {
                next.Dispose();
                continue;
            }
            RequestSurface(next);
            break;
        }
    }

    private void CleanupSurfaces(List<Exception> errors)
    {
        var surfaces = _waitingModalSurfaces.ToList();
        _waitingModalSurfaces.Clear();
        if (_surface is not null)
            surfaces.Add(_surface);
        _surface = null;
        foreach (var surface in surfaces)
            try
            {
                surface.Dispose();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
    }
}
