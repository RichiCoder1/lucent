using Lucent.Platform.Windows.TestHost;

namespace Lucent.Platform.Windows.TestHostEntry;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) =>
        args switch
        {
            ["--editor-session-proof"] => EditorSessionFixture.Run(),
            ["--listener-proof"] => ListenerProof.Run(),
            ["--uia-fixture"] => UiaFixture.Run(),
            ["--virtualization-fixture"] => VirtualizationFixture.Run(),
            ["--autosized-text-field-fixture"] => AutoSizedTextFieldFixture.Run(),
            ["--lifecycle-fixture"] => LifecycleFixture.Run(LifecycleFixtureMode.Normal),
            ["--lifecycle-startup-failure"] => LifecycleFixture.Run(
                LifecycleFixtureMode.StartupFailure
            ),
            ["--lifecycle-cleanup-failure"] => LifecycleFixture.Run(
                LifecycleFixtureMode.CleanupFailure
            ),
            _ => 2,
        };
}
