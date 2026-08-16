using Lucent.Examples.Workbench;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class CommandTests
{
    [TestMethod]
    public void Delegate_command_uses_one_predicate_and_notifies()
    {
        var executeCount = 0;
        var canExecute = false;
        var command = new DelegateCommand(() => executeCount++, () => canExecute);
        var notifications = 0;
        command.CanExecuteChanged += (_, _) => notifications++;

        Assert.IsFalse(command.CanExecute(null));
        canExecute = true;
        command.NotifyCanExecuteChanged();
        command.Execute(null);

        Assert.AreEqual(1, notifications);
        Assert.AreEqual(1, executeCount);
        Assert.IsTrue(command.CanExecute(null));
    }

    [TestMethod]
    public void Delegate_command_preserves_identity_and_does_not_copy_predicates()
    {
        var enabled = false;
        var command = new DelegateCommand(() => { }, () => enabled);
        System.Windows.Input.ICommand presentation = command;

        Assert.AreSame(command, presentation);
        Assert.IsFalse(presentation.CanExecute(null));
        enabled = true;
        command.NotifyCanExecuteChanged();
        Assert.IsTrue(presentation.CanExecute(null));
    }
}
