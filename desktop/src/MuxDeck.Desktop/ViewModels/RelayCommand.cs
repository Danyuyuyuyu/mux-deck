using System.Windows.Input;

namespace MuxDeck.Desktop.ViewModels;

public sealed class RelayCommand(Action execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
    // 导航命令始终可用；异步任务命令独立管理可执行状态。
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
