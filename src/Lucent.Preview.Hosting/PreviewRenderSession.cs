using System.Globalization;
using System.Threading.Channels;
using Lucent.Core;
using Lucent.Preview.Protocol;
using Lucent.Renderer.Skia;

namespace Lucent.Preview.Hosting;

/// <summary>One internal live preview owner; transport and process reaping remain separate.</summary>
internal sealed class PreviewRenderSession : IAsyncDisposable, IApplicationHost
{
    private const int MaximumCommands = 256;
    private const int MaximumPointers = 16;
    private const int MaximumPressedKeys = 128;
    private const int MaximumTextLength = 4096;
    private readonly object _gate = new();
    private readonly Queue<ICommand> _commands = new();
    private readonly AutoResetEvent _available = new(false);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<PreviewRenderSession> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly Channel<PreviewRenderedFrame> _frames =
        Channel.CreateBounded<PreviewRenderedFrame>(
            new BoundedChannelOptions(1)
            {
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
    private readonly Dictionary<int, PointerState> _pointers = [];
    private readonly HashSet<Key> _pressedKeys = [];
    private readonly Dictionary<
        Key,
        (ElementIdentity? Owner, PreviewFrameToken Frame)
    > _keyOwners = [];
    private readonly PreviewScenario _scenario;
    private readonly PreviewPresentation _presentation;
    private readonly Thread _thread;
    private PreviewRenderDiagnostics _diagnostics = new(0, 0, 0, 0, 0, 0, 0, 0, false);
    private PreviewFrameRenderer? _renderer;
    private ApplicationSession? _session;
    private RetainedScene? _scene;
    private PreviewRenderedFrame? _inFlight;
    private PreviewFrameToken? _currentFrame;
    private PreviewFrameToken? _displayedFrame;
    private FrameInputMetadata? _displayedInput;
    private FrameInputMetadata? _inFlightInput;
    private bool _displayedInputCompatible;
    private long _admissionEpoch;
    private long _lastInput;
    private long _frameSequence;
    private long _loopTurns;
    private bool _dirty = true;
    private bool _focused = true;
    private bool _stopping;
    private bool _terminated;
    private Exception? _stopError;
    private Exception? _startupError;
    private CancellationTokenRegistration _externalStop;

    private PreviewRenderSession(PreviewScenario scenario, PreviewWorkerRequest request)
    {
        PreviewProtocol.Validate(request);
        if (scenario.Descriptor.Id != request.ScenarioId)
            throw new ArgumentException(
                "The request does not identify this scenario.",
                nameof(request)
            );
        _scenario = scenario;
        Identity = PreviewSessionIdentity.From(request);
        var defaults = scenario.Descriptor.Presentation;
        var appearance = new ThemeAppearance(
            request.ColorScheme == "light" ? ThemeColorScheme.Light : ThemeColorScheme.Dark,
            request.Contrast == "normal" ? ThemeContrast.Normal : ThemeContrast.High
        );
        _presentation = new(
            new((float)request.LogicalWidth, (float)request.LogicalHeight, (float)request.Scale),
            appearance,
            _ =>
            {
                ThrowIfStopping();
                return defaults.ThemeFactory(appearance);
            },
            (float)request.Density,
            defaults.Culture,
            defaults.UICulture,
            defaults.InitialTime
        );
        _thread = new(RunOwner) { IsBackground = true, Name = "Lucent live preview" };
    }

    internal PreviewSessionIdentity Identity { get; }
    internal Task Completion => _completion.Task;
    internal PreviewRenderDiagnostics Diagnostics => Volatile.Read(ref _diagnostics);

    internal static Task<PreviewRenderSession> StartAsync(
        PreviewScenario scenario,
        PreviewWorkerRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var owner = new PreviewRenderSession(scenario, request);
        try
        {
            owner._externalStop = cancellationToken.Register(
                static state => _ = ((PreviewRenderSession)state!).StopAsync(),
                owner
            );
            owner._thread.Start();
            return owner._ready.Task;
        }
        catch
        {
            owner._externalStop.Dispose();
            owner._lifetime.Dispose();
            owner._available.Dispose();
            throw;
        }
    }

    internal ValueTask<PreviewRenderedFrame> ReadFrameAsync(CancellationToken cancellationToken) =>
        _frames.Reader.ReadAsync(cancellationToken);

    internal Task<PreviewFrameAcknowledgment> AcknowledgeAsync(PreviewFrameToken token) =>
        Enqueue(() =>
        {
            if (_inFlight?.Token != token || _frames.Reader.Count != 0)
                return default;
            // Cleanup can replace/dispose the scene before acknowledgment. Keep only its token.
            var presented = _session!.Composition.TryAcknowledgePresentation(
                _inFlight.SceneGeneration
            );
            _displayedFrame = token;
            _displayedInput = _inFlightInput?.Epoch == _admissionEpoch ? _inFlightInput : null;
            _displayedInputCompatible = _scene is not null && CompatibleDisplayedInput(_scene);
            _inFlightInput = null;
            _inFlight = null;
            return new PreviewFrameAcknowledgment(true, presented);
        });

    internal Task<InputDispatchResult?> PointerAsync(
        PreviewFrameToken frame,
        long sequence,
        PointerCommand command
    )
    {
        command.Validate();
        return Input(
            frame,
            sequence,
            () =>
            {
                if (
                    command.Kind == PointerCommandKind.Down
                    && !_pointers.ContainsKey(command.PointerId)
                    && _pointers.Count >= MaximumPointers
                )
                    throw new InvalidOperationException("Preview pointer count exceeds its bound.");
                var result = _session!.Composition.Input.DispatchPointer(command);
                if (result.Status == InputDispatchStatus.Delivered)
                {
                    if (
                        command.Kind == PointerCommandKind.Down
                        && !_pointers.ContainsKey(command.PointerId)
                    )
                        _pointers.Add(command.PointerId, new(command));
                    if (_pointers.TryGetValue(command.PointerId, out var pointer))
                    {
                        pointer.Last = command;
                        if (command.Kind == PointerCommandKind.Down)
                        {
                            pointer.Buttons.Add(command.Button);
                            pointer.Origins.TryAdd(command.Button, frame);
                        }
                        if (command.Kind == PointerCommandKind.Up)
                        {
                            if (command.Button == PointerButton.None)
                            {
                                pointer.Buttons.Clear();
                                pointer.Origins.Clear();
                            }
                            else
                            {
                                pointer.Buttons.Remove(command.Button);
                                pointer.Origins.Remove(command.Button);
                            }
                        }
                        if (command.Kind == PointerCommandKind.Cancel || pointer.Buttons.Count == 0)
                            _pointers.Remove(command.PointerId);
                    }
                }
                return result;
            },
            () =>
            {
                if (
                    command.Kind is not (PointerCommandKind.Up or PointerCommandKind.Cancel)
                    || !_pointers.TryGetValue(command.PointerId, out var pointer)
                    || command.Kind == PointerCommandKind.Up
                        && command.Button != PointerButton.None
                        && !pointer.Buttons.Contains(command.Button)
                    || !pointer.Origins.ContainsValue(frame)
                    || command.Kind == PointerCommandKind.Up
                        && command.Button != PointerButton.None
                        && pointer.Origins.GetValueOrDefault(command.Button) != frame
                )
                    return;
                // Reconcile against current geometry, then route only to surviving capture.
                // Losing that original owner never permits a fresh hit-test activation.
                Project();
                _session!.Composition.Input.DispatchCapturedPointerRelease(command);
                if (
                    command.Kind == PointerCommandKind.Cancel
                    || command.Button == PointerButton.None
                )
                {
                    pointer.Buttons.Clear();
                    pointer.Origins.Clear();
                }
                else
                {
                    pointer.Buttons.Remove(command.Button);
                    pointer.Origins.Remove(command.Button);
                }
                if (pointer.Buttons.Count == 0)
                    _pointers.Remove(command.PointerId);
            },
            () =>
                command.Kind == PointerCommandKind.Down && !_pointers.ContainsKey(command.PointerId)
        );
    }

    internal Task<InputDispatchResult?> WheelAsync(
        PreviewFrameToken frame,
        long sequence,
        WheelCommand command
    )
    {
        command.Validate();
        return Input(
            frame,
            sequence,
            () => _session!.Composition.Input.DispatchWheel(command),
            acceptDisplayed: static () => true
        );
    }

    internal Task<InputDispatchResult?> KeyAsync(
        PreviewFrameToken frame,
        long sequence,
        KeyCommand command
    )
    {
        command.Validate();
        return Input(
            frame,
            sequence,
            () =>
            {
                if (command.Kind == KeyCommandKind.Up)
                {
                    if (!_keyOwners.TryGetValue(command.Key, out var owner))
                        return null;
                    if (owner.Owner != _session!.Composition.Input.FocusedElement)
                    {
                        _pressedKeys.Remove(command.Key);
                        _keyOwners.Remove(command.Key);
                        return null;
                    }
                    if (owner.Frame != frame)
                        return null;
                }
                if (
                    command.Kind == KeyCommandKind.Down
                    && !_pressedKeys.Contains(command.Key)
                    && _pressedKeys.Count >= MaximumPressedKeys
                )
                    throw new InvalidOperationException(
                        "Preview pressed-key count exceeds its bound."
                    );
                var result = _session!.Composition.Input.DispatchKey(command);
                if (result.Status == InputDispatchStatus.Delivered)
                {
                    if (command.Kind == KeyCommandKind.Down)
                    {
                        if (_pressedKeys.Add(command.Key))
                            _keyOwners[command.Key] = (result.Target, frame);
                    }
                    else
                    {
                        _pressedKeys.Remove(command.Key);
                        _keyOwners.Remove(command.Key);
                    }
                }
                return result;
            },
            () =>
            {
                if (command.Kind != KeyCommandKind.Up || !_pressedKeys.Contains(command.Key))
                    return;
                var owner = _keyOwners[command.Key];
                if (owner.Frame != frame)
                    return;
                Project();
                if (owner.Owner == _session!.Composition.Input.FocusedElement)
                    ReleaseInput(
                        () =>
                            _session.Composition.Input.DispatchKey(
                                new(KeyCommandKind.Up, command.Key)
                            ),
                        () => owner.Owner == _session.Composition.Input.FocusedElement
                    );
                _pressedKeys.Remove(command.Key);
                _keyOwners.Remove(command.Key);
            }
        );
    }

    internal Task<InputDispatchResult?> TextAsync(
        PreviewFrameToken frame,
        long sequence,
        TextInputCommand command
    )
    {
        command.Validate();
        if (command.Kind != TextInputKind.Commit || command.Text.Length > MaximumTextLength)
            throw new ArgumentException(
                "Live preview accepts bounded committed text.",
                nameof(command)
            );
        return Input(frame, sequence, () => _session!.Composition.Input.DispatchText(command));
    }

    internal Task<bool> SetFocusAsync(PreviewSessionIdentity identity, bool focused) =>
        Enqueue(() =>
        {
            if (identity != Identity)
                return false;
            if (_focused == focused)
                return true;
            _focused = focused;
            if (!focused)
            {
                CleanupInput();
                _dirty = true;
            }
            return true;
        });

    private Task<InputDispatchResult?> Input(
        PreviewFrameToken frame,
        long sequence,
        Func<InputDispatchResult?> dispatch,
        Action? releaseStale = null,
        Func<bool>? acceptDisplayed = null
    ) =>
        Enqueue<InputDispatchResult?>(() =>
        {
            if (
                frame.Session != Identity
                || frame.Sequence is < 1
                || frame.Sequence > _frameSequence
                || sequence <= _lastInput
                || !_focused
            )
                return null;
            _lastInput = sequence;
            if (
                (_currentFrame is null || frame != _currentFrame)
                && !(
                    frame == _displayedFrame
                    && _displayedInputCompatible
                    && acceptDisplayed?.Invoke() == true
                )
            )
            {
                releaseStale?.Invoke();
                return null;
            }
            var result = dispatch();
            if (result is not null)
                _dirty = true;
            return result;
        });

    internal Task StopAsync()
    {
        lock (_gate)
        {
            if (_stopping || _terminated)
                return Completion;
            _stopping = true;
            _available.Set();
        }
        return Completion;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private Task<T> Enqueue<T>(Func<T> callback)
    {
        lock (_gate)
        {
            if (_stopping || _terminated)
                return Task.FromException<T>(
                    new ObjectDisposedException(nameof(PreviewRenderSession))
                );
            if (_commands.Count >= MaximumCommands)
            {
                var error = new InvalidOperationException("Preview command queue is full.");
                _stopError = error;
                _ = StopAsync();
                return Task.FromException<T>(error);
            }
            var command = new Command<T>(callback);
            _commands.Enqueue(command);
            _available.Set();
            return command.Task;
        }
    }

    private void Wake()
    {
        lock (_gate)
            if (!_terminated)
                _available.Set();
    }

    private bool IsStopping
    {
        get
        {
            lock (_gate)
                return _stopping;
        }
    }

    private void RunOwner()
    {
        Exception? failure = null;
        var lifetimeToken = _lifetime.Token;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUICulture = CultureInfo.CurrentUICulture;
        try
        {
            ThrowIfStopping();
            CultureInfo.CurrentCulture = _presentation.Culture;
            CultureInfo.CurrentUICulture = _presentation.UICulture;
            _renderer = new(_presentation.Viewport);
            var clock = new PreviewSessionClock(_presentation.InitialTime);
            var builder = LucentApplication.CreateBuilder().UseHost(this);
            ThrowIfStopping();
            var binding = _scenario.Bind(builder, clock, _presentation, _lifetime.Token);
            ThrowIfStopping();
            builder
                .OnDispose(context =>
                {
                    _startupError = context.Startup.Error;
                    return ValueTask.CompletedTask;
                })
                .Build()
                .Run(() =>
                {
                    ThrowIfStopping();
                    return binding.CreateRoot(_session!);
                });
            if (_stopError is { } stopError)
                throw stopError;
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            try
            {
                _renderer?.Dispose();
            }
            catch (Exception error)
            {
                failure = Combine(failure, error);
            }
            PublishDiagnostics(disposed: true);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUICulture;
            _externalStop.Dispose();
            lock (_gate)
            {
                _terminated = true;
                while (_commands.TryDequeue(out var command))
                    command.Fail(
                        failure ?? new ObjectDisposedException(nameof(PreviewRenderSession))
                    );
                _inFlight = null;
                _inFlightInput = null;
                _displayedInput = null;
                _displayedFrame = null;
                while (_frames.Reader.TryRead(out _)) { }
                _frames.Writer.TryComplete(failure);
                _lifetime.Dispose();
                _available.Dispose();
            }
        }
        // Only the authoritative startup cancellation may become clean Stop. Cleanup
        // failures remain separate or aggregated and can never match this reference.
        if (
            IsStopping
            && (
                failure is PreviewStoppedBeforeStartupException
                || ReferenceEquals(failure, _startupError)
                    && failure is OperationCanceledException canceled
                    && canceled.CancellationToken == lifetimeToken
            )
        )
            failure = null;
        if (failure is null)
        {
            _ready.TrySetCanceled();
            _completion.TrySetResult();
        }
        else
        {
            _completion.TrySetException(failure);
            if (_ready.TrySetException(failure))
                _ = _completion.Task.Exception; // Startup never handed the owner to its caller.
        }
    }

    int IApplicationHost.Run(ApplicationSession session)
    {
        _session = session;
        var composition = session.Composition;
        session.WorkAvailable += Wake;
        composition.PresentationDemandAvailable += Wake;
        Exception? failure = null;
        try
        {
            session.Theme.Appearance = _presentation.Appearance;
            composition.ConfigureImages(new ImageCache(new SkiaImagePreparer()));
            composition.SetPresentationAvailable(true);
            ThrowIfStopping();
            session.Start();
            while (!IsStopping && !session.IsCompleted)
            {
                _loopTurns++;
                using var context = session.EnterContext();
                _dirty |= session.ProcessEvents();
                if (session.IsCompleted || composition.IsDisposed)
                    break;
                _dirty |= composition.Flush();
                for (
                    var commandIndex = 0;
                    commandIndex < MaximumCommands && !IsStopping;
                    commandIndex++
                )
                {
                    ICommand? command;
                    lock (_gate)
                        command = _commands.TryDequeue(out var next) ? next : null;
                    if (command is null)
                        break;
                    try
                    {
                        command.Run();
                        _dirty |= session.ProcessEvents();
                        if (!session.IsCompleted && !composition.IsDisposed)
                            _dirty |= composition.Flush();
                        command.Complete();
                    }
                    catch (Exception error)
                    {
                        command.Fail(error);
                        throw;
                    }
                    if (session.IsCompleted || composition.IsDisposed)
                        break;
                }
                if (session.IsCompleted || composition.IsDisposed || IsStopping)
                    break;
                var timeout = _inFlight is null ? PresentationWait() : -1;
                if (timeout == 0)
                    _dirty = true;
                if (session.Status.Phase == ApplicationPhase.Running && _dirty && _inFlight is null)
                {
                    Project();
                    var png = _renderer!.Capture(_scene!, _focused);
                    if (png.Length > PreviewProtocol.MaximumFrameBytes)
                        throw new InvalidDataException("Preview PNG exceeds its bound.");
                    var viewport = _presentation.Viewport;
                    _currentFrame = new(Identity, checked(++_frameSequence));
                    _inFlight = new(
                        _currentFrame,
                        _scene!.Generation,
                        png,
                        (int)MathF.Ceiling(viewport.Width * viewport.Scale),
                        (int)MathF.Ceiling(viewport.Height * viewport.Scale)
                    );
                    _inFlightInput = new(
                        _admissionEpoch,
                        _scene.Viewport,
                        _scene.Input,
                        _scene.ScrollBars
                    );
                    if (!_frames.Writer.TryWrite(_inFlight))
                        throw new InvalidOperationException("Preview frame slot is occupied.");
                    _dirty = false;
                    _ready.TrySetResult(this);
                }
                PublishDiagnostics();
                // Backpressure gates frame deadlines, never lifecycle or stop wakes.
                _available.WaitOne(_inFlight is null ? PresentationWait() : -1);
            }
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            // Cancellation, reprojection and routed cleanup all invoke author code.
            using var context = session.EnterContext();
            try
            {
                _lifetime.Cancel();
            }
            catch (Exception error)
            {
                failure = Combine(failure, error);
            }
            if (!composition.IsDisposed)
            {
                try
                {
                    CleanupInput();
                }
                catch (Exception error)
                {
                    failure = Combine(failure, error);
                }
                try
                {
                    composition.SetPresentationAvailable(false);
                    session.RequestClose();
                }
                catch (Exception error)
                {
                    failure = Combine(failure, error);
                }
            }
            session.WorkAvailable -= Wake;
            composition.PresentationDemandAvailable -= Wake;
            try
            {
                _scene?.Dispose();
            }
            catch (Exception error)
            {
                failure = Combine(failure, error);
            }
            _scene = null;
        }
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return 0;
    }

    private int PresentationWait()
    {
        var demand = _session!.Composition.PresentationDemand;
        if (!demand.IsActive)
            return -1;
        var now = TimeProvider.System.GetElapsedTime(0, TimeProvider.System.GetTimestamp());
        var remaining = demand.NextDeadline.GetValueOrDefault(now) - now;
        return remaining <= TimeSpan.Zero ? 0
            : remaining.TotalMilliseconds >= int.MaxValue ? int.MaxValue
            : Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds));
    }

    private void Project()
    {
        // Reprojection can install geometry that has never been published to the consumer.
        // Its outstanding transport acknowledgment remains independently valid.
        _currentFrame = null;
        _dirty = true;
        var composition = _session!.Composition;
        composition.SamplePresentation(
            TimeProvider.System.GetElapsedTime(0, TimeProvider.System.GetTimestamp())
        );
        for (var attempt = 0; attempt < 3; attempt++)
        {
            composition.Flush();
            var next = SceneLayout.ProjectFrame(
                composition,
                _presentation.Viewport,
                _renderer!.Renderer,
                _scene
            );
            bool accepted;
            try
            {
                accepted = composition.Input.SetScene(next);
            }
            catch
            {
                next.Dispose();
                throw;
            }
            if (accepted)
            {
                var previous = _scene;
                _scene = next;
                _displayedInputCompatible = CompatibleDisplayedInput(next);
                previous?.Dispose();
                return;
            }
            next.Dispose();
        }
        throw new InvalidOperationException("Core rejected three consecutive preview scenes.");
    }

    private void CleanupInput()
    {
        _admissionEpoch = checked(_admissionEpoch + 1);
        _displayedInput = null;
        _displayedInputCompatible = false;
        _currentFrame = null;
        _dirty = true;
        List<Exception> failures = [];
        try
        {
            foreach (var state in _pointers.Values)
                try
                {
                    ReleaseInput(() =>
                        _session!.Composition.Input.DispatchPointer(
                            new(
                                PointerCommandKind.Cancel,
                                state.Last.PointerId,
                                state.Last.X,
                                state.Last.Y
                            )
                        )
                    );
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            foreach (var key in _pressedKeys)
                try
                {
                    if (_keyOwners.TryGetValue(key, out var owner))
                        ReleaseInput(
                            () =>
                                _session!.Composition.Input.DispatchKey(
                                    new(KeyCommandKind.Up, key)
                                ),
                            () => owner.Owner == _session!.Composition.Input.FocusedElement
                        );
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            try
            {
                if (_scene is not null)
                    Project();
                _session!.Composition.Input.ClearPointerHover();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        finally
        {
            _pointers.Clear();
            _pressedKeys.Clear();
            _keyOwners.Clear();
        }
        if (failures.Count != 0)
            throw new AggregateException("Preview input cleanup failed.", failures);
    }

    private void ReleaseInput(Func<InputDispatchResult> release, Func<bool>? stillOwned = null)
    {
        // Only synthetic cleanup releases may reconcile and retry a stale rejection.
        // External commands remain bound to the exact published frame token.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Project();
            if (stillOwned?.Invoke() == false)
                return;
            var result = release();
            if (result.Status == InputDispatchStatus.Delivered)
                return;
            if (result.Rejection != InputRejection.StaleScene)
                throw new InvalidOperationException(
                    "Core rejected preview input cleanup: " + result.Rejection
                );
        }
        throw new InvalidOperationException(
            "Core rejected three consecutive preview input releases."
        );
    }

    private bool CompatibleDisplayedInput(RetainedScene scene)
    {
        if (
            _displayedInput is not { } displayed
            || displayed.Epoch != _admissionEpoch
            || displayed.Viewport != scene.Viewport
            || !(
                ReferenceEquals(displayed.Input, scene.Input)
                || displayed.Input.SequenceEqual(scene.Input)
            )
            || displayed.ScrollBars.Count != scene.ScrollBars.Count
        )
            return false;
        if (ReferenceEquals(displayed.ScrollBars, scene.ScrollBars))
            return true;
        for (var index = 0; index < displayed.ScrollBars.Count; index++)
        {
            var original = displayed.ScrollBars[index];
            var current = scene.ScrollBars[index];
            if (
                original.Viewport != current.Viewport
                || original.Track != current.Track
                || original.Thumb != current.Thumb
                || original.Maximum != current.Maximum
                || original.CornerRadius != current.CornerRadius
            )
                return false;
        }
        return true;
    }

    private sealed record FrameInputMetadata(
        long Epoch,
        LayoutViewport Viewport,
        IReadOnlyList<RetainedInputElement> Input,
        IReadOnlyList<RetainedScrollBar> ScrollBars
    );

    private void ThrowIfStopping()
    {
        if (IsStopping)
            throw new PreviewStoppedBeforeStartupException();
    }

    private sealed class PreviewStoppedBeforeStartupException : OperationCanceledException { }

    private void PublishDiagnostics(bool disposed = false) =>
        Volatile.Write(
            ref _diagnostics,
            new(
                _frameSequence,
                _loopTurns,
                Environment.CurrentManagedThreadId,
                _pointers.Count,
                _pressedKeys.Count,
                _renderer?.SurfaceBytes ?? 0,
                _renderer?.Renderer.TextBlobCreationCount ?? 0,
                _renderer?.Renderer.LiveTextBlobCount ?? 0,
                disposed
            )
        );

    private static Exception Combine(Exception? first, Exception second) =>
        first is null ? second : new AggregateException(first, second);

    private interface ICommand
    {
        void Run();
        void Complete();
        void Fail(Exception error);
    }

    private sealed class PointerState(PointerCommand initial)
    {
        internal PointerCommand Last { get; set; } = initial;
        internal HashSet<PointerButton> Buttons { get; } = [];
        internal Dictionary<PointerButton, PreviewFrameToken> Origins { get; } = [];
    }

    private sealed class Command<T>(Func<T> callback) : ICommand
    {
        private readonly TaskCompletionSource<T> _result = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private T _value = default!;
        internal Task<T> Task => _result.Task;

        public void Run() => _value = callback();

        public void Complete() => _result.TrySetResult(_value);

        public void Fail(Exception error) => _result.TrySetException(error);
    }
}
