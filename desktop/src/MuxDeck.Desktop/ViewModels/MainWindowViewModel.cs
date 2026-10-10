using System.Collections.ObjectModel;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;

namespace MuxDeck.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ToolLocator tools;
    private int selectedPage;
    private string log = "桌面工作区已就绪。选择视频或检查本机工具环境。";

    public MainWindowViewModel(Workspace workspace)
    {
        WorkspaceRoot = workspace.Root;
        tools = new ToolLocator(workspace);
        Single = new SingleMuxViewModel(new SingleMuxService(workspace));
        ShowSingleCommand = new(() => SelectedPage = 0);
        ShowBatchCommand = new(() => SelectedPage = 1);
        ShowSubtitleCommand = new(() => SelectedPage = 2);
        ShowEnvironmentCommand = new(() => { SelectedPage = 3; RefreshEnvironment(); });
        RefreshEnvironmentCommand = new(RefreshEnvironment);
        RefreshEnvironment();
    }

    public string WorkspaceRoot { get; }
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
    public string PageTitle => SelectedPage switch { 1 => "批量队列", 2 => "字幕工具", 3 => "环境检测", _ => "单个封装" };
    public string PageDescription => SelectedPage switch
    {
        1 => "批量封装尚未实现。",
        2 => "字幕工具尚未实现。",
        3 => "检查工作目录内的工具，以及系统 PATH 中的可用组件。",
        _ => "一个视频 · 简繁 ASS · 字体子集化 · MKV 成品校验"
    };
    // 旧冒烟入口继续转发，实际单任务状态由独立 ViewModel 持有。
    public string VideoPath { get => Single.VideoPath; set => Single.VideoPath = value; }
    public string ScSubtitle { get => Single.ScSubtitle; set => Single.ScSubtitle = value; }
    public string TcSubtitle { get => Single.TcSubtitle; set => Single.TcSubtitle = value; }
    public string OutputDirectory { get => Single.OutputDirectory; set => Single.OutputDirectory = value; }
    public string Log { get => log; private set => Set(ref log, value); }
    public string EnvironmentSummary => $"已找到 {Tools.Count(t => t.IsAvailable)} / {Tools.Count} 个工具（仅检查路径）";
    public ObservableCollection<ToolStatus> Tools { get; } = [];
    public RelayCommand ShowSingleCommand { get; }
    public RelayCommand ShowBatchCommand { get; }
    public RelayCommand ShowSubtitleCommand { get; }
    public RelayCommand ShowEnvironmentCommand { get; }
    public RelayCommand RefreshEnvironmentCommand { get; }

    private void RefreshEnvironment()
    {
        Tools.Clear();
        foreach (var tool in tools.Inspect()) Tools.Add(tool);
        Raise(nameof(EnvironmentSummary));
        AppendLog(EnvironmentSummary);
    }

    public void AppendLog(string message)
    {
        var lines = (Log + $"{Environment.NewLine}[{DateTime.Now:HH:mm:ss}] {message}").Split(Environment.NewLine);
        Log = string.Join(Environment.NewLine, lines.TakeLast(80));
    }
}
