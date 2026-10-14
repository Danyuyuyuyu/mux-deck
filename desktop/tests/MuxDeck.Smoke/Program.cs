using MuxDeck.Desktop.Composition;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;
using MuxDeck.Infrastructure.Components;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using MuxDeck.Core;
using MuxDeck.Desktop;
using MuxDeck.Desktop.Preferences;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;

// Child modes exercise real pipes, argument escaping, failures and cancellation without external media tools.
if (args.FirstOrDefault() == "--child")
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine(args[1]); Console.Error.WriteLine("stderr 字幕"); return 7;
}
if (args.FirstOrDefault() == "--tree-child")
{
    Console.WriteLine("tree-child-pipe-open");
    Console.Error.WriteLine("tree-child-error-pipe-open");
    Console.Out.Flush(); Console.Error.Flush();
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 0;
}
if (args.FirstOrDefault() == "--long-line")
{
    Console.Out.Write(new string('x', 1024 * 1024));
    Console.Out.Flush();
    return 0;
}
if (args.FirstOrDefault() == "--tree-parent") return await RunTreeParent(exitAfterLaunch: false);
if (args.FirstOrDefault() == "--tree-parent-exit") return await RunTreeParent(exitAfterLaunch: true);
if (args.FirstOrDefault() == "--wait") { await Task.Delay(TimeSpan.FromSeconds(30)); return 0; }
AppComposition.InitializeLegacyComponents();
if (args.FirstOrDefault() == "--integration") return await Phase1Integration.RunAsync();
if (args.FirstOrDefault() == "--single-mux-round1") return SingleMuxRound1Checks.Run();
if (args.FirstOrDefault() == "--single-mux-ui-round1") return SingleMuxUiRound1Checks.Run();
if (args.FirstOrDefault() == "--workbench-ui") return WorkbenchUiChecks.Run();
if (args.FirstOrDefault() == "--environment-ui") return EnvironmentUiChecks.Run();
if (args.FirstOrDefault() == "--components") return await ComponentChecks.RunAsync();
if (args.FirstOrDefault() == "--components-real")
{
    if (args.Length > 2) throw new ArgumentException("用法：--components-real [workspace-root]");
    return await ComponentChecks.RunAsync(real: true, workspaceRoot: args.ElementAtOrDefault(1));
}
if (args.FirstOrDefault() == "--component-install-ui") return ComponentInstallationUiChecks.Run();
if (args.FirstOrDefault() == "--component-install-all") return await ComponentCompletionChecks.RunAsync();
if (args.FirstOrDefault() == "--component-install-all-real") return await ComponentCompletionChecks.RunAsync(real: true);
if (args.FirstOrDefault() == "--component-uninstall") return await ComponentUninstallChecks.RunAsync();
if (args.FirstOrDefault() == "--component-uninstall-ui") return ComponentUninstallUiChecks.Run();
if (args.FirstOrDefault() == "--component-uninstall-real")
{
    if (args.Length != 2) throw new ArgumentException("用法：--component-uninstall-real <固定发布包缓存目录>");
    return await ComponentUninstallChecks.RunAsync(Path.GetFullPath(args[1]));
}
if (args.FirstOrDefault() == "--media-drop") return AutomaticMediaDropChecks.Run();
if (args.FirstOrDefault() == "--input-feedback") return InputFeedbackChecks.Run();
if (args.FirstOrDefault() == "--media-drop-real") return AutomaticMediaDropChecks.Run(real: true);
if (args.FirstOrDefault() == "--warning-policy") return await WarningPolicyChecks.RunAsync();
if (args.FirstOrDefault() == "--warning-real")
{
    if (args.Length > 2) throw new ArgumentException("用法：--warning-real [core-task.log]");
    return await WarningPolicyChecks.RunRealAsync(args.Length == 2 ? args[1] : null);
}
if (args.FirstOrDefault() == "--warning-synthetic-bold-real")
{
    if (args.Length > 2) throw new ArgumentException("用法：--warning-synthetic-bold-real [core-task.log]");
    return await WarningPolicyChecks.RunRealAsync(args.Length == 2 ? args[1] : null, expectSyntheticBold: true);
}
if (args.FirstOrDefault() == "--ui-integration")
{
    if (args.Length != 3) throw new ArgumentException("用法：--ui-integration <fixtureDirectory> <screenshotPath>");
    return RealUiAcceptance.Run(args[1], args[2]);
}

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}

var runner = new ProcessRunner();
var executable = Environment.ProcessPath!;
var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
    ? new[] { System.Reflection.Assembly.GetEntryAssembly()!.Location } : Array.Empty<string>();
var root = Workspace.Discover();
var artifacts = Path.Combine(root.Root, "desktop", "artifacts");
Directory.CreateDirectory(artifacts);
var smokeRun = Path.Combine(artifacts, "smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(smokeRun);
var special = "中文 subtitle \"quoted\" & spaces";
var result = await runner.RunAsync(new ProcessRequest(executable, [.. prefix, "--child", special]));
Check(result.ExitCode == 7 && result.StandardOutput.Trim() == special && result.StandardError.Contains("stderr 字幕"),
    $"进程参数、Unicode、stderr 和非零退出码 (exit={result.ExitCode}, out={result.StandardOutput.Trim()}, err={result.StandardError.Trim()})");
var emittedLineLengths = new List<int>();
var longLineResult = await runner.RunAsync(new ProcessRequest(executable, [.. prefix, "--long-line"],
    MaxCapturedCharacters: 16_384), new InlineProcessProgress(line =>
    {
        if (!line.IsError) emittedLineLengths.Add(line.Text.Length);
    }));
Check(longLineResult.ExitCode == 0 && longLineResult.CaptureLimitExceeded &&
    longLineResult.StandardOutput.Length <= 16_384 && emittedLineLengths.Count > 0 &&
    emittedLineLengths.All(length => length <= 8_192),
    $"1 MiB 无换行输出分块且有界捕获 (exit={longLineResult.ExitCode}, captured={longLineResult.StandardOutput.Length}, limitExceeded={longLineResult.CaptureLimitExceeded}, maxEvent={emittedLineLengths.DefaultIfEmpty().Max()})");
using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
{
    var elapsed = Stopwatch.StartNew();
    try
    {
        await runner.RunAsync(new ProcessRequest(executable, [.. prefix, "--wait"]), cancellationToken: cancellation.Token);
        throw new InvalidOperationException("Expected cancellation");
    }
    catch (OperationCanceledException) { Check(elapsed.Elapsed < TimeSpan.FromSeconds(10), "取消终止子进程"); }
}

Check(new ToolLocator(root).Inspect().Count == 6, "六项工具路径检测");
var fixture = Path.Combine(smokeRun, "tool-locator-fixture");
Directory.CreateDirectory(Path.Combine(fixture, "bin", "mkvtoolnix"));
try
{
    var bundled = Path.Combine(fixture, "bin", "mkvtoolnix", "mkvmerge.exe");
    File.WriteAllText(bundled, "fixture");
    Check(new ToolLocator(new Workspace(fixture)).Inspect()[0].Path == bundled, "便携 bin 优先");
}
finally { Directory.Delete(fixture, recursive: true); }

var environmentRoot = Path.Combine(smokeRun, "environment");
var portableTools = Path.Combine(environmentRoot, "bin");
Directory.CreateDirectory(Path.Combine(portableTools, "mkvtoolnix"));
Directory.CreateDirectory(Path.Combine(portableTools, "assfontsubset"));
Directory.CreateDirectory(Path.Combine(portableTools, "ffmpeg", "bin"));
Directory.CreateDirectory(Path.Combine(portableTools, "assfonts"));
foreach (var path in new[]
{
    Path.Combine(portableTools, "mkvtoolnix", "mkvmerge.exe"),
    Path.Combine(portableTools, "mkvtoolnix", "mkvextract.exe"),
    Path.Combine(portableTools, "mkvtoolnix", "mkvpropedit.exe"),
    Path.Combine(portableTools, "assfontsubset", "AssFontSubset.Console.exe"),
    Path.Combine(portableTools, "ffmpeg", "bin", "ffmpeg.exe"),
    Path.Combine(portableTools, "assfonts", "assfonts.exe")
}) File.WriteAllText(path, "fixture");
var environmentStorePath = Path.Combine(environmentRoot, "settings.json");
var environmentStorage = StoragePaths.ForRoot(Path.Combine(environmentRoot, "data"), portableTools);
var environment = AppComposition.CreateEnvironment(new Workspace(environmentRoot) { ToolsDirectoryOverride = portableTools },
    environmentStorage, environmentStorePath);
Check(environment.CoreComponents.Count == 2 && environment.ExtensionComponents.Count == 2 && environment.Tools.Count == 6,
    "环境页按组件分组并保留六项工具状态");
var customTools = Path.Combine(environmentRoot, "custom-tools");
var customWork = Path.Combine(environmentRoot, "custom-work");
environment.ToolsDirectoryInput = customTools;
environment.WorkDirectoryInput = customWork;
environment.SaveSettingsCommand.Execute(null);
Check(environment.Storage.ToolsDirectory == Path.GetFullPath(customTools) &&
      environment.Storage.AppDataRoot == Path.GetFullPath(customWork) &&
      Directory.Exists(Path.Combine(customWork, "config")) && File.Exists(environmentStorePath),
    "组件安装位置与工作目录可保存并创建子目录");

// All UI operations run on one STA thread after asynchronous core checks complete.
Exception? uiError = null;
var uiThread = new Thread(() =>
{
    try
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
            { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var model = AppComposition.CreateMain(root);
        var window = new MainWindow { DataContext = model };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<SingleMuxView>("SingleView")!;
        var interactions = new ScriptedInteractions();
        view.Interactions = interactions;
        var smokeVideo = Path.Combine(smokeRun, "测试 片源.MKV");
        File.WriteAllText(smokeVideo, "smoke fixture");
        UiInputTestSupport.Pick(window, view, interactions, "PickVideoButton", UiInputTestSupport.File(window, smokeVideo));
        Check(model.VideoPath == smokeVideo && view.FindControl<TextBlock>("VideoName")!.Text == "测试 片源.MKV", "新视频按钮接纳并通过编译绑定展示文件名");
        model.Single.VideoPath = "";
        Dispatcher.UIThread.RunJobs();
        var single = model.Single;
        single.TcSubtitle = Path.Combine(smokeRun, "tc-first.ass");
        Check(single.TcTrack.IsDefault, "仅繁体字幕时默认轨自动启用");
        single.ScSubtitle = Path.Combine(smokeRun, "sc-first.ass");
        Check(single.ScTrack.IsDefault && !single.TcTrack.IsDefault, "简繁同时存在时简体默认、繁体非默认");
        single.ScTrack.Name = "自定义简体轨";
        single.ScTrack.IsDefault = false;
        single.ScTrack.IsForced = true;
        single.TcTrack.Name = "自定义繁体轨";
        single.TcTrack.IsDefault = true;
        single.TcTrack.IsForced = true;
        single.ScSubtitle = Path.Combine(smokeRun, "sc-replaced.ass");
        single.TcSubtitle = Path.Combine(smokeRun, "tc-replaced.ass");
        Check(single.ScTrack.Name == "自定义简体轨" && !single.ScTrack.IsDefault && single.ScTrack.IsForced &&
            single.TcTrack.Name == "自定义繁体轨" && single.TcTrack.IsDefault && single.TcTrack.IsForced,
            "同槽替换字幕路径保留名称与显式旗标");
        single.FontsDirectory = Path.Combine(smokeRun, "fonts");
        single.OutputDirectory = Path.Combine(smokeRun, "output");
        Check(!single.CanStart && !single.StartCommand.CanExecute(null), "缺少视频时不能开始任务");
        model.ShowEnvironmentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check(model.IsEnvironment && model.Tools.Count == 6, "导航到真实环境与组件");
        model.ShowSingleCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check(model.IsSingle &&
            single.ScSubtitle.EndsWith("sc-replaced.ass", StringComparison.Ordinal) &&
            single.TcSubtitle.EndsWith("tc-replaced.ass", StringComparison.Ordinal), "切页保留单任务表单状态");
        UiSingleMuxContractChecks.VerifyRunningSingleVm(smokeRun, Check);
        WarningPolicyChecks.VerifyUiWarningPresentation(smokeRun, (condition, name) => Check(condition, name));
        UiInputTestSupport.Pick(window, view, interactions, "PickVideoButton", UiInputTestSupport.File(window, smokeVideo));
        Dispatcher.UIThread.RunJobs();
        Check(model.VideoPath == smokeVideo && single.CanStart && view.FindControl<Button>("StartButton")!.IsEffectivelyEnabled,
            "新视频入口完成接纳，开始按钮绑定可执行");
        model.ShowEnvironmentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        model.ShowSingleCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check(model.IsSingle && view.FindControl<TextBlock>("VideoName")!.Text == "测试 片源.MKV" && single.ScSubtitle.EndsWith("sc-replaced.ass", StringComparison.Ordinal),
            "切页保留真实新摘要与完整单任务输入");
        single.VideoPath = "";
        Dispatcher.UIThread.RunJobs();
        Check(window.Bounds.Width >= 900 && window.Bounds.Height >= 700, "窗口布局与最小尺寸");
        var screenshotEnvironment = args.FirstOrDefault() is "--environment-screenshot" or "--environment-management-screenshot";
        if (screenshotEnvironment)
        {
            model.ShowEnvironmentCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            if (args.FirstOrDefault() == "--environment-management-screenshot")
                model.Environment.ToggleSettingsCommand.Execute(null);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        if (args.FirstOrDefault() == "--screenshot" || screenshotEnvironment)
        {
            var requested = screenshotEnvironment
                ? args.FirstOrDefault() == "--environment-management-screenshot" ? "environment-management.png" : "environment.png"
                : args.Length == 2 ? args[1] : "window.png";
            var screenshot = Path.GetFullPath(Path.IsPathRooted(requested)
                ? requested : Path.Combine(smokeRun, requested));
            var artifactPrefix = Path.GetFullPath(artifacts) + Path.DirectorySeparatorChar;
            if (!screenshot.StartsWith(artifactPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("截图必须落在 desktop/artifacts/ 下。");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame");
            image.Save(screenshot, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Console.WriteLine("Screenshot: " + screenshot);
        }
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        var closeTimeout = Stopwatch.StartNew();
        while (!closed && closeTimeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Check(closed, "窗口退出等待探测/任务取消后关闭");
    }
    catch (Exception ex) { uiError = ex; }
});
if (OperatingSystem.IsWindows()) uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start(); uiThread.Join();
if (uiError is not null) throw uiError;
Console.WriteLine($"{passed}/{passed} PASS");
return 0;

static async Task<int> RunTreeParent(bool exitAfterLaunch)
{
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法识别 Smoke 执行文件。");
    var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? new[] { System.Reflection.Assembly.GetEntryAssembly()!.Location } : Array.Empty<string>();
    var start = new ProcessStartInfo(executable) { UseShellExecute = false };
    foreach (var argument in prefix.Append("--tree-child")) start.ArgumentList.Add(argument);
    if (!exitAfterLaunch)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
    }
    using var child = Process.Start(start) ?? throw new InvalidOperationException("无法启动进程树子进程。");
    if (exitAfterLaunch)
    {
        Console.WriteLine($"descendant-pid:{child.Id}");
        Console.Out.Flush();
        return 0;
    }

    var childOutput = child.StandardOutput.ReadLineAsync();
    var childError = child.StandardError.ReadLineAsync();
    _ = await childOutput.WaitAsync(TimeSpan.FromSeconds(10));
    _ = await childError.WaitAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine($"descendant-pid:{child.Id}");
    Console.Out.Flush();
    var stdoutDrain = child.StandardOutput.ReadToEndAsync();
    var stderrDrain = child.StandardError.ReadToEndAsync();
    await child.WaitForExitAsync();
    await Task.WhenAll(stdoutDrain, stderrDrain);
    return child.ExitCode;
}

internal sealed class InlineProcessProgress(Action<ProcessLine> callback) : IProgress<ProcessLine>
{
    public void Report(ProcessLine value) => callback(value);
}
