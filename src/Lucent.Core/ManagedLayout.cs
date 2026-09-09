namespace Lucent.Core;

internal readonly record struct LayoutItemSpec(
    int Index,
    float Width,
    float Height,
    float MinWidth,
    float MinHeight,
    float MaxWidth,
    float MaxHeight,
    bool AutoWidth,
    bool AutoHeight,
    float? Basis,
    float Grow,
    float Shrink,
    GridPlacement? Placement
);

internal readonly record struct LayoutAssignment(
    int Index,
    LayoutRect Bounds,
    bool WidthAssigned,
    bool HeightAssigned
);

internal static class ManagedLayout
{
    internal static LayoutAssignment[] ArrangeFlex(
        IReadOnlyList<LayoutItemSpec> items,
        LayoutAxis axis,
        float width,
        float height,
        float mainGap,
        float lineGap,
        bool wrap,
        LayoutAlignment mainAlignment,
        LayoutAlignment crossAlignment
    )
    {
        var mainLimit = axis == LayoutAxis.Row ? width : height;
        var crossLimit = axis == LayoutAxis.Row ? height : width;
        var pending = new List<List<FlexItem>> { new() };
        var occupied = 0f;
        foreach (var item in items)
        {
            var main = item.Basis ?? (axis == LayoutAxis.Row ? item.Width : item.Height);
            var cross = axis == LayoutAxis.Row ? item.Height : item.Width;
            var min = axis == LayoutAxis.Row ? item.MinWidth : item.MinHeight;
            var max = axis == LayoutAxis.Row ? item.MaxWidth : item.MaxHeight;
            var candidate = new FlexItem(item, Math.Clamp(main, min, max), cross, min, max);
            var line = pending[^1];
            var gap = line.Count == 0 ? 0 : mainGap;
            if (wrap && line.Count != 0 && occupied + gap + candidate.Main > mainLimit)
            {
                line = [];
                pending.Add(line);
                occupied = 0;
                gap = 0;
            }
            line.Add(candidate);
            occupied = checked(occupied + gap + candidate.Main);
        }

        var crossSizes = pending
            .Select(line =>
                !wrap ? crossLimit
                : line.Count == 0 ? 0
                : line.Max(item => item.Cross)
            )
            .ToArray();
        var crossCursor = 0f;
        var results = new List<LayoutAssignment>(items.Count);
        for (var lineIndex = 0; lineIndex < pending.Count; lineIndex++)
        {
            var line = pending[lineIndex];
            var gaps = mainGap * Math.Max(0, line.Count - 1);
            var used = line.Sum(item => item.Main) + gaps;
            if (used < mainLimit)
                Grow(line, mainLimit - used);
            else if (used > mainLimit)
                Shrink(line, used - mainLimit);
            used = line.Sum(item => item.Main) + gaps;
            var cursor = mainAlignment switch
            {
                LayoutAlignment.Center => Math.Max(0, (mainLimit - used) / 2),
                LayoutAlignment.End => Math.Max(0, mainLimit - used),
                _ => 0,
            };
            var lineCross = crossSizes[lineIndex];
            foreach (var item in line)
            {
                var autoCross = axis == LayoutAxis.Row ? item.Spec.AutoHeight : item.Spec.AutoWidth;
                var cross =
                    crossAlignment == LayoutAlignment.Stretch && autoCross ? lineCross
                    : autoCross ? Math.Min(item.Cross, lineCross)
                    : item.Cross;
                var crossOffset = crossAlignment switch
                {
                    LayoutAlignment.Center => (lineCross - cross) / 2,
                    LayoutAlignment.End => lineCross - cross,
                    _ => 0,
                };
                var bounds =
                    axis == LayoutAxis.Row
                        ? new LayoutRect(cursor, crossCursor + crossOffset, item.Main, cross)
                        : new LayoutRect(crossCursor + crossOffset, cursor, cross, item.Main);
                results.Add(
                    new(
                        item.Spec.Index,
                        bounds,
                        axis == LayoutAxis.Row
                            || crossAlignment == LayoutAlignment.Stretch && autoCross,
                        axis == LayoutAxis.Column
                            || crossAlignment == LayoutAlignment.Stretch && autoCross
                    )
                );
                cursor += item.Main + mainGap;
            }
            crossCursor += lineCross + lineGap;
        }
        return results.OrderBy(result => result.Index).ToArray();
    }

    internal static LayoutAssignment[] ArrangeGrid(
        IReadOnlyList<LayoutItemSpec> items,
        GridTracks columns,
        GridTracks rows,
        float width,
        float height,
        float columnGap,
        float rowGap,
        LayoutAlignment alignment
    )
    {
        foreach (var item in items)
        {
            if (item.Placement is not { } placement)
                throw new InvalidOperationException(
                    "Every child of a Grid container requires an explicit placement."
                );
            if (
                placement.Row < 0
                || placement.Column < 0
                || placement.RowSpan <= 0
                || placement.ColumnSpan <= 0
            )
                throw new InvalidOperationException(
                    "Grid placement indices must be nonnegative and spans must be positive."
                );
            if (
                placement.Column > columns.Count - placement.ColumnSpan
                || placement.Row > rows.Count - placement.RowSpan
            )
                throw new InvalidOperationException(
                    "Grid placement extends outside the explicitly declared tracks."
                );
        }

        var columnSizes = ResolveTracks(
            columns,
            width,
            columnGap,
            items
                .Select(item =>
                    (item.Placement!.Value.Column, item.Placement.Value.ColumnSpan, item.Width)
                )
                .ToArray()
        );
        var rowSizes = ResolveTracks(
            rows,
            height,
            rowGap,
            items
                .Select(item =>
                    (item.Placement!.Value.Row, item.Placement.Value.RowSpan, item.Height)
                )
                .ToArray()
        );
        var columnEdges = Edges(columnSizes, columnGap);
        var rowEdges = Edges(rowSizes, rowGap);
        var result = new LayoutAssignment[items.Count];
        foreach (var item in items)
        {
            var placement = item.Placement!.Value;
            var cellWidth =
                columnSizes.Skip(placement.Column).Take(placement.ColumnSpan).Sum()
                + columnGap * (placement.ColumnSpan - 1);
            var cellHeight =
                rowSizes.Skip(placement.Row).Take(placement.RowSpan).Sum()
                + rowGap * (placement.RowSpan - 1);
            var childWidth =
                !item.AutoWidth ? item.Width
                : alignment == LayoutAlignment.Stretch && item.MaxWidth >= cellWidth ? cellWidth
                : Math.Min(item.Width, cellWidth);
            var childHeight =
                !item.AutoHeight ? item.Height
                : alignment == LayoutAlignment.Stretch && item.MaxHeight >= cellHeight ? cellHeight
                : Math.Min(item.Height, cellHeight);
            var xOffset = alignment switch
            {
                LayoutAlignment.Center => (cellWidth - childWidth) / 2,
                LayoutAlignment.End => cellWidth - childWidth,
                _ => 0,
            };
            var yOffset = alignment switch
            {
                LayoutAlignment.Center => (cellHeight - childHeight) / 2,
                LayoutAlignment.End => cellHeight - childHeight,
                _ => 0,
            };
            result[item.Index] = new(
                item.Index,
                new(
                    columnEdges[placement.Column] + xOffset,
                    rowEdges[placement.Row] + yOffset,
                    childWidth,
                    childHeight
                ),
                true,
                true
            );
        }
        return result;
    }

    private static float[] ResolveTracks(
        GridTracks tracks,
        float available,
        float gap,
        (int Start, int Span, float Contribution)[] contributions
    )
    {
        var sizes = tracks
            .Select(track => track.Kind == GridTrackKind.Fixed ? track.Maximum : track.Minimum)
            .ToArray();
        foreach (var contribution in contributions.OrderBy(value => value.Span))
        {
            var current =
                sizes.Skip(contribution.Start).Take(contribution.Span).Sum()
                + gap * (contribution.Span - 1);
            var deficit = Math.Max(0, contribution.Contribution - current);
            if (deficit == 0)
                continue;
            for (var pass = 0; pass < contribution.Span && deficit > .0001f; pass++)
            {
                var eligible = Enumerable
                    .Range(contribution.Start, contribution.Span)
                    .Where(index =>
                        tracks[index].Kind != GridTrackKind.Fixed
                        && tracks[index].FractionWeight == 0
                        && sizes[index] < tracks[index].Maximum
                    )
                    .ToArray();
                if (eligible.Length == 0)
                    break;
                var consumed = 0f;
                foreach (var index in eligible)
                {
                    var next = Math.Min(
                        tracks[index].Maximum,
                        sizes[index] + deficit / eligible.Length
                    );
                    consumed += next - sizes[index];
                    sizes[index] = next;
                }
                if (consumed <= .0001f)
                    break;
                deficit -= consumed;
            }
        }
        var remaining = Math.Max(0, available - sizes.Sum() - gap * Math.Max(0, tracks.Count - 1));
        for (var pass = 0; pass < tracks.Count && remaining > .0001f; pass++)
        {
            var eligible = Enumerable
                .Range(0, tracks.Count)
                .Where(index =>
                    tracks[index].FractionWeight > 0 && sizes[index] < tracks[index].Maximum
                )
                .ToArray();
            if (eligible.Length == 0)
                break;
            var weight = eligible.Sum(index => tracks[index].FractionWeight);
            var consumed = 0f;
            foreach (var index in eligible)
            {
                var next = Math.Min(
                    tracks[index].Maximum,
                    sizes[index] + remaining * tracks[index].FractionWeight / weight
                );
                consumed += next - sizes[index];
                sizes[index] = next;
            }
            if (consumed <= .0001f)
                break;
            remaining -= consumed;
        }
        return sizes;
    }

    private static float[] Edges(float[] sizes, float gap)
    {
        var edges = new float[sizes.Length];
        var cursor = 0f;
        for (var index = 0; index < sizes.Length; index++)
        {
            edges[index] = cursor;
            cursor += sizes[index] + gap;
        }
        return edges;
    }

    private static void Grow(List<FlexItem> items, float remaining)
    {
        for (var pass = 0; pass < items.Count && remaining > .0001f; pass++)
        {
            var eligible = items
                .Where(item => item.Spec.Grow > 0 && item.Main < item.Max)
                .ToArray();
            if (eligible.Length == 0)
                break;
            var total = eligible.Sum(item => item.Spec.Grow);
            var used = 0f;
            foreach (var item in eligible)
            {
                var next = Math.Min(item.Max, item.Main + remaining * item.Spec.Grow / total);
                used += next - item.Main;
                item.Main = next;
            }
            if (used <= .0001f)
                break;
            remaining -= used;
        }
    }

    private static void Shrink(List<FlexItem> items, float excess)
    {
        for (var pass = 0; pass < items.Count && excess > .0001f; pass++)
        {
            var eligible = items
                .Where(item => item.Spec.Shrink > 0 && item.Main > item.Min)
                .ToArray();
            if (eligible.Length == 0)
                break;
            var total = eligible.Sum(item => item.Spec.Shrink * Math.Max(item.Main, .0001f));
            var removed = 0f;
            foreach (var item in eligible)
            {
                var weight = item.Spec.Shrink * Math.Max(item.Main, .0001f);
                var next = Math.Max(item.Min, item.Main - excess * weight / total);
                removed += item.Main - next;
                item.Main = next;
            }
            if (removed <= .0001f)
                break;
            excess -= removed;
        }
    }

    private sealed class FlexItem(
        LayoutItemSpec spec,
        float main,
        float cross,
        float min,
        float max
    )
    {
        internal LayoutItemSpec Spec { get; } = spec;
        internal float Main { get; set; } = main;
        internal float Cross { get; } = cross;
        internal float Min { get; } = min;
        internal float Max { get; } = max;
    }
}
