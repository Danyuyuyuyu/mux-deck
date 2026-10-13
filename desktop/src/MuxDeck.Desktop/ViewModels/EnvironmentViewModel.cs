using System.Collections.ObjectModel;
using MuxDeck.Core;
using MuxDeck.Core.Components;
using MuxDeck.Desktop.Preferences;

namespace MuxDeck.Desktop.ViewModels;

public sealed class EnvironmentViewModel : ObservableObject
{
    private Workspace workspace;
    private readonly StorageSettingsStore settingsStore;
    private readonly IComponentVerifier verifier;
    private readonly IComponentInstaller installer;
    private readonly IComponentUninstaller uninstaller;
    private Func<bool> workBusy = () => false;
    private bool settingsOpen, installing, uninstalling, checking, closing;
    private string toolsDirectoryInput, workDirectoryInput, lastChecked = "尚未检测", settingsMessage = "";
    private CancellationTokenSource? checkCancellation, installCancellation, removalCancellation;
    private readonly List<Task> checkTasks = [];
    private long checkRevision, installRevision;

    public EnvironmentViewModel(Workspace workspace, StoragePaths storage, StorageSettingsStore? settingsStore = null,
        IComponentVerifier? verifier = null, IComponentInstaller? installer = null, IComponentUninstaller? uninstaller = null)
    {
        this.workspace = workspace;
        Storage = storage;
        this.settingsStore = settingsStore ?? new StorageSettingsStore(StorageSettingsStore.DefaultPath);
        this.verifier = verifier ?? new ComponentVerifier();
        this.installer = installer ?? new ComponentInstaller(verifier: this.verifier);
        this.uninstaller = uninstaller ?? new ComponentUninstaller();
        toolsDirectoryInput = storage.ToolsDirectory;
        workDirectoryInput = storage.AppDataRoot;
        RefreshCommand = new(Refresh);
        ToggleSettingsCommand = new(() => IsSettingsOpen = !IsSettingsOpen);
        SaveSettingsCommand = new(SaveSettings);
        ResetSettingsCommand = new(ResetSettings);
        var located = new ToolLocator(workspace).Inspect();
        foreach (var part in located) Tools.Add(part);
        foreach (var definition in ComponentCatalog.All)
        {
            var row = new EnvironmentComponentViewModel(definition, definition.ToolNames.Select(n => located.Single(t => t.Name == n)).ToArray());
            row.ConfigureActions(() => InstallAsync(row), () => installCancellation?.Cancel(), () => CanChangePaths);
            row.ConfigureRemoval(kind => RequestRemoval(row, kind), () => RemoveAsync(row));
            row.UpdateManagedDirectories(Storage);
            (definition.IsCore ? CoreComponents : ExtensionComponents).Add(row);
        }
    }

    public StoragePaths Storage { get; private set; }
    public ObservableCollection<ToolStatus> Tools { get; } = [];
    public ObservableCollection<EnvironmentComponentViewModel> CoreComponents { get; } = [];
    public ObservableCollection<EnvironmentComponentViewModel> ExtensionComponents { get; } = [];
    private IEnumerable<EnvironmentComponentViewModel> Components => CoreComponents.Concat(ExtensionComponents);
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand ResetSettingsCommand { get; }
    public event Action<StoragePaths>? StorageChanged;
    public event Action? OperationChanged;
    public Task ValidationTask { get; private set; } = Task.CompletedTask;
    public Task InstallTask { get; private set; } = Task.CompletedTask;
    public Task RemovalTask { get; private set; } = Task.CompletedTask;
    public bool IsInstalling => installing;
    public bool IsUninstalling => uninstalling;
    public bool IsModifyingComponents => installing || uninstalling;
    public bool IsChecking => checking;
    public bool CanRefresh => !IsModifyingComponents && !closing;
    public bool CanChangePaths => !IsModifyingComponents && !closing && !workBusy();
    public bool IsSettingsOpen
    {
        get => settingsOpen;
        private set { if (Set(ref settingsOpen, value)) { Raise(nameof(SettingsToggleText)); Raise(nameof(SettingsToggleGlyph)); } }
    }
    public string SettingsToggleText => "组件管理";
    public string SettingsToggleGlyph => IsSettingsOpen ? "⌃" : "›";
    public string SettingsSummaryText => "工具目录 · 工作目录";
    public string ToolsDirectoryInput { get => toolsDirectoryInput; set { if (CanChangePaths) Set(ref toolsDirectoryInput, value); } }
    public string WorkDirectoryInput { get => workDirectoryInput; set { if (CanChangePaths) Set(ref workDirectoryInput, value); } }
    public string LastChecked { get => lastChecked; private set => Set(ref lastChecked, value); }
    public string SettingsMessage { get => settingsMessage; private set => Set(ref settingsMessage, value); }
    public string SummaryTitle => checking ? "检测中" : uninstalling ? "正在卸载组件" : installing ? "正在安装组件" : IsEnvironmentReady ? "环境就绪" : "需要处理";
    public bool IsSummaryReady => !checking && !IsModifyingComponents && IsEnvironmentReady;
    public bool IsSummaryNeedsAttention => !checking && !IsModifyingComponents && !IsEnvironmentReady;
    public string SummaryDescription => "组件验证仅检查当前工具和必要依赖，实际素材仍由任务校验。";
    public string CoreSummary => $"核心组件  {CoreComponents.Count(x => x.IsAvailable)} / {CoreComponents.Count}";
    public string ExtensionSummary => $"扩展组件  {ExtensionComponents.Count(x => x.IsAvailable)} / {ExtensionComponents.Count}";
    public string SummaryMetrics => $"{CoreSummary}   ·   {ExtensionSummary}";
    public string CoreSectionTitle => $"核心组件 · {CoreComponents.Count}";
    public string ExtensionSectionTitle => $"扩展组件 · {ExtensionComponents.Count}";
    public bool IsEnvironmentReady => !checking && CoreComponents.Count > 0 && CoreComponents.All(x => x.IsAvailable);

    public void SetWorkGuard(Func<bool> guard) { workBusy = guard; NotifyWorkChanged(); }
    public void NotifyWorkChanged()
    {
        Raise(nameof(CanChangePaths));
        foreach (var row in Components) row.Notify();
    }

    public void Refresh()
    {
        if (!CanRefresh) return;
        checkCancellation?.Cancel();
        var lifetime = new CancellationTokenSource();
        checkCancellation = lifetime;
        var revision = ++checkRevision;
        var snapshot = workspace;
        var located = new ToolLocator(snapshot).Inspect();
        Tools.Clear();
        foreach (var part in located) Tools.Add(part);
        var rows = Components.ToArray();
        foreach (var row in rows)
        {
            row.CancelRemovalConfirmation();
            var parts = row.Definition.ToolNames.Select(n => located.Single(t => t.Name == n)).ToArray();
            row.ApplyInspection(new(parts.All(p => p.Path is null) ? ComponentState.Missing : ComponentState.Checking, parts));
            row.UpdateManagedDirectories(Storage);
        }
        checking = true;
        NotifySummary();
        ValidationTask = ValidateAllAsync();
        checkTasks.RemoveAll(t => t.IsCompleted);
        checkTasks.Add(ValidationTask);

        async Task ValidateAllAsync()
        {
            try
            {
                await Task.WhenAll(rows.Select(async row =>
                {
                    var inspection = await verifier.VerifyAsync(row.Definition, snapshot, lifetime.Token);
                    if (!closing && revision == checkRevision && !lifetime.IsCancellationRequested)
                    {
                        row.ApplyInspection(inspection);
                        row.UpdateManagedDirectories(Storage);
                    }
                }));
                if (revision == checkRevision && !closing) LastChecked = DateTime.Now.ToString("HH:mm:ss");
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (revision == checkRevision && !closing) SettingsMessage = "检测失败：" + ex.Message;
            }
            finally
            {
                lifetime.Dispose();
                if (ReferenceEquals(checkCancellation, lifetime)) checkCancellation = null;
                if (revision == checkRevision) { checking = false; NotifySummary(); }
            }
        }
    }

    public Task InstallAsync(EnvironmentComponentViewModel row)
    {
        if (!CanChangePaths || !row.ShowInstall || !Components.Contains(row)) return Task.CompletedTask;
        foreach (var component in Components) component.CancelRemovalConfirmation();
        checkCancellation?.Cancel();
        ++checkRevision;
        checking = false;
        installing = true;
        var revision = ++installRevision;
        installCancellation = new();
        row.BeginInstall();
        NotifySummary();
        OperationChanged?.Invoke();
        NotifyWorkChanged();
        InstallTask = RunInstallAsync(row, Storage, installCancellation, revision);
        return InstallTask;
    }

    private async Task RunInstallAsync(EnvironmentComponentViewModel row, StoragePaths snapshot, CancellationTokenSource lifetime, long revision)
    {
        try
        {
            // 异步命令先公开任务与忙碌状态，关闭钩子始终能等待同一安装任务。
            await Task.Yield();
            var progress = new Progress<ComponentInstallProgress>(value =>
            {
                if (!closing && installing && revision == installRevision) row.SetInstallProgress(value);
            });
            var result = await installer.InstallAsync(row.Id, snapshot, progress, lifetime.Token);
            row.SetInstallProgress(new(ComponentInstallPhase.Completed, "安装完成"));
            // 提交后不再使用已取消的安装 token，把正式组件重新定位验证。
            var inspection = await verifier.VerifyAsync(row.Definition, workspace, CancellationToken.None);
            row.ApplyInspection(inspection);
            if (inspection.State != ComponentState.Available) row.Fail(inspection.Reason ?? "安装后验证未通过。", result.LogPath);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { row.SetInstallProgress(new(ComponentInstallPhase.Cancelled, "安装已取消")); }
        catch (ComponentInstallException ex) { row.Fail(ex.Message, ex.LogPath); }
        catch (Exception ex) { row.Fail(ex.Message); }
        finally
        {
            ++installRevision;
            lifetime.Dispose();
            installCancellation = null;
            installing = false;
            OperationChanged?.Invoke();
            NotifyWorkChanged();
            NotifySummary();
            if (!closing) Refresh();
        }
    }

    public async Task CancelAndWaitAsync()
    {
        closing = true;
        ++checkRevision;
        checkCancellation?.Cancel();
        installCancellation?.Cancel();
        removalCancellation?.Cancel();
        await Task.WhenAll(checkTasks.Append(InstallTask).Append(RemovalTask));
    }

    public void RequestRemoval(EnvironmentComponentViewModel row, ComponentRemovalKind kind)
    {
        if (!CanChangePaths || checking || !Components.Contains(row)) return;
        if (kind == ComponentRemovalKind.Component && !row.CanRemoveComponent && !row.HasRemovalError ||
            kind == ComponentRemovalKind.Dependencies && !row.CanRemoveDependencies && !row.HasRemovalError) return;
        try
        {
            var plan = ComponentDirectories.CreatePlan(Storage, row.Id, kind);
            foreach (var component in Components) component.CancelRemovalConfirmation();
            row.ConfirmRemoval(plan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { row.FailRemoval(ex.Message); }
    }

    public Task RemoveAsync(EnvironmentComponentViewModel row)
    {
        if (!CanChangePaths || !Components.Contains(row) || row.ConfirmationPlan is not { } plan) return Task.CompletedTask;
        var snapshot = Storage;
        try { ComponentDirectories.Validate(plan, snapshot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { row.FailRemoval(ex.Message); return Task.CompletedTask; }
        checkCancellation?.Cancel(); ++checkRevision; checking = false; uninstalling = true;
        var revision = ++installRevision;
        var lifetime = new CancellationTokenSource(); removalCancellation = lifetime;
        foreach (var component in Components) component.CancelRemovalConfirmation();
        row.BeginRemoval(plan);
        NotifySummary(); OperationChanged?.Invoke(); NotifyWorkChanged();
        RemovalTask = RunRemovalAsync();
        return RemovalTask;

        async Task RunRemovalAsync()
        {
            try
            {
                await Task.Yield();
                // 检测进程必须真正退出，不能在取消信号刚发出时删除正在使用的工具。
                await Task.WhenAll(checkTasks.ToArray());
                var progress = new Progress<ComponentRemovalProgress>(value =>
                {
                    if (!closing && uninstalling && revision == installRevision) row.SetRemovalProgress(value);
                });
                await uninstaller.UninstallAsync(plan, snapshot, progress, lifetime.Token);
                row.CompleteRemoval();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { row.CompleteRemoval(); }
            catch (ComponentRemovalException ex) { row.FailRemoval(ex.Message, ex.LogPath); }
            catch (Exception ex) { row.FailRemoval(ex.Message); }
            finally
            {
                ++installRevision; lifetime.Dispose(); removalCancellation = null; uninstalling = false;
                OperationChanged?.Invoke(); NotifyWorkChanged(); NotifySummary();
                if (!closing) Refresh();
            }
        }
    }

    private void SaveSettings()
    {
        if (!CanChangePaths) { SettingsMessage = "封装、探测或组件安装/卸载期间不能修改路径。"; return; }
        try
        {
            if (string.IsNullOrWhiteSpace(WorkDirectoryInput) || string.IsNullOrWhiteSpace(ToolsDirectoryInput)) throw new ArgumentException("路径不能为空。");
            var work = System.Environment.ExpandEnvironmentVariables(WorkDirectoryInput.Trim());
            var toolsPath = System.Environment.ExpandEnvironmentVariables(ToolsDirectoryInput.Trim());
            if (!Path.IsPathFullyQualified(work) || !Path.IsPathFullyQualified(toolsPath)) throw new ArgumentException("请选择绝对目录路径。");
            var next = StoragePaths.ForRoot(work, toolsPath);
            next.EnsureDirectories();
            settingsStore.Save(next);
            Storage = next;
            workspace = workspace with { ToolsDirectoryOverride = next.ToolsDirectory, StorageOverride = next };
            StorageChanged?.Invoke(next);
            Refresh();
            SettingsMessage = "路径已保存，组件已自动重新检测。";
            Raise(nameof(Storage));
            IsSettingsOpen = false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { SettingsMessage = "保存失败：" + ex.Message; }
    }

    private void ResetSettings()
    {
        if (!CanChangePaths) return;
        ToolsDirectoryInput = StoragePaths.Default.ToolsDirectory;
        WorkDirectoryInput = StoragePaths.Default.AppDataRoot;
        SettingsMessage = "已恢复默认路径，点击保存后生效。";
    }
    private void NotifySummary()
    {
        foreach (var name in new[] { nameof(SummaryTitle), nameof(IsSummaryReady), nameof(IsSummaryNeedsAttention), nameof(SummaryMetrics), nameof(CoreSummary), nameof(ExtensionSummary), nameof(IsEnvironmentReady), nameof(IsInstalling), nameof(IsUninstalling), nameof(IsModifyingComponents), nameof(IsChecking), nameof(CanRefresh), nameof(CanChangePaths), nameof(CoreSectionTitle), nameof(ExtensionSectionTitle) }) Raise(name);
    }
}
