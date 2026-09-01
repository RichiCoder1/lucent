using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>Translates Core's edge notification into one coalesced SDL event; only the owner drains Core.</summary>
internal sealed class WindowsWorkDispatcher : IDisposable
{
    internal delegate bool PushEvent(ref SDL.Event @event);
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Composition _composition;
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

    internal uint EventType => _eventType;

    internal static void ValidateEventType(uint eventType)
    {
        if (eventType == 0)
            throw new InvalidOperationException(
                "SDL_RegisterEvents(reactive work) failed: " + SDL.GetError()
            );
    }

    internal bool IsWakeEvent(SDL.Event @event) => @event.Type == _eventType;

    /// <summary>Resets before draining so a post after the empty check gets a new wake.</summary>
    internal bool Process()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "Reactive work must drain on the SDL owner thread."
            );
        lock (_gate)
        {
            if (_disposed)
                return false;
            Interlocked.Exchange(ref _scheduled, 0);
            return _composition.Flush();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _composition.WorkAvailable -= Wake;
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
                _fatal("SDL_PushEvent(reactive work) failed: " + SDL.GetError());
        }
    }
}
