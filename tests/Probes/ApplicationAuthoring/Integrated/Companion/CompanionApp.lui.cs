namespace IntegratedFixture;

using Lucent.Core;

public sealed partial class CompanionApp
{
    private readonly string organization = "companion";

    [State(Initializer = nameof(CreateCompanionCount))]
    public partial int CompanionCount { get; set; }

    public static int SetupCalls { get; private set; }
    public static int CleanupCalls { get; private set; }

    public string Organization => organization;

    private static int CreateCompanionCount(ComponentContext context) => 3;

    partial void Setup(ComponentContext context)
    {
        SetupCalls++;
        context.OnDispose(() => CleanupCalls++);
    }
}
