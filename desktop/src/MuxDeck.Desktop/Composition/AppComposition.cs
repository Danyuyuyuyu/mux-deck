using Avalonia;
using MuxDeck.Application;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Core;
using MuxDeck.Core.Components;
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
    // 4a-2 前旧组件 API 仍通过同一个桥接取得租约，创建归属已经移出 Core。
    private static readonly Lazy<IOperationCoordinator> legacyCoordinator = new(() =>
    {
        var coordinator = new OperationCoordinator();
        ComponentManagement.ConfigureLegacyCoordinator(coordinator);
        return coordinator;
    });

    public static void InitializeLegacyComponents() => _ = legacyCoordinator.Value;

    public static void Start(string[] args)
    {
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--root"))
            throw new ArgumentException("Usage: MuxDeck.Desktop [--root <workspace directory>]");
        var explicitRoot = args.Length == 2 ? args[1] : null;
        var workspace = Workspace.Discover(explicitRoot);
        var storage = LoadStorage(workspace, !string.IsNullOrWhiteSpace(explicitRoot), StorageSettingsStore.DefaultPath);
        storage.EnsureDirectories();
        workspace = workspace with { ToolsDirectoryOverride = storage.ToolsDirectory };
        InitializeLegacyComponents();
        AppBuilder.Configure(() => new App(() => new MainWindow
        {
            DataContext = CreateMain(workspace, preferences: UserPreferencesStore.CreateDefault(storage), storage: storage)
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
        return new(workspace, new ProbeMediaUseCase(prober), workspaces =>
            new RunSingleMuxUseCase(new MuxInputReader(), new ToolReadiness(runner), prober,
                new AfsSubsetter(runner), new MkvmergeMuxer(runner), new MkvextractExtractor(runner),
                new MediaReferenceBuilder(runner), new MuxEvidenceReader(), new NoOverwritePublisher(), workspaces));
    }

    public static SingleMuxViewModel CreateSingle(SingleMuxService service, UserPreferencesStore? preferences = null) =>
        new(service, new TrackColumnLayout(preferences));

    public static EnvironmentViewModel CreateEnvironment(Workspace workspace, StoragePaths storage,
        string? settingsPath = null, IComponentVerifier? verifier = null, IComponentInstaller? installer = null,
        IComponentUninstaller? uninstaller = null)
    {
        InitializeLegacyComponents();
        var settings = new StorageSettingsStore(settingsPath ?? StorageSettingsStore.DefaultPath);
        verifier ??= new ComponentVerifier();
        installer ??= new ComponentInstaller(verifier: verifier);
        uninstaller ??= new ComponentUninstaller();
        return new(workspace, storage, next => settings.Save(new(next.AppDataRoot, next.ToolsDirectory)),
            verifier, installer, uninstaller, current => new ToolLocator(current).Inspect());
    }

    public static MainWindowViewModel CreateMain(Workspace workspace, SingleMuxService? singleService = null,
        UserPreferencesStore? preferences = null, StoragePaths? storage = null, EnvironmentViewModel? environment = null)
    {
        var paths = storage ?? StoragePaths.ForWorkspace(workspace.Root);
        if (storage is not null)
            workspace = workspace with { ToolsDirectoryOverride = storage.ToolsDirectory, StorageOverride = storage };
        return new(workspace, paths, environment ?? CreateEnvironment(workspace, paths),
            CreateSingle(singleService ?? CreateSingleService(workspace), preferences));
    }
}
