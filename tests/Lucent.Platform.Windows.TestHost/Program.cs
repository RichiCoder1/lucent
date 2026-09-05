using Lucent.Platform.Windows.TestHost;

namespace Lucent.Platform.Windows.TestHostEntry;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) =>
        args switch
        {
            ["--listener-proof"] => ListenerProof.Run(),
            ["--uia-fixture"] => UiaFixture.Run(),
            ["--virtualization-fixture"] => VirtualizationFixture.Run(),
            ["--autosized-text-field-fixture"] => AutoSizedTextFieldFixture.Run(),
            _ => 2,
        };
}
