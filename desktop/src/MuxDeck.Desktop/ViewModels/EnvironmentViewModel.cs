using System.Collections.ObjectModel;
using MuxDeck.Contracts;

namespace MuxDeck.Desktop.ViewModels;

public sealed class EnvironmentViewModel : ObservableObject
{
    private ComponentConfiguration configuration;
    private readonly Action<EnvironmentStorage> saveSettings;
    private readonly IManageComponents manager;
    private readonly Func<ComponentConfiguration, IReadOnlyList<EnvironmentToolStatus>> inspectTools;
    private readonly Func<string, string, bool> isInside;
    private readonly Func<string, string, EnvironmentStorage> createStorage;
    private readonly IReadOnlyList<EnvironmentComponentDefinition> definitions;
    private EnvironmentStorage storage;
    private Func<bool> workBusy = () => false;
    private bool settingsOpen, installing, uninstalling, checking, closing;
    private string toolsDirectoryInput, workDirectoryInput, lastChecked = "尚未检测", settingsMessage = "";
    private CancellationTokenSource? checkCancellation, installCancellation, removalCancellation;
    private readonly List<Task> checkTasks = [];
    private long checkRevision, installRevision;

    public EnvironmentViewModel(ComponentConfiguration configuration, EnvironmentStorage storage, IManageComponents manager,
        Func<ComponentConfiguration, IReadOnlyList<EnvironmentToolStatus>> inspectTools, Func<string, string, bool> isInside,
        Func<string, string, EnvironmentStorage> createStorage, IReadOnlyList<EnvironmentComponentDefinition> definitions,
        IOperationCoordinator coordinator, Action<EnvironmentStorage> saveSettings)
    {
        this.configuration = configuration;
        this.storage = storage;
        this.manager = manager;
        this.inspectTools = inspectTools;
        this.isInside = isInside;
        this.createStorage = createStorage;
        this.definitions = definitions;
        this.saveSettings = saveSettings;
        Coordinator = coordinator;
        toolsDirectoryInput = storage.ToolsDirectory;
        workDirectoryInput = storage.AppDataRoot;
        RefreshCommand = new(Refresh);
        ToggleSettingsCommand = new(() => IsSettingsOpen = !IsSettingsOpen);
        SaveSettingsCommand = new(SaveSettings);
        ResetSettingsCommand = new(ResetSettings);
        var located = inspectTools(configuration);
        foreach (var part in located) Tools.Add(part);
        foreach (var definition in definitions)
        {
            var row = new EnvironmentComponentViewModel(definition, definition.ToolNames.Select(n => located.Single(t => t.Name == n)).ToArray(), isInside);
            row.ConfigureActions(() => InstallAsync(row), () => installCancellation?.Cancel(), () => CanChangePaths);
            row.ConfigureRemoval(kind => RequestRemoval(row, kind), () => RemoveAsync(row));
            UpdateDirectories(row);
            (definition.IsCore ? CoreComponents : ExtensionComponents).Add(row);
        }
    }

    public EnvironmentStorage Storage { get => storage; private set => Set(ref storage, value); }
    public ObservableCollection<EnvironmentToolStatus> Tools { get; } = [];
    public ObservableCollection<EnvironmentComponentViewModel> CoreComponents { get; } = [];
    public ObservableCollection<EnvironmentComponentViewModel> ExtensionComponents { get; } = [];
    private IEnumerable<EnvironmentComponentViewModel> Components => CoreComponents.Concat(ExtensionComponents);
    // 与环境页共用同一协调器，供 4d 读取跨页面忙碌状态。
    public IOperationCoordinator Coordinator { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand ResetSettingsCommand { get; }
    public event Action<EnvironmentStorage>? StorageChanged;
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
        var snapshot = configuration;
        var located = inspectTools(snapshot);
        Tools.Clear();
        foreach (var part in located) Tools.Add(part);
        var rows = Components.ToArray();
        foreach (var row in rows)
        {
            row.CancelRemovalConfirmation();
            row.ApplyInspection(new(row.Id, ComponentState.Checking, []));
            UpdateDirectories(row);
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
                var inspections = await manager.InspectAsync(snapshot, lifetime.Token).ConfigureAwait(false);
                if (!closing && revision == checkRevision && !lifetime.IsCancellationRequested)
                    foreach (var row in rows)
                    {
                        if (inspections.FirstOrDefault(x => x.Id == row.Id) is { } value) row.ApplyInspection(value);
                        UpdateDirectories(row);
                    }
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

    private void UpdateDirectories(EnvironmentComponentViewModel row)
    {
        var main = manager.QueryUninstallDirectory(row.Id, ComponentRemovalKind.Component, configuration);
        var dependencies = row.Id == "afs" ? manager.QueryUninstallDirectory(row.Id, ComponentRemovalKind.Dependencies, configuration) : null;
        row.UpdateManagedDirectories(main, dependencies);
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
        InstallTask = RunInstallAsync(row, configuration, installCancellation, revision);
        return InstallTask;
    }

    private async Task RunInstallAsync(EnvironmentComponentViewModel row, ComponentConfiguration snapshot, CancellationTokenSource lifetime, long revision)
    {
        try
        {
            await Task.Yield();
            var progress = new Progress<ComponentProgress>(value =>
            {
                if (!closing && installing && revision == installRevision) row.SetInstallProgress(value);
            });
            var result = await manager.InstallAsync(row.Id, snapshot, progress, lifetime.Token);
            row.SetInstallProgress(new(ComponentPhase.Completed, "安装完成"));
            row.ApplyInspection(await manager.InspectAsync(row.Id, configuration, CancellationToken.None));
            if (result.Failure is { } failure) row.Fail(failure.Message, result.Log?.Path);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { row.SetInstallProgress(new(ComponentPhase.Cancelled, "安装已取消")); }
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
        _ = PrepareRemovalAsync(row, kind);
    }

    private async Task PrepareRemovalAsync(EnvironmentComponentViewModel row, ComponentRemovalKind kind)
    {
        try
        {
            var plan = await manager.PrepareUninstallAsync(row.Id, kind, configuration, CancellationToken.None);
            foreach (var component in Components) component.CancelRemovalConfirmation();
            row.ConfirmRemoval(plan);
        }
        catch (Exception ex) { row.FailRemoval(ex.Message); }
    }

    public Task RemoveAsync(EnvironmentComponentViewModel row)
    {
        if (!CanChangePaths || !Components.Contains(row) || row.ConfirmationPlan is not { } plan) return Task.CompletedTask;
        checkCancellation?.Cancel();
        ++checkRevision;
        checking = false;
        uninstalling = true;
        var revision = ++installRevision;
        var lifetime = new CancellationTokenSource();
        removalCancellation = lifetime;
        foreach (var component in Components) component.CancelRemovalConfirmation();
        row.BeginRemoval(plan);
        NotifySummary();
        OperationChanged?.Invoke();
        NotifyWorkChanged();
        RemovalTask = RunRemovalAsync();
        return RemovalTask;

        async Task RunRemovalAsync()
        {
            try
            {
                await Task.Yield();
                var progress = new Progress<ComponentProgress>(value =>
                {
                    if (!closing && uninstalling && revision == installRevision) row.SetRemovalProgress(value);
                });
                var result = await manager.UninstallAsync(plan, configuration, progress, lifetime.Token);
                if (result.Failure is { } failure) row.FailRemoval(failure.Message, result.Log?.Path);
                else row.CompleteRemoval();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { row.CompleteRemoval(); }
            catch (Exception ex) { row.FailRemoval(ex.Message); }
            finally
            {
                ++installRevision;
                lifetime.Dispose();
                removalCancellation = null;
                uninstalling = false;
                OperationChanged?.Invoke();
                NotifyWorkChanged();
                NotifySummary();
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
            // 路径生成与建目录移到装配注入的工厂，保持校验输入→生成路径→创建目录→保存→更新配置的顺序与异常文案。
            var next = createStorage(work, toolsPath);
            saveSettings(next);
            Storage = next;
            configuration = new ComponentConfiguration(configuration.WorkspaceRoot, next.ToolsDirectory, next.LogsDirectory, next.CacheDirectory, configuration.FontToolsDirectoryOverride);
            StorageChanged?.Invoke(next);
            Refresh();
            SettingsMessage = "路径已保存，组件已自动重新检测。";
            IsSettingsOpen = false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { SettingsMessage = "保存失败：" + ex.Message; }
    }

    private void ResetSettings()
    {
        if (!CanChangePaths) return;
        ToolsDirectoryInput = "";
        WorkDirectoryInput = "";
        SettingsMessage = "已恢复默认路径，点击保存后生效。";
    }
    private void NotifySummary()
    {
        foreach (var name in new[] { nameof(SummaryTitle), nameof(IsSummaryReady), nameof(IsSummaryNeedsAttention), nameof(SummaryMetrics), nameof(CoreSummary), nameof(ExtensionSummary), nameof(IsEnvironmentReady), nameof(IsInstalling), nameof(IsUninstalling), nameof(IsModifyingComponents), nameof(IsChecking), nameof(CanRefresh), nameof(CanChangePaths), nameof(CoreSectionTitle), nameof(ExtensionSectionTitle) }) Raise(name);
    }
}
