using System.Windows.Input;

namespace MuxDeck.Desktop.ViewModels;

public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> reportError) : ICommand
{
    private bool executing;
    public bool CanExecute(object? parameter) => !executing && canExecute();
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        executing = true;
        NotifyCanExecuteChanged();
        try { await execute(); }
        // ICommand 由控件以 async void 调用，未预料异常也必须转成可观察的失败。
        catch (Exception ex) { reportError(ex); }
        finally { executing = false; NotifyCanExecuteChanged(); }
    }
    public event EventHandler? CanExecuteChanged;
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
