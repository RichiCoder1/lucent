namespace Lucent.Core;

internal static class TooltipDefaults
{
    internal static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan DismissGrace = TimeSpan.FromMilliseconds(150);
}

public static partial class Components
{
    [LucentComponent]
    internal static ComponentRecipe TooltipFrame(
        string description,
        TimeSpan? delay,
        TimeProvider? timeProvider,
        [DefaultContent] ComponentContent content,
        Style? style = null
    )
    {
        description = Required(description, nameof(description));
        content = Content(content);
        var effectiveDelay = delay ?? TooltipDefaults.Delay;
        if (effectiveDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay));
        var effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        return ComponentRecipe.Create(
            "tooltip-anchor",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    component: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column),
                    author: style
                );
                context.Mount(root, content);
                root.AttachBehaviors(
                    new TooltipBehavior(
                        root,
                        context.Theme,
                        description,
                        effectiveDelay,
                        effectiveTimeProvider
                    )
                );
            }
        );
    }

    private sealed class TooltipBehavior(
        Element target,
        ThemeContext theme,
        string description,
        TimeSpan delay,
        TimeProvider timeProvider
    ) : Behavior
    {
        private readonly Element _target = target;
        private readonly ThemeContext _theme = theme;
        private readonly string _description = description;
        private readonly TimeSpan _delay = delay;
        private readonly TimeProvider _timeProvider = timeProvider;
        private Func<Action, IDisposable>? _post;
        private TooltipTimerSlot? _timerSlot;
        private OwnedSurfaceRequest? _surface;
        private bool _hovered;
        private bool _focused;
        private bool _surfacePointerInside;
        private bool _escapeSuppressed;
        private long _timerGeneration;

        public override string Name => "tooltip";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Focus;

        public override void Attach(BehaviorContext context)
        {
            _post = context.Post;
            _timerSlot = context.Own(new TooltipTimerSlot());
            context.SetSupplementalDescription(_description);
            context.RegisterTooltip(SetHovered, SetFocused, Escape);
            context.OnKey(route =>
            {
                if (
                    route.Command is { Kind: KeyCommandKind.Down, Key: Key.Escape, IsRepeat: false }
                    && !context.CompositionInput().HasTextComposition
                )
                {
                    Escape();
                    route.Handled = true;
                }
            });
            context.OnDispose(() =>
            {
                _post = null;
                CancelTimer();
                DismissSurface();
            });
        }

        private void Escape()
        {
            _escapeSuppressed = true;
            CancelTimer();
            DismissSurface();
        }

        private void SetHovered(bool hovered)
        {
            if (_hovered == hovered)
                return;
            _hovered = hovered;
            if (hovered)
            {
                _escapeSuppressed = false;
                if (_focused)
                    Show();
                else
                    Schedule();
            }
            else
            {
                CancelTimer();
                if (!_focused && !_surfacePointerInside)
                    ScheduleDismiss();
            }
        }

        private void SetFocused(bool focused)
        {
            if (_focused == focused)
                return;
            _focused = focused;
            if (focused)
            {
                _escapeSuppressed = false;
                CancelTimer();
                Show();
            }
            else if (!_hovered && !_surfacePointerInside)
            {
                ScheduleDismiss();
            }
        }

        private void Schedule()
        {
            if (_escapeSuppressed)
                return;
            CancelTimer();
            if (_surface is { IsDismissed: false })
                return;
            ScheduleTimer(
                _delay,
                () =>
                {
                    if (_hovered && !_escapeSuppressed && _surface is not { IsDismissed: false })
                        Show();
                }
            );
        }

        private void ScheduleDismiss()
        {
            if (_surface is not { IsDismissed: false })
                return;
            CancelTimer();
            ScheduleTimer(
                TooltipDefaults.DismissGrace,
                () =>
                {
                    if (!_hovered && !_focused && !_surfacePointerInside)
                        DismissSurface();
                }
            );
        }

        private void ScheduleTimer(TimeSpan delay, Action callback)
        {
            var generation = checked(++_timerGeneration);
            var timerSlot = _timerSlot;
            if (timerSlot is null)
                return;
            timerSlot.Replace(
                new TooltipTimer(
                    _timeProvider,
                    delay,
                    () =>
                    {
                        var post = _post;
                        if (post is null)
                            return;
                        try
                        {
                            _ = post(() =>
                            {
                                if (generation == _timerGeneration)
                                    callback();
                            });
                        }
                        catch (ObjectDisposedException)
                        {
                            // Scope disposal races with a worker timer callback by design.
                        }
                    }
                )
            );
        }

        private void CancelTimer()
        {
            _timerSlot?.Clear();
            _timerGeneration = checked(_timerGeneration + 1);
        }

        private void Show()
        {
            if ((!_hovered && !_focused) || _escapeSuppressed)
                return;
            if (_surface is { IsDismissed: false })
                return;
            _surface?.Dispose();
            _surface = null;
            _surfacePointerInside = false;
            OwnedSurfaceRequest? request = null;
            request = new OwnedSurfaceRequest(
                _target,
                _theme,
                TooltipPopup(_description),
                interactive: false,
                consumeOutsideClick: false,
                closed: () => SurfaceClosed(request),
                pointerInsideChanged: inside => SurfacePointerChanged(request, inside)
            );
            _surface = request;
            try
            {
                _target.Composition.Input.RequestSurface(request);
            }
            catch
            {
                if (ReferenceEquals(_surface, request))
                    _surface = null;
                request.Dispose();
                throw;
            }
        }

        private void SurfacePointerChanged(OwnedSurfaceRequest? request, bool inside)
        {
            if (!ReferenceEquals(_surface, request))
                return;
            _surfacePointerInside = inside;
            if (inside)
                CancelTimer();
            else if (!_hovered && !_focused)
                ScheduleDismiss();
        }

        private void SurfaceClosed(OwnedSurfaceRequest? request)
        {
            if (!ReferenceEquals(_surface, request))
                return;
            _surface = null;
            _surfacePointerInside = false;
        }

        private void DismissSurface()
        {
            CancelTimer();
            var surface = _surface;
            _surface = null;
            _surfacePointerInside = false;
            surface?.Dismiss();
            surface?.Dispose();
        }

        private sealed class TooltipTimer : IDisposable
        {
            private readonly ITimer _timer;
            private Action? _callback;

            public TooltipTimer(TimeProvider provider, TimeSpan delay, Action callback)
            {
                _callback = callback;
                _timer = provider.CreateTimer(
                    static state => ((TooltipTimer)state!).Fire(),
                    this,
                    delay,
                    Timeout.InfiniteTimeSpan
                );
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _callback, null);
                _timer.Dispose();
            }

            private void Fire() => Volatile.Read(ref _callback)?.Invoke();
        }

        private sealed class TooltipTimerSlot : IDisposable
        {
            private readonly object _gate = new();
            private IDisposable? _timer;

            public void Replace(IDisposable timer)
            {
                ArgumentNullException.ThrowIfNull(timer);
                IDisposable? prior;
                lock (_gate)
                {
                    prior = _timer;
                    _timer = timer;
                }
                prior?.Dispose();
            }

            public void Clear()
            {
                IDisposable? prior;
                lock (_gate)
                {
                    prior = _timer;
                    _timer = null;
                }
                prior?.Dispose();
            }

            public void Dispose() => Clear();
        }
    }
}
