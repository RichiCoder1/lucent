namespace CompanionFixture;

using Lucent.Core;

public sealed partial class Counter
{
    private readonly int ordinaryField = Harness.RecordValue("ordinary-field", 7);

    [State(Initializer = nameof(CreateCompanionCount))]
    public partial int CompanionCount { get; set; }

    private static int CreateCompanionCount(ComponentContext context)
    {
        Harness.Record("companion-state");
        return 3;
    }

    partial void Setup(ComponentContext context)
    {
        Harness.Record("setup");
        Harness.Counters.Add(this);
        Harness.SetupCalls++;
        context.OnDispose(() => Harness.CounterCleanups++);
    }

    public void IncrementFromCompanion()
    {
        LuiCount += CompanionCount;
    }

    public int Snapshot => ordinaryField + CompanionCount + LuiCount + LuiSecond;
}
