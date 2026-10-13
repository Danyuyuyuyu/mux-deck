using System.Collections.ObjectModel;
using MuxDeck.Core;
using MuxDeck.Core.Components;

namespace MuxDeck.Desktop.ViewModels;

public sealed class EnvironmentComponentViewModel : ObservableObject
{
    private ComponentInspection inspection;
    private ComponentInstallProgress install = new(ComponentInstallPhase.Idle, "");
    private string error = "", logPath = "";
    private bool detailsOpen;
    private bool removing, removalError;
    private string removalMessage = "";
    private ComponentRemovalPlan? componentPlan, dependencyPlan, confirmationPlan, lastRemovalPlan;
    private Func<bool> canOperate = () => false;

    public EnvironmentComponentViewModel(string name, string description, string category, IReadOnlyList<ToolStatus> parts)
        : this(ComponentCatalog.All.FirstOrDefault(c => c.Name == name) ?? new(name, name, description, category == "核心组件", parts.Select(p => p.Name).ToArray(), ""), parts) { }

    public EnvironmentComponentViewModel(ComponentDefinition definition, IReadOnlyList<ToolStatus> parts)
    {
        Definition = definition;
        Parts = new(parts);
        inspection = new(parts.All(p => p.Path is null) ? ComponentState.Missing : ComponentState.Found, parts);
        ToggleDetailsCommand = new(() => { detailsOpen = !detailsOpen; Notify(); });
        GuideCommand = new(() => { detailsOpen = !detailsOpen; Notify(); });
    }

    public ComponentDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Category => Definition.IsCore ? "核心组件" : "扩展组件";
    public ObservableCollection<ToolStatus> Parts { get; }
    public ComponentState State => inspection.State;
    public bool HasParts => Parts.Count > 1;
    public bool IsAvailable => State == ComponentState.Available;
    public bool IsPartiallyAvailable => State == ComponentState.Partial;
    public bool IsMissing => State == ComponentState.Missing;
    public bool IsInstalling => install.Phase is ComponentInstallPhase.Preparing or ComponentInstallPhase.Downloading or ComponentInstallPhase.Installing or ComponentInstallPhase.Verifying;
    public bool HasInstallError => install.Phase == ComponentInstallPhase.Failed;
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
        ComponentInstallPhase.Preparing => "准备安装",
        ComponentInstallPhase.Downloading => "下载中",
        ComponentInstallPhase.Installing => "正在安装",
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
    public bool CanAutoInstall => ComponentCatalog.SupportsInstall(Id);
    public bool ShowInstall => CanAutoInstall && (IsMissing || HasInstallError || Id == "afs" && State == ComponentState.DependencyMissing) && !IsOperating && !HasRemovalError && !IsConfirmingRemoval;
    public bool ShowGuide => !IsOperating && !HasRemovalError && !IsConfirmingRemoval && !ShowInstall && !IsAvailable && State != ComponentState.Checking;
    public bool ShowCancel => IsInstalling;
    public string InstallActionText => HasInstallError ? "重试" : Id == "afs" && State == ComponentState.DependencyMissing ? "安装依赖" : "安装";
    public bool CanChange => canOperate();
    public bool ShowProgress => IsOperating;
    public bool ProgressIndeterminate => IsUninstalling || install.Phase != ComponentInstallPhase.Downloading || install.TotalBytes is null;
    public double ProgressValue => install.TotalBytes is > 0 ? 100d * install.Bytes / install.TotalBytes.Value : 0;
    public string ProgressText => IsUninstalling ? removalMessage : install.Phase == ComponentInstallPhase.Downloading
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
    public ComponentRemovalPlan? ConfirmationPlan => confirmationPlan;
    public ComponentRemovalPlan? LastRemovalPlan => lastRemovalPlan;
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

    public void UpdateManagedDirectories(StoragePaths storage)
    {
        if (removalError && lastRemovalPlan is { } previous &&
            (!string.Equals(previous.ToolsDirectory, Path.TrimEndingDirectorySeparator(Path.GetFullPath(storage.ToolsDirectory)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !Directory.Exists(previous.TargetDirectory) && !File.Exists(previous.TargetDirectory)))
        {
            // 用户已清理目标或切换组件目录，旧卸载错误不能阻止当前目录的安装。
            removalError = false; lastRemovalPlan = null; error = ""; logPath = ""; detailsOpen = false;
        }
        var main = ComponentDirectories.TryPlan(storage, Id, ComponentRemovalKind.Component);
        componentPlan = main is not null && (Parts.All(p => p.Path is null) || Parts.Any(p => p.Path is { } path && ComponentDirectories.IsInside(main.TargetDirectory, path))) ? main : null;
        dependencyPlan = Id == "afs" ? ComponentDirectories.TryPlan(storage, Id, ComponentRemovalKind.Dependencies) : null;
        Notify();
    }
    public void ConfirmRemoval(ComponentRemovalPlan plan)
    {
        confirmationPlan = plan; detailsOpen = false; Notify();
    }
    public void CancelRemovalConfirmation() { confirmationPlan = null; Notify(); }
    public void BeginRemoval(ComponentRemovalPlan plan)
    {
        lastRemovalPlan = plan; confirmationPlan = null; removing = true; removalError = false;
        install = new(ComponentInstallPhase.Idle, ""); error = ""; logPath = ""; detailsOpen = false; removalMessage = "正在卸载"; Notify();
    }
    public void SetRemovalProgress(ComponentRemovalProgress value) { removalMessage = value.Message; Notify(); }
    public void CompleteRemoval() { removing = false; removalError = false; Notify(); }
    public void FailRemoval(string message, string? log = null)
    {
        confirmationPlan = null; removing = false; removalError = true; install = new(ComponentInstallPhase.Idle, "");
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
        Parts.Clear();
        foreach (var part in value.Parts) Parts.Add(part);
        Notify();
    }
    public void SetInstallProgress(ComponentInstallProgress value) { install = value; Notify(); }
    public void BeginInstall()
    {
        removing = false; removalError = false; confirmationPlan = null;
        error = ""; logPath = ""; detailsOpen = false;
        SetInstallProgress(new(ComponentInstallPhase.Preparing, "准备安装"));
    }
    public void Fail(string message, string? log = null)
    {
        error = message; logPath = log ?? "";
        SetInstallProgress(new(ComponentInstallPhase.Failed, "安装失败"));
    }
    public void Notify()
    {
        foreach (var property in GetType().GetProperties().Where(p => p.Name is not nameof(Definition) and not nameof(Parts)))
            Raise(property.Name);
        InstallCommand?.NotifyCanExecuteChanged();
        ConfirmRemovalCommand?.NotifyCanExecuteChanged();
    }
}
