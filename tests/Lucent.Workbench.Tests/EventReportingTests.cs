using Avalonia.Controls;
using Avalonia.Interactivity;
using Lucent.Examples.Workbench;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class EventReportingTests
{
    [TestMethod]
    public async Task Generated_event_reports_authored_failure_exactly_once()
    {
        await HeadlessTestHarness.RunWindowAsync(window =>
        {
            var reports = new List<Exception>();
            using var component = new ThrowingEventComponent(__lucent_reportUnhandled: reports.Add);
            var button = component.MountRoot();
            window.Content = button;
            window.UpdateLayout();

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.HasCount(1, reports);
            Assert.AreEqual("event failure", reports[0].Message);
            return Task.CompletedTask;
        });
    }
}
