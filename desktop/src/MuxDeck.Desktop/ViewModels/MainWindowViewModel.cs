using System.Collections.ObjectModel;
using MuxDeck.Core;

namespace MuxDeck.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private int selectedPage;
    private string log = "桌面工作区已就绪。选择视频或检查本机工具环境。";

    public MainWindowViewModel(Workspace workspace, StoragePaths storage, EnvironmentViewModel environment,
        SingleMuxViewModel single)
    {
        WorkspaceRoot = workspace.Root;
        Storage = storage;
        Environment = environment;
        Single = single;
        Environment.SetWorkGuard(() => !Single.CanEdit || Single.IsProbing || Single.HasActiveToolProbes);
        Environment.OperationChanged += () =>
        {
            Single.IsComponentInstalling = Environment.IsInstalling;
            Single.IsComponentUninstalling = Environment.IsUninstalling;
        };
        Single.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Single.IsRunning) or nameof(Single.IsProbing) or nameof(Single.HasActiveToolProbes) or nameof(Single.CanEdit))
                Environment.NotifyWorkChanged();
        };
        Environment.StorageChanged += next =>
        {
            Storage = next;
            Raise(nameof(Storage));
            if (!Single.IsRunning)
                Single.ConfigureTools(workspace with { ToolsDirectoryOverride = next.ToolsDirectory, StorageOverride = next });
        };
        ShowSingleCommand = new(() => SelectedPage = 0);
        ShowBatchCommand = new(() => SelectedPage = 1);
        ShowSubtitleCommand = new(() => SelectedPage = 2);
        ShowEnvironmentCommand = new(() => { SelectedPage = 3; RefreshEnvironment(); });
        RefreshEnvironmentCommand = new(RefreshEnvironment);
    }

    public string WorkspaceRoot { get; }
    public StoragePaths Storage { get; private set; }
    public EnvironmentViewModel Environment { get; }
    public SingleMuxViewModel Single { get; }
    public int SelectedPage
    {
        get => selectedPage;
        set
        {
            if (!Set(ref selectedPage, value)) return;
            Raise(nameof(IsSingle)); Raise(nameof(IsBatch)); Raise(nameof(IsSubtitle)); Raise(nameof(IsEnvironment));
            Raise(nameof(PageTitle)); Raise(nameof(PageDescription));
        }
    }
    public bool IsSingle => SelectedPage == 0;
    public bool IsBatch => SelectedPage == 1;
    public bool IsSubtitle => SelectedPage == 2;
    public bool IsEnvironment => SelectedPage == 3;
    public string PageTitle => SelectedPage switch { 1 => "批量队列", 2 => "字幕工具", 3 => "环境与组件", _ => "单个封装" };
    public string PageDescription => SelectedPage switch
    {
        1 => "批量封装尚未实现。",
        2 => "字幕工具尚未实现。",
        3 => "检查并管理 Mux Deck 使用的外部组件。",
        _ => "一个视频 · 字幕与字体 · MKV 成品"
    };
    // 保留已有转发属性；新视图直接绑定独立的单任务状态。
    public string VideoPath { get => Single.VideoPath; set => Single.VideoPath = value; }
    public string ScSubtitle { get => Single.ScSubtitle; set => Single.ScSubtitle = value; }
    public string TcSubtitle { get => Single.TcSubtitle; set => Single.TcSubtitle = value; }
    public string OutputDirectory { get => Single.OutputDirectory; set => Single.OutputDirectory = value; }
    public string Log { get => log; private set => Set(ref log, value); }
    public string EnvironmentSummary => $"{Environment.CoreSummary} · {Environment.ExtensionSummary}";
    // 保留旧测试和任务摘要使用的工具集合；正式页面使用 Environment 的组件分组。
    public ObservableCollection<ToolStatus> Tools => Environment.Tools;
    public RelayCommand ShowSingleCommand { get; }
    public RelayCommand ShowBatchCommand { get; }
    public RelayCommand ShowSubtitleCommand { get; }
    public RelayCommand ShowEnvironmentCommand { get; }
    public RelayCommand RefreshEnvironmentCommand { get; }

    private void RefreshEnvironment() { Environment.Refresh(); Raise(nameof(EnvironmentSummary)); AppendLog(EnvironmentSummary); }

    public void AppendLog(string message)
    {
        var lines = (Log + $"{System.Environment.NewLine}[{DateTime.Now:HH:mm:ss}] {message}").Split(System.Environment.NewLine);
        Log = string.Join(System.Environment.NewLine, lines.TakeLast(80));
    }
}
