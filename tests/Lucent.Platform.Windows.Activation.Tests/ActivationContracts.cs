using Lucent.Platform.Windows.Activation;

namespace Lucent.Platform.Windows.Activation.Tests;

[TestClass]
public sealed class ActivationContracts
{
    private static readonly WindowsActivationOptions Options = new(
        "Lucent.Test.Stable",
        "lucent-test",
        "navigation"
    );

    [TestMethod]
    public void RawEscapedRouteIsPreservedAndRedirectRemainsUntrusted()
    {
        const string raw = "lucent-test://navigation/items/%2e%2e/admin?q=a%2Fb";
        Assert.IsTrue(
            WindowsActivationEnvelope.TryProtocol(
                raw,
                Options,
                ActivationDelivery.Redirected,
                out var envelope
            )
        );
        Assert.IsNotNull(envelope);
        Assert.AreEqual(raw, envelope.RawUri);
        Assert.AreEqual("/items/%2e%2e/admin?q=a%2Fb", envelope.EscapedPathAndQuery);
        Assert.AreEqual(ActivationProvenance.UntrustedExternal, envelope.Provenance);
    }

    [TestMethod]
    public void AuthorityAndOriginalSyntaxAreCheckedBeforeCoreParsing()
    {
        string[] rejected =
        [
            "LUCENT-TEST://navigation/path",
            "lucent-test:navigation/path",
            "lucent-test://other/path",
            "lucent-test://navigation.evil/path",
            "lucent-test://user@navigation/path",
            "lucent-test://navigation:443/path",
            "lucent-test://navigation",
            "lucent-test://navigation/path#fragment",
            "lucent-test://navigation/path\\evil",
            "lucent-test://navigation/path" + new string('x', 4096),
            "lucent-test://navigation/\ud800",
        ];
        foreach (var raw in rejected)
            Assert.IsFalse(
                WindowsActivationEnvelope.TryProtocol(raw, Options, ActivationDelivery.Cold, out _),
                raw.Length > 100 ? "oversized URI" : raw
            );
        Assert.IsTrue(
            WindowsActivationEnvelope.TryProtocol(
                "lucent-test://navigation/a/../b?x=one+two",
                Options,
                ActivationDelivery.Cold,
                out var valid
            )
        );
        Assert.AreEqual("/a/../b?x=one+two", valid!.EscapedPathAndQuery);
    }

    [TestMethod]
    public void StartupInboxRetainsLatestProtocolAndClosesWithoutLateOwnerCallbacks()
    {
        using var inbox = new ActivationInbox();
        var received = new List<ActivationEnvelope>();
        var pending = new List<Action>();
        inbox.Offer(WindowsActivationEnvelope.Launch(ActivationDelivery.Cold));
        Assert.IsTrue(
            WindowsActivationEnvelope.TryProtocol(
                "lucent-test://navigation/first",
                Options,
                ActivationDelivery.Redirected,
                out var first
            )
        );
        Assert.IsTrue(
            WindowsActivationEnvelope.TryProtocol(
                "lucent-test://navigation/second",
                Options,
                ActivationDelivery.Redirected,
                out var second
            )
        );
        inbox.Offer(first!);
        inbox.Offer(second!);
        Assert.AreEqual("/second", inbox.TakeStartup()!.EscapedPathAndQuery);
        inbox.Attach(pending.Add, received.Add);
        inbox.Offer(first!);
        inbox.Offer(second!);
        Assert.HasCount(1, pending);
        pending[0]();
        Assert.HasCount(2, received);
        Assert.AreEqual(ActivationKind.Launch, received[0].Kind);
        Assert.AreEqual("/second", received[1].EscapedPathAndQuery);
        inbox.BeginClose();
        Assert.IsFalse(inbox.Offer(first!));
        inbox.CancelClose();
        Assert.IsTrue(inbox.Offer(first!));
        inbox.Dispose();
        pending[1]();
        Assert.HasCount(2, received);
    }

    [TestMethod]
    public void InvalidDeliveryDoesNotDisplaceLatestValidProtocol()
    {
        using var inbox = new ActivationInbox();
        Assert.IsTrue(
            WindowsActivationEnvelope.TryProtocol(
                "lucent-test://navigation/first",
                Options,
                ActivationDelivery.Cold,
                out var valid
            )
        );
        inbox.Offer(valid!);
        inbox.Offer(
            WindowsActivationEnvelope.Rejected(
                ActivationDelivery.Redirected,
                ActivationRejection.InvalidProtocol
            )
        );
        Assert.AreEqual("/first", inbox.TakeStartup()!.EscapedPathAndQuery);
        var pending = new List<Action>();
        var received = new List<ActivationEnvelope>();
        inbox.Attach(pending.Add, received.Add);
        pending.Single()();
        Assert.AreEqual(ActivationRejection.InvalidProtocol, received.Single().Rejection);
    }

    [TestMethod]
    public void ExtractedOlderProtocolCannotCompleteWaitForNewerQueuedDelivery()
    {
        using var inbox = new ActivationInbox();
        var pending = new List<Action>();
        Task? waitForNewer = null;
        var olderDelivery = 0;
        var newerDelivery = 0;
        Assert.IsTrue(
            WindowsActivationEnvelope.TryProtocol(
                "lucent-test://navigation/first",
                Options,
                ActivationDelivery.Redirected,
                out var first
            )
        );
        Assert.IsTrue(
            WindowsActivationEnvelope.TryProtocol(
                "lucent-test://navigation/second",
                Options,
                ActivationDelivery.Redirected,
                out var second
            )
        );
        inbox.Attach(
            pending.Add,
            envelope =>
            {
                if (envelope.EscapedPathAndQuery == "/first")
                {
                    olderDelivery++;
                    inbox.Offer(second!);
                    waitForNewer = inbox.WaitForProtocolDeliveryAsync(inbox.ProtocolVersion);
                    throw new ExpectedStopAfterOlderDelivery();
                }
                newerDelivery++;
            }
        );
        inbox.Offer(first!);
        Assert.ThrowsExactly<ExpectedStopAfterOlderDelivery>(pending.Single());
        Assert.AreEqual(1, olderDelivery);
        Assert.AreEqual(0, newerDelivery);
        Assert.IsNotNull(waitForNewer);
        Assert.IsFalse(waitForNewer.IsCompleted);
        pending.Single()();
        Assert.AreEqual(1, newerDelivery);
        Assert.IsTrue(waitForNewer.IsCompletedSuccessfully);
    }

    private sealed class ExpectedStopAfterOlderDelivery : Exception { }
}
