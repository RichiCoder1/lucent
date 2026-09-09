using System.Globalization;
using System.Text;

namespace Lucent.Core;

internal sealed class MotionTimeline(Action demandAvailable)
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromTicks(
        TimeSpan.TicksPerSecond / 60
    );
    private readonly Dictionary<(long Element, IProperty Property), ISlot> _slots = [];
    private readonly HashSet<ISlot> _active = [];
    private TimeSpan _timestamp;
    private bool _hasTimestamp;
    private bool _presentationAvailable = true;
    private long _demandRevision;
    private (long SceneGeneration, long PresentationRevision)? _captured;
    private HashSet<long>? _capturedElements;
    private readonly HashSet<long> _acknowledgedElements = [];
    private long _presentationRevision;
    private bool _activeDemand;
    private bool _batching;
    private bool _notifyAfterBatch;
    private long _starts;
    private long _retargets;
    private long _sampledTracks;
    private long _cancellations;
    private long _wakeRequests;

    internal PresentationDiagnostics Diagnostics =>
        new(_active.Count, _starts, _retargets, _sampledTracks, _cancellations, _wakeRequests);

    internal string Dump()
    {
        var output = new StringBuilder("presentation revision=")
            .Append(_demandRevision.ToString(CultureInfo.InvariantCulture))
            .Append(" active=")
            .Append(_active.Count.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (
            var item in _slots
                .OrderBy(item => item.Key.Element)
                .ThenBy(item => item.Key.Property.Name, StringComparer.Ordinal)
        )
            item.Value.AppendDump(output, item.Key.Element, _timestamp);
        return output.ToString();
    }

    internal PresentationFrameDemand Demand =>
        new(
            _presentationAvailable && _active.Count != 0,
            _presentationAvailable && _active.Count != 0 ? _timestamp + FrameInterval : null,
            _demandRevision
        );

    internal PresentationSampleResult Sample(TimeSpan timestamp)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timestamp, TimeSpan.Zero, nameof(timestamp));
        if (_hasTimestamp && timestamp < _timestamp)
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                "Presentation time cannot move backwards."
            );
        var timestampAdvanced = !_hasTimestamp || timestamp != _timestamp;
        _timestamp = timestamp;
        _hasTimestamp = true;
        var changed = false;
        if (_presentationAvailable)
        {
            _sampledTracks = checked(_sampledTracks + _active.Count);
            foreach (var slot in _active.ToArray())
            {
                changed |= slot.Sample(timestamp);
                if (slot.LastSampleCanceled)
                    _cancellations = checked(_cancellations + 1);
                Track(slot);
            }
            if (changed)
                _presentationRevision = checked(_presentationRevision + 1);
        }
        RefreshDemand(timestampAdvanced && _active.Count != 0);
        return new(changed, Demand);
    }

    internal bool Commit<T>(
        Element element,
        Property<T> property,
        T target,
        Motion? policy,
        string? policySource,
        ThemeContext? theme,
        bool controlOwned,
        bool suppressed,
        string? suppressionReason,
        long appearanceGeneration
    )
    {
        ValidateTarget(property.Transition, target);
        var key = (element.Id, (IProperty)property);
        if (!_slots.TryGetValue(key, out var untyped))
        {
            var created = new Slot<T>(
                property,
                target,
                target,
                policy,
                policySource,
                theme,
                appearanceGeneration,
                false
            );
            _slots.Add(key, created);
            _presentationRevision = checked(_presentationRevision + 1);
            Track(created);
            RefreshDemand();
            return true;
        }
        var wasActive = untyped.IsActive;
        var version = untyped.TargetVersion;
        var changed = ((Slot<T>)untyped).Commit(
            target,
            policy,
            policySource,
            controlOwned,
            suppressed || !_presentationAvailable,
            !_presentationAvailable ? "presentation-unavailable" : suppressionReason,
            appearanceGeneration,
            _timestamp
        );
        Track(untyped);
        if (changed)
            _presentationRevision = checked(_presentationRevision + 1);
        if (untyped.TargetVersion != version)
        {
            if (wasActive && untyped.IsActive)
                _retargets = checked(_retargets + 1);
            else if (!wasActive && untyped.IsActive)
                _starts = checked(_starts + 1);
        }
        if (wasActive && !untyped.IsActive)
            _cancellations = checked(_cancellations + 1);
        RefreshDemand();
        return changed;
    }

    internal bool TryRead<T>(Element element, Property<T> property, out T value)
    {
        if (_slots.TryGetValue((element.Id, property), out var slot))
        {
            value = ((Slot<T>)slot).Presented;
            return true;
        }
        value = default!;
        return false;
    }

    internal bool Contains<T>(Element element, Property<T> property) =>
        _slots.ContainsKey((element.Id, property));

    internal bool IsAcknowledged(Element element) => _acknowledgedElements.Contains(element.Id);

    internal void SeedAcknowledged<T>(
        Element element,
        Property<T> property,
        T priorTarget,
        T presented,
        Motion policy,
        string? policySource,
        ThemeContext theme,
        long appearanceGeneration,
        bool acknowledged
    )
    {
        Remove(element, property);
        var slot = new Slot<T>(
            property,
            priorTarget,
            presented,
            policy,
            policySource,
            theme,
            appearanceGeneration,
            acknowledged
        );
        _slots.Add((element.Id, property), slot);
        _presentationRevision = checked(_presentationRevision + 1);
    }

    internal void Remove(Element element)
    {
        var removed =
            RemoveCore(element.Id, VisualProperties.Background)
            | RemoveCore(element.Id, VisualProperties.Opacity)
            | RemoveCore(element.Id, TypographyProperties.TextColor);
        if (removed)
            _presentationRevision = checked(_presentationRevision + 1);
        _acknowledgedElements.Remove(element.Id);
        _capturedElements?.Remove(element.Id);
        RefreshDemand();
    }

    private bool RemoveCore(long elementId, IProperty property)
    {
        if (_slots.Remove((elementId, property), out var slot))
        {
            if (slot.IsActive)
                _cancellations = checked(_cancellations + 1);
            _active.Remove(slot);
            return true;
        }
        return false;
    }

    internal void Remove<T>(Element element, Property<T> property)
    {
        if (_slots.Remove((element.Id, property), out var slot))
        {
            if (slot.IsActive)
                _cancellations = checked(_cancellations + 1);
            _active.Remove(slot);
            _presentationRevision = checked(_presentationRevision + 1);
        }
        RefreshDemand();
    }

    internal void SetAvailable(bool available)
    {
        if (_presentationAvailable == available)
            return;
        _presentationAvailable = available;
        if (!available)
        {
            if (_active.Count != 0)
                _presentationRevision = checked(_presentationRevision + 1);
            _cancellations = checked(_cancellations + _active.Count);
            foreach (var slot in _slots.Values)
            {
                slot.Snap("presentation-unavailable");
                _active.Remove(slot);
            }
        }
        RefreshDemand();
    }

    internal void Capture(long sceneGeneration, IEnumerable<long> elementIds)
    {
        _captured = (sceneGeneration, _presentationRevision);
        _capturedElements = elementIds.ToHashSet();
    }

    internal bool Acknowledge(long sceneGeneration)
    {
        if (
            _captured is not { } captured
            || captured.SceneGeneration != sceneGeneration
            || captured.PresentationRevision != _presentationRevision
        )
            return false;
        foreach (var slot in _slots.Values)
            slot.Acknowledge();
        _acknowledgedElements.UnionWith(_capturedElements ?? []);
        _captured = null;
        _capturedElements = null;
        return true;
    }

    internal void Clear()
    {
        _slots.Clear();
        _active.Clear();
        _captured = null;
        _capturedElements = null;
        _acknowledgedElements.Clear();
        RefreshDemand();
    }

    internal void CancelForFailure()
    {
        _cancellations = checked(_cancellations + _active.Count);
        foreach (var slot in _active)
            slot.Snap("fatal-failure");
        _active.Clear();
        _captured = null;
        _capturedElements = null;
        _batching = false;
        _notifyAfterBatch = false;
        RefreshDemand();
    }

    internal void BeginCommitBatch() => _batching = true;

    internal void EndCommitBatch()
    {
        _batching = false;
        if (_notifyAfterBatch && _activeDemand)
        {
            _notifyAfterBatch = false;
            _wakeRequests = checked(_wakeRequests + 1);
            try
            {
                demandAvailable();
            }
            catch
            {
                CancelForFailure();
                throw;
            }
        }
        _notifyAfterBatch = false;
    }

    private void RefreshDemand(bool deadlineChanged = false)
    {
        var active = _presentationAvailable && _active.Count != 0;
        var priorActive = _activeDemand;
        _activeDemand = active;
        if (active == priorActive && !deadlineChanged)
            return;
        _demandRevision = checked(_demandRevision + 1);
        if (active && !priorActive)
        {
            if (_batching)
                _notifyAfterBatch = true;
            else
            {
                _wakeRequests = checked(_wakeRequests + 1);
                demandAvailable();
            }
        }
    }

    private void Track(ISlot slot)
    {
        if (slot.IsActive)
            _active.Add(slot);
        else
            _active.Remove(slot);
    }

    private interface ISlot
    {
        bool IsActive { get; }
        long TargetVersion { get; }
        bool LastSampleCanceled { get; }
        bool Sample(TimeSpan timestamp);
        void Snap(string reason);
        void Acknowledge();
        void AppendDump(StringBuilder output, long elementId, TimeSpan timestamp);
    }

    private sealed class Slot<T>(
        Property<T> property,
        T target,
        T presented,
        Motion? policy,
        string? policySource,
        ThemeContext? theme,
        long appearanceGeneration,
        bool acknowledged
    ) : ISlot
    {
        private T _target = target;
        private Motion _motion;
        private TimeSpan _started;
        private bool _acknowledged = acknowledged;
        private long _appearanceGeneration = appearanceGeneration;
        private Motion? _policy = policy;
        private string? _policySource = policySource;
        private string? _capturedPolicySource;
        private string _reason = "first-presentation";
        private object _preparedPresented = Prepare(property.Transition, presented);
        private object _preparedStart = Prepare(property.Transition, presented);
        private object _preparedTarget = Prepare(property.Transition, target);

        internal T Presented { get; private set; } = presented;
        public bool IsActive { get; private set; }
        public long TargetVersion { get; private set; }
        public bool LastSampleCanceled { get; private set; }

        internal bool Commit(
            T target,
            Motion? policy,
            string? policySource,
            bool controlOwned,
            bool suppressed,
            string? suppressionReason,
            long appearanceGeneration,
            TimeSpan timestamp
        )
        {
            var appearanceChanged = _appearanceGeneration != appearanceGeneration;
            _appearanceGeneration = appearanceGeneration;
            _policy = policy;
            _policySource = policySource;
            if ((appearanceChanged || controlOwned || suppressed) && IsActive)
            {
                var changed = !EqualityComparer<T>.Default.Equals(Presented, target);
                _target = target;
                Presented = target;
                _preparedPresented = Prepare(property.Transition, target);
                IsActive = false;
                _reason =
                    controlOwned ? "control-authority"
                    : appearanceChanged ? "appearance-change"
                    : suppressionReason ?? "suppressed";
                return changed;
            }
            if (EqualityComparer<T>.Default.Equals(_target, target))
            {
                if (
                    (
                        controlOwned
                        || suppressed
                        || policy is null
                        || policy.Value.DurationMilliseconds == 0
                    ) && IsActive
                )
                {
                    Presented = target;
                    IsActive = false;
                    _reason =
                        controlOwned ? "control-authority"
                        : suppressed ? suppressionReason ?? "suppressed"
                        : "policy-none";
                    return true;
                }
                return false;
            }

            _target = target;
            TargetVersion = checked(TargetVersion + 1);
            if (
                !_acknowledged
                || controlOwned
                || suppressed
                || appearanceChanged
                || policy is null
                || policy.Value.DurationMilliseconds == 0
                || !CanInterpolate(property.Transition, Presented, target)
            )
            {
                var changed = !EqualityComparer<T>.Default.Equals(Presented, target);
                Presented = target;
                _preparedPresented = Prepare(property.Transition, target);
                IsActive = false;
                _reason =
                    !_acknowledged ? "first-presentation"
                    : controlOwned ? "control-authority"
                    : suppressed ? suppressionReason ?? "suppressed"
                    : appearanceChanged ? "appearance-change"
                    : policy is null ? "no-policy"
                    : policy.Value.DurationMilliseconds == 0 ? "policy-none"
                    : "pair-ineligible";
                return changed;
            }

            if (EqualityComparer<T>.Default.Equals(Presented, target))
            {
                _preparedPresented = Prepare(property.Transition, target);
                IsActive = false;
                _reason = "target-equals-sample";
                return false;
            }

            _preparedStart = _preparedPresented;
            _preparedTarget = Prepare(property.Transition, target);
            _motion = policy.Value;
            _capturedPolicySource = policySource;
            _started = timestamp;
            IsActive = true;
            _reason = "active";
            return false;
        }

        public bool Sample(TimeSpan timestamp)
        {
            LastSampleCanceled = false;
            if (!IsActive)
                return false;
            if (
                theme is not null
                && (
                    theme.IsReducedMotion
                    || theme.Appearance.Contrast == ThemeContrast.High
                    || theme.AppearanceGeneration != _appearanceGeneration
                )
            )
            {
                var suppressionChanged = !EqualityComparer<T>.Default.Equals(Presented, _target);
                Presented = _target;
                _preparedPresented = Prepare(property.Transition, _target);
                IsActive = false;
                LastSampleCanceled = true;
                _reason =
                    theme.IsReducedMotion ? "reduced-motion"
                    : theme.Appearance.Contrast == ThemeContrast.High ? "high-contrast"
                    : "appearance-change";
                return suppressionChanged;
            }
            var elapsed = timestamp - _started;
            var progress = Math.Clamp(
                elapsed.TotalMilliseconds / _motion.DurationMilliseconds,
                0d,
                1d
            );
            T next;
            if (progress >= 1d)
            {
                next = _target;
                _preparedPresented = _preparedTarget;
            }
            else
            {
                _preparedPresented = InterpolatePrepared(
                    property.Transition,
                    _preparedStart,
                    _preparedTarget,
                    _motion.Apply(progress)
                );
                next = Publish<T>(property.Transition, _preparedPresented);
            }
            var changed = !EqualityComparer<T>.Default.Equals(Presented, next);
            Presented = next;
            if (progress >= 1d)
            {
                IsActive = false;
                _reason = "completed";
            }
            return changed;
        }

        public void Snap(string reason)
        {
            Presented = _target;
            _preparedPresented = Prepare(property.Transition, _target);
            IsActive = false;
            _reason = reason;
        }

        public void Acknowledge() => _acknowledged = true;

        public void AppendDump(StringBuilder output, long elementId, TimeSpan timestamp)
        {
            var elapsed = IsActive ? Math.Max(0d, (timestamp - _started).TotalMilliseconds) : 0d;
            var progress =
                IsActive && _motion.DurationMilliseconds != 0
                    ? Math.Clamp(elapsed / _motion.DurationMilliseconds, 0d, 1d)
                    : 1d;
            var diagnosticPolicy = IsActive ? _motion : _policy;
            var diagnosticSource = IsActive ? _capturedPolicySource : _policySource;
            output
                .Append("  track element=")
                .Append(elementId.ToString(CultureInfo.InvariantCulture))
                .Append(" property=")
                .Append(DiagnosticText.Quote(property.Name))
                .Append(" target=")
                .Append(FormatValue(_target))
                .Append(" presented=")
                .Append(FormatValue(Presented))
                .Append(" policy=")
                .Append(
                    diagnosticPolicy is { } policy
                        ? policy.DurationMilliseconds.ToString(CultureInfo.InvariantCulture)
                            + "ms/"
                            + policy.Easing
                        : "none"
                )
                .Append(" policySource=")
                .Append(diagnosticSource ?? "none")
                .Append(" start=")
                .Append(_started.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture))
                .Append(" elapsed=")
                .Append(elapsed.ToString("R", CultureInfo.InvariantCulture))
                .Append(" progress=")
                .Append(progress.ToString("R", CultureInfo.InvariantCulture))
                .Append(" generation=")
                .Append(TargetVersion.ToString(CultureInfo.InvariantCulture))
                .Append(" state=")
                .Append(IsActive ? "active" : "idle")
                .Append(" reason=")
                .Append(_reason)
                .Append('\n');
        }

        private static string FormatValue(T value) =>
            value switch
            {
                float number => number.ToString("R", CultureInfo.InvariantCulture),
                Color color => color.ToString(),
                Brush { Color: { } color } => "solid(" + color + ")",
                Brush => "gradient",
                _ => "unsupported",
            };
    }

    private static bool CanInterpolate<T>(TransitionKind kind, T from, T to) =>
        kind switch
        {
            TransitionKind.Opacity => from is float left
                && to is float right
                && float.IsFinite(left)
                && float.IsFinite(right)
                && left is >= 0 and <= 1
                && right is >= 0 and <= 1,
            TransitionKind.Color => from is Color && to is Color,
            TransitionKind.Brush => from is Brush { Color: not null }
                && to is Brush { Color: not null },
            _ => false,
        };

    internal static void ValidateTarget<T>(TransitionKind kind, T target)
    {
        if (
            kind == TransitionKind.Opacity
            && target is float opacity
            && (!float.IsFinite(opacity) || opacity is < 0 or > 1)
        )
            throw new ArgumentOutOfRangeException(
                nameof(target),
                "Opacity motion endpoints must be finite and between zero and one."
            );
    }

    private static object Prepare<T>(TransitionKind kind, T value) =>
        kind switch
        {
            TransitionKind.Opacity => (double)(float)(object)value!,
            TransitionKind.Color => PreparedColor.From((Color)(object)value!),
            TransitionKind.Brush => PreparedColor.From(((Brush)(object)value!).Color ?? default),
            _ => value!,
        };

    private static object InterpolatePrepared(
        TransitionKind kind,
        object from,
        object to,
        double progress
    ) =>
        kind switch
        {
            TransitionKind.Opacity => Lerp((double)from, (double)to, progress),
            TransitionKind.Color or TransitionKind.Brush => PreparedColor.Lerp(
                (PreparedColor)from,
                (PreparedColor)to,
                progress
            ),
            _ => to,
        };

    private static T Publish<T>(TransitionKind kind, object prepared) =>
        kind switch
        {
            TransitionKind.Opacity => (T)(object)(float)(double)prepared,
            TransitionKind.Color => (T)(object)((PreparedColor)prepared).Publish(),
            TransitionKind.Brush => (T)(object)Brush.Solid(((PreparedColor)prepared).Publish()),
            _ => (T)prepared,
        };

    internal static Color InterpolateColor(Color from, Color to, double progress)
    {
        if (progress <= 0d)
            return from;
        if (progress >= 1d)
            return to;
        var fromA = from.A / 255d;
        var toA = to.A / 255d;
        var alpha = Lerp(fromA, toA, progress);
        byte Channel(byte left, byte right)
        {
            if (alpha <= 0d)
                return 0;
            var premultiplied = Lerp(
                ToLinear(left / 255d) * fromA,
                ToLinear(right / 255d) * toA,
                progress
            );
            return Quantize(ToSrgb(premultiplied / alpha));
        }
        return new(
            Channel(from.R, to.R),
            Channel(from.G, to.G),
            Channel(from.B, to.B),
            Quantize(alpha)
        );
    }

    private static double Lerp(double from, double to, double progress) =>
        from + (to - from) * progress;

    private static double ToLinear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double ToSrgb(double value) =>
        value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1d / 2.4) - 0.055;

    private static byte Quantize(double value) =>
        (byte)Math.Clamp((int)Math.Round(value * 255d, MidpointRounding.AwayFromZero), 0, 255);

    private readonly record struct PreparedColor(
        double R,
        double G,
        double B,
        double A,
        Color Exact,
        bool IsExact
    )
    {
        internal static PreparedColor From(Color color)
        {
            var alpha = color.A / 255d;
            return new(
                ToLinear(color.R / 255d) * alpha,
                ToLinear(color.G / 255d) * alpha,
                ToLinear(color.B / 255d) * alpha,
                alpha,
                color,
                true
            );
        }

        internal static PreparedColor Lerp(PreparedColor from, PreparedColor to, double progress)
        {
            if (progress <= 0d)
                return from;
            if (progress >= 1d)
                return to;
            return new(
                MotionTimeline.Lerp(from.R, to.R, progress),
                MotionTimeline.Lerp(from.G, to.G, progress),
                MotionTimeline.Lerp(from.B, to.B, progress),
                MotionTimeline.Lerp(from.A, to.A, progress),
                to.Exact,
                false
            );
        }

        internal Color Publish()
        {
            if (IsExact)
                return Exact;
            if (A <= 0d)
                return new(0, 0, 0, 0);
            return new(
                Quantize(ToSrgb(R / A)),
                Quantize(ToSrgb(G / A)),
                Quantize(ToSrgb(B / A)),
                Quantize(A)
            );
        }
    }
}
