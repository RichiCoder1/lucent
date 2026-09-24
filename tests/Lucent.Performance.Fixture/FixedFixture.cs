using System.Globalization;
using Lucent.Core;

namespace Lucent.Performance.Fixture;

/// <summary>The versioned, offline native workload shared by the app and its contract tests.</summary>
public static class FixedFixture
{
    public const string Version = "fixed-native-v1";
    public const int WindowWidth = 800;
    public const int WindowHeight = 500;
    public const int RowCount = 100;
    public const float RowHeight = 30;
    public const float ListHeight = 60;
    public const string ListLabel = "Benchmark rows";

    public static readonly IReadOnlyList<string> ButtonLabels =
    [
        "Benchmark action 1",
        "Benchmark action 2",
        "Benchmark action 3",
        "Benchmark action 4",
    ];

    private static readonly int[] Rows = Enumerable.Range(1, RowCount).ToArray();

    public static string RowLabel(int number)
    {
        if (number < 1 || number > RowCount)
            throw new ArgumentOutOfRangeException(nameof(number));
        return "Benchmark row " + number.ToString(CultureInfo.InvariantCulture);
    }

    public static ComponentRecipe Create() =>
        Components.Column(
            [
                Components.Row(
                    [
                        Components.Button(ButtonLabels[0], static () => { }),
                        Components.Button(ButtonLabels[1], static () => { }),
                        Components.Button(ButtonLabels[2], static () => { }),
                        Components.Button(ButtonLabels[3], static () => { }),
                    ],
                    style: Style.Empty.Height(40)
                ),
                Components.VirtualizedList(
                    static () => Rows,
                    static number => number,
                    static current => Components.Text(RowLabel(current.Value)),
                    static () => RowHeight,
                    label: ListLabel,
                    style: Style.Empty.Width(WindowWidth).Height(ListHeight)
                ),
            ],
            style: Style.Empty.MainGrow(1).MainBasis(0)
        );
}
