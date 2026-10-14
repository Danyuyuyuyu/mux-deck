using System.Collections.ObjectModel;
using MuxDeck.Contracts;

namespace MuxDeck.Desktop.ViewModels;

public sealed class EnvironmentComponentViewModel : ObservableObject
{
    private ComponentInspection inspection;
    private ComponentProgress install = new(ComponentPhase.Idle, "");
    private string error = "", logPath = "";
    private bool detailsOpen;
    private bool removing, removalError;
    private string removalMessage = "";
    private ComponentUninstallPlan? componentPlan, dependencyPlan, confirmationPlan, lastRemovalPlan;
    private Func<bool> canOperate = () => false;
    private readonly Func<string, string, bool> isInside;

    public EnvironmentComponentViewModel(string name, string description, string category, IReadOnlyList<EnvironmentToolStatus> parts,
        Func<string, string, bool> isInside)
        : this(new EnvironmentComponentDefinition(name, name, description, category == "核心组件", parts.Select(p => p.Name).ToArray(), ""), parts, isInside) { }

    public EnvironmentComponentViewModel(EnvironmentComponentDefinition definition, IReadOnlyList<EnvironmentToolStatus> parts,
        Func<string, string, bool> isInside)
    {
        Definition = definition;
        this.isInside = isInside;
        Parts = new(parts);
        inspection = new(Id, parts.All(p => p.Path is null) ? ComponentState.Missing : ComponentState.Found, []);
        ToggleDetailsCommand = new(() => { detailsOpen = !detailsOpen; Notify(); });
        GuideCommand = new(() => { detailsOpen = !detailsOpen; Notify(); });
    }

    public EnvironmentComponentDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Category => Definition.IsCore ? "核心组件" : "扩展组件";
    public ObservableCollection<EnvironmentToolStatus> Parts { get; }
    public ComponentState State => inspection.State;
    public bool HasParts => Parts.Count > 1;
    public bool IsAvailable => State == ComponentState.Available;
    public bool IsPartiallyAvailable => State == ComponentState.Partial;
    public bool IsMissing => State == ComponentState.Missing;
    public bool IsInstalling => install.Phase is ComponentPhase.Preparing or ComponentPhase.Downloading or ComponentPhase.Installing or ComponentPhase.Verifying;
    public bool HasInstallError => install.Phase == ComponentPhase.Failed;
    public bool IsUninstalling => removing;
    public bool IsOperating => IsInstalling || IsUninstalling;
    public bool HasRemovalError => removalError;
    public bool HasOperationError => HasInstallError || HasRemovalError;
    public bool IsHealthy => IsAvailable && !HasOperationError && !IsOperating;
    public bool IsErrorStatus => !IsOperating && (HasOperationError || State == ComponentState.Missing);
    public bool IsWarningStatus => !IsOperating && !IsErrorStatus && State is ComponentState.Found or ComponentState.Partial or ComponentState.Invalid or ComponentState.DependencyMissing;
    public bool IsNeutralStatus => !IsHealthy && !IsErrorStatus && !IsWarningStatus;
    public string StatusText => IsUninstalling ? "正在卸载" : HasRemovalError ? "卸载失败" : IsInstalling ? install.Phase switch
    {
        ComponentPhase.Preparing => "准备安装",
        ComponentPhase.Downloading => "下载中",
        ComponentPhase.Installing => "正在安装",
        _ => "验证中"
    } : HasInstallError ? "安装失败" : State switch
    {
        ComponentState.Available => "可用", ComponentState.Missing => "未安装",
        ComponentState.Partial => "不完整", ComponentState.Invalid => "异常",
        ComponentState.DependencyMissing => "缺少依赖", ComponentState.Checking => "检测中", _ => "待验证"
    };
    public string SourceText => string.Join(" / ", Parts.Where(x => x.Path is not null).Select(x => x.Source).Distinct());
    public string VersionText => inspection.Version ?? "待验证";
    public bool HasMetadata => Parts.Any(x => x.Path is not null);
    public string MetadataText => $"版本  {VersionText}   ·   来源  {SourceText}";
    public string FullPath => Parts.FirstOrDefault(x => x.Path is not null)?.Path ?? "";
    public string PathSummary => FullPath.Length <= 68 ? FullPath : $"{FullPath[..24]}…\\{Path.GetFileName(FullPath)}";
    public string MissingText => inspection.Reason ?? (IsMissing ? "未检测到可执行文件。" : "");
    public bool HasReason => MissingText.Length > 0 && !IsOperating && !HasOperationError;
    public string PartsText => string.Join("   ·   ", Parts.Select(x => $"{x.Name} {(x.Path is not null ? "✓" : "—")}"));
    public bool CanAutoInstall => Id is "mkvtoolnix" or "afs" or "ffmpeg" or "assfonts";
    public bool ShowInstall => CanAutoInstall && (IsMissing || HasInstallError || Id == "afs" && State == ComponentState.DependencyMissing) && !IsOperating && !HasRemovalError && !IsConfirmingRemoval;
    public bool ShowGuide => !IsOperating && !HasRemovalError && !IsConfirmingRemoval && !ShowInstall && !IsAvailable && State != ComponentState.Checking;
    public bool ShowCancel => IsInstalling;
    public string InstallActionText => HasInstallError ? "重试" : Id == "afs" && State == ComponentState.DependencyMissing ? "安装依赖" : "安装";
    public bool CanChange => canOperate();
    public bool ShowProgress => IsOperating;
    public bool ProgressIndeterminate => IsUninstalling || install.Phase != ComponentPhase.Downloading || install.TotalBytes is null;
    public double ProgressValue => install.TotalBytes is > 0 ? 100d * install.Bytes / install.TotalBytes.Value : 0;
    public string ProgressText => IsUninstalling ? removalMessage : install.Phase == ComponentPhase.Downloading
        ? $"{install.Message}  {install.Bytes / 1048576d:0.0} / {install.TotalBytes.GetValueOrDefault() / 1048576d:0.0} MB"
        : install.Message;
    public string ErrorSummary => error.Length <= 110 ? error : error[..110] + "…";
    public bool HasDetails => HasOperationError;
    public bool IsDetailsOpen => detailsOpen;
    public string DetailsText => HasOperationError ? error + (logPath.Length > 0 ? "\n日志：" + logPath : "") : MissingText;
    public bool CanRemoveComponent => componentPlan is not null;
    public bool CanRemoveDependencies => dependencyPlan is not null;
    public bool ShowManagementMenu => (CanRemoveComponent || CanRemoveDependencies) && !IsOperating && !IsConfirmingRemoval;
    public bool HasIndependentDependencies => dependencyPlan is not null;
    public string DependencySummary => "独立 fonttools 依赖 · 来源 Mux Deck";
    public ComponentUninstallPlan? ConfirmationPlan => confirmationPlan;
    public ComponentUninstallPlan? LastRemovalPlan => lastRemovalPlan;
    public bool IsConfirmingRemoval => confirmationPlan is not null;
    public string RemovalTitle => confirmationPlan?.Kind == ComponentRemovalKind.Dependencies ? "卸载独立依赖" : "卸载 " + Name;
    public string RemovalDirectory => confirmationPlan?.TargetDirectory ?? "";
    public string RemovalDescription => confirmationPlan?.Kind == ComponentRemovalKind.Dependencies ? "将永久删除此依赖目录及其中的全部内容，组件主程序保留。"
        : Id == "afs" ? "将永久删除此组件目录及其中的全部内容，包含随包依赖；独立 fonttools 目录保留。" : "将永久删除此组件目录及其中的全部内容。";
    public bool ShowRemovalRetry => HasRemovalError && lastRemovalPlan is not null && !IsOperating && !IsConfirmingRemoval;
    public string GuideUrl => State == ComponentState.DependencyMissing ? "https://fonttools.readthedocs.io/en/latest/" : Definition.GuideUrl;
    public string GuideText => State == ComponentState.DependencyMissing
        ? "在所用 Python 环境安装 fonttools，确保 pyftsubset 和 ttx 可运行，然后重新检测。"
        : "请从官方页面安装 Windows 版本，或放入已设置的组件目录后重新检测。";
    public AsyncRelayCommand? InstallCommand { get; private set; }
    public RelayCommand? CancelCommand { get; private set; }
    public RelayCommand ToggleDetailsCommand { get; }
    public RelayCommand GuideCommand { get; }
    public RelayCommand? RequestRemoveComponentCommand { get; private set; }
    public RelayCommand? RequestRemoveDependenciesCommand { get; private set; }
    public RelayCommand? RetryRemovalCommand { get; private set; }
    public RelayCommand? CancelRemovalConfirmationCommand { get; private set; }
    public AsyncRelayCommand? ConfirmRemovalCommand { get; private set; }

    public void ConfigureRemoval(Action<ComponentRemovalKind> request, Func<Task> confirm)
    {
        RequestRemoveComponentCommand = new(() => request(ComponentRemovalKind.Component));
        RequestRemoveDependenciesCommand = new(() => request(ComponentRemovalKind.Dependencies));
        RetryRemovalCommand = new(() => { if (lastRemovalPlan is { } plan) request(plan.Kind); });
        CancelRemovalConfirmationCommand = new(CancelRemovalConfirmation);
        ConfirmRemovalCommand = new(confirm, () => IsConfirmingRemoval && canOperate(), ex => FailRemoval(ex.Message));
        Notify();
    }

    public void UpdateManagedDirectories(ComponentDirectoryQuery? main, ComponentDirectoryQuery? dependencies)
    {
        if (removalError && lastRemovalPlan is { } previous &&
            (main is null || !string.Equals(previous.TargetDirectory, main.TargetDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            // 用户已清理目标或切换组件目录，旧卸载错误不能阻止当前目录的安装。
            removalError = false; lastRemovalPlan = null; error = ""; logPath = ""; detailsOpen = false;
        }
        // 归属判断统一走注入的 Infrastructure 规则，Desktop 不再自行比较路径。
        componentPlan = main is not null && (Parts.All(p => p.Path is null) || Parts.Any(p => p.Path is { } path &&
            isInside(main.TargetDirectory, path)))
            ? new ComponentUninstallPlan(main.ComponentId, main.Kind, main.ToolsDirectory, main.TargetDirectory) : null;
        dependencyPlan = dependencies is null ? null : new ComponentUninstallPlan(dependencies.ComponentId, dependencies.Kind,
            dependencies.ToolsDirectory, dependencies.TargetDirectory);
        Notify();
    }
    public void ConfirmRemoval(ComponentUninstallPlan plan)
    {
        confirmationPlan = plan; detailsOpen = false; Notify();
    }
    public void CancelRemovalConfirmation() { confirmationPlan = null; Notify(); }
    public void BeginRemoval(ComponentUninstallPlan plan)
    {
        lastRemovalPlan = plan; confirmationPlan = null; removing = true; removalError = false;
        install = new(ComponentPhase.Idle, ""); error = ""; logPath = ""; detailsOpen = false; removalMessage = "正在卸载"; Notify();
    }
    public void SetRemovalProgress(ComponentProgress value) { removalMessage = value.Message; Notify(); }
    public void CompleteRemoval() { removing = false; removalError = false; Notify(); }
    public void FailRemoval(string message, string? log = null)
    {
        confirmationPlan = null; removing = false; removalError = true; install = new(ComponentPhase.Idle, "");
        error = message; logPath = log ?? ""; Notify();
    }

    public void ConfigureActions(Func<Task> installAction, Action cancelAction, Func<bool> guard)
    {
        canOperate = guard;
        InstallCommand = new(installAction, () => ShowInstall && canOperate(), ex => Fail(ex.Message));
        CancelCommand = new(cancelAction);
        Notify();
    }

    public void ApplyInspection(ComponentInspection value)
    {
        inspection = value;
        if (!value.Parts.IsEmpty)
        {
            Parts.Clear();
            foreach (var part in value.Parts)
                Parts.Add(new EnvironmentToolStatus(part.Name, PurposeText(part.Purpose), part.Path, SourceDisplay(part.Source)));
        }
        Notify();
    }

    private static string PurposeText(ComponentPartPurpose purpose) => purpose switch
    {
        ComponentPartPurpose.PackagingAndProbe => "封装与轨道探测",
        ComponentPartPurpose.SubtitleFontChapterExtraction => "字幕、字体与章节提取",
        ComponentPartPurpose.TrackMetadataRepair => "轨道与容器元数据修补",
        ComponentPartPurpose.SubtitleRenderPreview => "字幕渲染与预览",
        ComponentPartPurpose.PrimaryFontSubsetting => "字体子集化主工具",
        ComponentPartPurpose.FallbackFontSubsetting => "字体子集化备用工具",
        _ => ""
    };
    private static string SourceDisplay(ComponentPartSource source) => source switch
    {
        ComponentPartSource.MuxDeck => "Mux Deck", ComponentPartSource.Path => "PATH", ComponentPartSource.System => "系统", _ => "未找到"
    };
    public void SetInstallProgress(ComponentProgress value) { install = value; Notify(); }
    public void BeginInstall()
    {
        removing = false; removalError = false; confirmationPlan = null;
        error = ""; logPath = ""; detailsOpen = false;
        SetInstallProgress(new(ComponentPhase.Preparing, "准备安装"));
    }
    public void Fail(string message, string? log = null)
    {
        error = message; logPath = log ?? "";
        SetInstallProgress(new(ComponentPhase.Failed, "安装失败"));
    }
    public void Notify()
    {
        foreach (var property in GetType().GetProperties().Where(p => p.Name is not nameof(Definition) and not nameof(Parts)))
            Raise(property.Name);
        InstallCommand?.NotifyCanExecuteChanged();
        ConfirmRemovalCommand?.NotifyCanExecuteChanged();
    }
}
