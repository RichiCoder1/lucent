using System.Windows.Input;

namespace Lucent.Examples.Workbench;

internal sealed class DelegateCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    private readonly Func<bool> _canExecute = canExecute ?? (() => true);

    public bool CanExecute(object? parameter) => _canExecute();
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged;
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
