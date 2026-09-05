using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>Translates owner work into one coalesced SDL event; only the owner drains Core.</summary>
internal sealed class WindowsWorkDispatcher : IDisposable
{
    internal delegate bool PushEvent(ref SDL.Event @event);
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Composition _composition;
    private readonly ApplicationSession? _session;
    private readonly uint _eventType;
    private readonly PushEvent _push;
    private readonly Action<string> _fatal;
    private readonly object _gate = new();
    private int _scheduled;
    private bool _disposed;

    internal WindowsWorkDispatcher(
        Composition composition,
        PushEvent? push = null,
        Action<string>? fatal = null
    )
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _push = push ?? SDL.PushEvent;
        _fatal = fatal ?? Environment.FailFast;
        _eventType = SDL.RegisterEvents(1);
        ValidateEventType(_eventType);
        composition.WorkAvailable += Wake;
    }

    internal WindowsWorkDispatcher(
        ApplicationSession session,
        PushEvent? push = null,
        Action<string>? fatal = null
    )
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _composition = session.Composition;
        _push = push ?? SDL.PushEvent;
        _fatal = fatal ?? Environment.FailFast;
        _eventType = SDL.RegisterEvents(1);
        ValidateEventType(_eventType);
        session.WorkAvailable += Wake;
    }

    internal uint EventType => _eventType;

    internal static void ValidateEventType(uint eventType)
    {
        if (eventType == 0)
            throw new InvalidOperationException(
                "SDL_RegisterEvents(owner work) failed: " + SDL.GetError()
            );
    }

    internal bool IsWakeEvent(SDL.Event @event) => @event.Type == _eventType;

    /// <summary>Resets before draining so a post after the empty check gets a new wake.</summary>
    internal bool Process()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Owner work must drain on the SDL owner thread.");
        lock (_gate)
        {
            if (_disposed)
                return false;
            Interlocked.Exchange(ref _scheduled, 0);
        }

        var sessionWork = _session?.ProcessEvents() == true;
        if (_session?.IsCompleted == true || _composition.IsDisposed)
            return false;
        return sessionWork | _composition.Flush();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_session is null)
                _composition.WorkAvailable -= Wake;
            else
                _session.WorkAvailable -= Wake;
        }
    }

    private void Wake()
    {
        lock (_gate)
        {
            if (_disposed || Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0)
                return;
            var @event = new SDL.Event { Type = _eventType };
            if (!_push(ref @event))
                _fatal("SDL_PushEvent(owner work) failed: " + SDL.GetError());
        }
    }
}
