namespace CompanionFixture;

using System;
using Lucent.Core;

internal sealed partial class Failing
{
    [State(Initializer = nameof(Fail))]
    public partial int CompanionCount { get; set; }

    private static int Fail(ComponentContext context)
    {
        Harness.Record("failing-state");
        context.OnDispose(() => Harness.FailedInitializerCleanups++);
        throw new InvalidOperationException("initializer failed");
    }
}
