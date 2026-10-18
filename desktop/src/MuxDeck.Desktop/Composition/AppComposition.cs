using Avalonia;
using MuxDeck.Application;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Core;
using MuxDeck.Infrastructure.Components;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop.Preferences;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;

namespace MuxDeck.Desktop.Composition;

// 生产启动与 Smoke 共用对象图；外部边界可替换，ViewModel 不创建默认服务。
public static class AppComposition
{
    public static void Start(string[] args)
    {
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--root"))
            throw new ArgumentException("Usage: MuxDeck.Desktop [--root <workspace directory>]");
        var explicitRoot = args.Length == 2 ? args[1] : null;
        var workspace = Workspace.Discover(explicitRoot);
        var storage = LoadStorage(workspace, !string.IsNullOrWhiteSpace(explicitRoot), StorageSettingsStore.DefaultPath);
        storage.EnsureDirectories();
        workspace = workspace with { ToolsDirectoryOverride = storage.ToolsDirectory };
        AppBuilder.Configure(() => new App(() => new MainWindow
        {
            DataContext = CreateMain(workspace, preferences: UserPreferencesStore.CreateDefault(storage.ConfigDirectory), storage: storage)
        })).UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }

    public static StoragePaths LoadStorage(Workspace workspace, bool portable, string settingsPath)
    {
        var fallback = portable ? StoragePaths.ForWorkspace(workspace.Root) : StoragePaths.Default;
        if (portable) return fallback;
        var loaded = new StorageSettingsStore(settingsPath).Load(new(fallback.AppDataRoot, fallback.ToolsDirectory));
        return StoragePaths.ForRoot(loaded.WorkDirectory, loaded.ToolsDirectory);
    }

    public static SingleMuxService CreateSingleService(Workspace workspace, IMuxToolRunner? runner = null)
    {
        runner ??= new MuxToolRunner();
        var prober = new MkvmergeProber(runner);
        var inputReader = new MuxInputReader();
        var pathFacts = SystemPathFacts.Instance;
        var probeMedia = new ProbeMediaUseCase(prober);
        var import = new PrepareImportUseCase(probeMedia, inputReader, pathFacts);
        return new(workspace, probeMedia, workspaces =>
            new RunSingleMuxUseCase(inputReader, new ToolReadiness(runner), prober,
                new AfsSubsetter(runner), new MkvmergeMuxer(runner), new MkvextractExtractor(runner),
                new MediaReferenceBuilder(runner), new MuxEvidenceReader(), new NoOverwritePublisher(), workspaces),
            inputReader: inputReader, pathFacts: pathFacts, collectFacts: pathFacts.Collect, import: import,
            slotPathFacts: pathFacts);
    }

    public static SingleMuxViewModel CreateSingle(SingleMuxService service, UserPreferencesStore? preferences = null) =>
        new(service, new TrackColumnLayout(preferences));

    public static EnvironmentViewModel CreateEnvironment(Workspace workspace, StoragePaths storage, IOperationCoordinator coordinator,
        string? settingsPath = null, IComponentVerifier? verifier = null, IComponentInstaller? installer = null,
        IComponentUninstaller? uninstaller = null)
    {
        var settings = new StorageSettingsStore(settingsPath ?? StorageSettingsStore.DefaultPath);
        verifier ??= new ComponentVerifier();
        installer ??= new ComponentInstaller(verifier: verifier);
        uninstaller ??= new ComponentUninstaller();
        var manager = new ManageComponentsUseCase(new InfrastructureComponentManager(verifier, installer, uninstaller), coordinator);
        var configuration = new ComponentConfiguration(workspace.Root, storage.ToolsDirectory, storage.LogsDirectory, storage.CacheDirectory, workspace.FontToolsDirectoryOverride);
        var definitions = ComponentCatalog.All.Select(value => new EnvironmentComponentDefinition(value.Id, value.Name, value.Description, value.IsCore, value.ToolNames, value.GuideUrl)).ToArray();
        IReadOnlyList<EnvironmentToolStatus> Inspect(ComponentConfiguration value) => new ToolLocator(value).Inspect().Select(part => new EnvironmentToolStatus(part.Name, part.Purpose, part.Path, part.Source)).ToArray();
        // 保存路径时由装配负责生成目录，ViewModel 不再自己拼路径。
        static EnvironmentStorage CreateStorage(string workRoot, string toolsDirectory)
        {
            var paths = StoragePaths.ForRoot(workRoot, toolsDirectory);
            paths.EnsureDirectories();
            return new(paths.AppDataRoot, paths.ToolsDirectory, paths.ConfigDirectory, paths.LogsDirectory, paths.CacheDirectory);
        }
        var viewStorage = new EnvironmentStorage(storage.AppDataRoot, storage.ToolsDirectory, storage.ConfigDirectory, storage.LogsDirectory, storage.CacheDirectory);
        // 目录归属判断复用 Infrastructure 的纯规则，Desktop 只注入不重写。
        return new(configuration, viewStorage, manager, Inspect, ComponentDirectories.IsInside, CreateStorage, definitions, coordinator, next => settings.Save(new(next.AppDataRoot, next.ToolsDirectory)));
    }

    public static MainWindowViewModel CreateMain(Workspace workspace, SingleMuxService? singleService = null,
        UserPreferencesStore? preferences = null, StoragePaths? storage = null, EnvironmentViewModel? environment = null)
    {
        var paths = storage ?? StoragePaths.ForWorkspace(workspace.Root);
        if (storage is not null)
            workspace = workspace with { ToolsDirectoryOverride = storage.ToolsDirectory, StorageOverride = storage };
        // 每个对象图只创建一个协调器；环境页与组件用例共用同一实例。
        var env = environment ?? CreateEnvironment(workspace, paths, new OperationCoordinator());
        return new(workspace, env.Storage, env, CreateSingle(singleService ?? CreateSingleService(workspace), preferences), env.Coordinator);
    }
}
