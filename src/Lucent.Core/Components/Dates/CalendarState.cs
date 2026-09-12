namespace Lucent.Core;

internal sealed class CalendarState
{
    private readonly DatePickerOptions _options;
    private readonly Signal<DateOnly> _focused;
    private readonly Signal<DateOnly> _month;
    private readonly Signal<bool> _focusVisible;
    private readonly DateOnly _today;

    internal CalendarState(
        ReactiveScope scope,
        DatePickerOptions options,
        DateOnly? selected,
        string name
    )
    {
        _options = options;
        _today = options.Today();
        var initial = Clamp(selected ?? _today);
        _focused = scope.Signal(initial, name + ".focused");
        _month = scope.Signal(new DateOnly(initial.Year, initial.Month, 1), name + ".month");
        _focusVisible = scope.Signal(false, name + ".focus-visible");
    }

    internal DateOnly Focused => _focused.Value;
    internal DateOnly DisplayedMonth => _month.Value;
    internal bool FocusVisible
    {
        get => _focusVisible.Value;
        set => _focusVisible.Value = value;
    }

    internal IReadOnlyList<CalendarDay> Days(DateOnly? selected)
    {
        var result = new CalendarDay[42];
        for (var index = 0; index < result.Length; index++)
            result[index] = Day(index, selected);
        return result;
    }

    internal CalendarDay Day(int index, DateOnly? selected)
    {
        if ((uint)index >= 42u)
            throw new ArgumentOutOfRangeException(nameof(index));
        var firstDay = _options.Culture.DateTimeFormat.FirstDayOfWeek;
        var leading = (7 + (int)_month.Value.DayOfWeek - (int)firstDay) % 7;
        var dayNumber = (long)_month.Value.DayNumber + index - leading;
        if (dayNumber < 0 || dayNumber > DateOnly.MaxValue.DayNumber)
            return new(null, "Unavailable date", false, false, false, false, false);
        var date = DateOnly.FromDayNumber((int)dayNumber);
        return new(
            date,
            date.ToString("D", _options.Culture),
            date.Month == _month.Value.Month,
            date == _today,
            date == selected,
            date == _focused.Value,
            _options.InRange(date)
        );
    }

    internal bool MoveDays(int count)
    {
        var target = Math.Clamp(
            (long)_focused.Value.DayNumber + count,
            DateOnly.MinValue.DayNumber,
            DateOnly.MaxValue.DayNumber
        );
        return MoveTo(DateOnly.FromDayNumber((int)target));
    }

    internal bool MoveWeekEdge(bool end)
    {
        var firstDay = _options.Culture.DateTimeFormat.FirstDayOfWeek;
        var offset = (7 + (int)_focused.Value.DayOfWeek - (int)firstDay) % 7;
        return MoveDays(end ? 6 - offset : -offset);
    }

    internal bool MoveMonth(int count)
    {
        var monthIndex = ((long)_month.Value.Year - 1) * 12 + _month.Value.Month - 1;
        var targetIndex = Math.Clamp(monthIndex + count, 0, (long)DateOnly.MaxValue.Year * 12 - 1);
        var targetMonth = new DateOnly((int)(targetIndex / 12) + 1, (int)(targetIndex % 12) + 1, 1);
        var day = Math.Min(
            _focused.Value.Day,
            DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month)
        );
        return MoveTo(new(targetMonth.Year, targetMonth.Month, day));
    }

    internal bool MoveTo(DateOnly value)
    {
        var target = Clamp(value);
        if (target == _focused.Value)
            return false;
        _focused.Value = target;
        _month.Value = new(target.Year, target.Month, 1);
        return true;
    }

    private DateOnly Clamp(DateOnly value)
    {
        if (_options.Minimum is { } minimum && value < minimum)
            return minimum;
        if (_options.Maximum is { } maximum && value > maximum)
            return maximum;
        return value;
    }
}

internal readonly record struct CalendarDay(
    DateOnly? Date,
    string AccessibleName,
    bool InDisplayedMonth,
    bool IsToday,
    bool IsSelected,
    bool IsFocused,
    bool IsEnabled
);
