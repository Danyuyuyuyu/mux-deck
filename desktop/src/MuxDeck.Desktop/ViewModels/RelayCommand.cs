using System.Windows.Input;

namespace MuxDeck.Desktop.ViewModels;

public sealed class RelayCommand(Action execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
    // All skeleton commands remain enabled; asynchronous commands will own their busy state.
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
